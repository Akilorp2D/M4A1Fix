using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using M4A1Fix.Compatibility;
using M4A1Fix.Runtime;
using M4A1Fix.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace M4A1Fix.Tests
{
    public partial class PatchRegressionTests
    {
        [TestMethod]
        public void WarmupReadinessYieldsUntilBodyCatalogIsAvailable()
        {
            bool ready = false;
            var gate = RunStartWarmup.WaitForCatalog(() => ready, () => true);
            Assert.IsTrue(gate.MoveNext());
            Assert.IsNull(gate.Current);
            Assert.IsTrue(gate.MoveNext());
            ready = true;
            Assert.IsFalse(gate.MoveNext());
        }

        [TestMethod]
        public void WarmupReadinessStopsWhenRunStartsBeforeCatalogCompletes()
        {
            bool idle = true;
            var gate = RunStartWarmup.WaitForCatalog(() => false, () => idle);
            Assert.IsTrue(gate.MoveNext());
            idle = false;
            Assert.IsFalse(gate.MoveNext());
        }

        [TestMethod]
        public void AlreadyReadyCatalogDoesNotAddAnotherWaitingFrame()
        {
            var gate = RunStartWarmup.WaitForCatalog(() => true, () => true);
            Assert.IsFalse(gate.MoveNext());
        }

        private sealed class WarmupOperation : IAssetWarmupOperation
        {
            public bool IsDone { get; set; } = true;
            public bool Succeeded { get; set; } = true;
            public int Releases { get; private set; }
            public bool ThrowOnRelease { get; set; }
            public void Dispose()
            {
                Releases++;
                if (ThrowOnRelease) throw new InvalidOperationException("release fixture");
            }
        }

        [TestMethod]
        public void WarmupSchedulesBoundedBatchesAndDeduplicatesExactKeys()
        {
            var keys = new List<object>();
            int finalized = 0;
            var job = new AssetWarmupJob(new object[] { "a", "b", "a", "A" }, key =>
            { keys.Add(key); return new WarmupOperation(); }, () => true, () => finalized++, 2);
            Assert.IsTrue(job.MoveNext());
            CollectionAssert.AreEqual(new object[] { "a", "b" }, keys);
            Assert.AreEqual(0, finalized);
            Assert.IsTrue(job.MoveNext());
            CollectionAssert.AreEqual(new object[] { "a", "b", "A" }, keys);
            Assert.AreEqual(3, job.Count);
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(1, finalized);
            Assert.AreEqual(AssetWarmupStatus.Completed, job.Status);
        }

        [TestMethod]
        public void WarmupWaitsAcrossFramesThenFinalizesAndReleasesExactlyOnce()
        {
            var operation = new WarmupOperation { IsDone = false };
            int finalized = 0;
            var job = new AssetWarmupJob(new[] { "key" }, _ => operation, () => true, () =>
            { Assert.AreEqual(0, operation.Releases); finalized++; });
            Assert.IsTrue(job.MoveNext());
            Assert.IsTrue(job.MoveNext());
            Assert.AreEqual(0, finalized);
            operation.IsDone = true;
            Assert.IsFalse(job.MoveNext());
            Assert.IsFalse(job.MoveNext());
            job.Dispose();
            Assert.AreEqual(1, finalized);
            Assert.AreEqual(1, operation.Releases);
        }

        [TestMethod]
        public void WarmupWithNoMissingAssetsFinalizesOnceWithoutLoading()
        {
            int finalized = 0;
            var job = new AssetWarmupJob(Array.Empty<object>(), _ => throw new Exception("must not load"),
                () => true, () => finalized++);
            Assert.IsFalse(job.MoveNext());
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(1, finalized);
            Assert.AreEqual(AssetWarmupStatus.Completed, job.Status);
        }

        [TestMethod]
        public void RunStartingDuringWarmupKeepsOriginalInitializerAndReleasesPendingLoads()
        {
            bool idle = true;
            int finalized = 0;
            var operation = new WarmupOperation { IsDone = false };
            var job = new AssetWarmupJob(new[] { "key" }, _ => operation, () => idle, () => finalized++);
            Assert.IsTrue(job.MoveNext());
            idle = false;
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(AssetWarmupStatus.Skipped, job.Status);
            Assert.AreEqual(0, finalized);
            Assert.AreEqual(1, operation.Releases);
        }

        [TestMethod]
        public void AlreadyStartedRunDoesNotLoadOrInitializeAssets()
        {
            var job = new AssetWarmupJob(new[] { "key" }, _ => throw new Exception("must not load"),
                () => false, () => Assert.Fail("must not initialize"));
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(AssetWarmupStatus.Skipped, job.Status);
        }

        [TestMethod]
        public void FailedAssetPreventsInitializationAndReleasesAllOwnedOperations()
        {
            var operations = new[] { new WarmupOperation { Succeeded = false }, new WarmupOperation() };
            int next = 0;
            var job = new AssetWarmupJob(new[] { "a", "b" }, _ => operations[next++],
                () => true, () => Assert.Fail("failed asset must not initialize cache"));
            Assert.IsTrue(job.MoveNext());
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(AssetWarmupStatus.Failed, job.Status);
            StringAssert.Contains(job.Error, "failed to load");
            Assert.IsTrue(operations.All(o => o.Releases == 1));
        }

        [TestMethod]
        public void LoadExceptionReleasesEarlierOperationAndPreservesDiagnostic()
        {
            var operation = new WarmupOperation();
            var job = new AssetWarmupJob(new[] { "a", "b" }, key => key.ToString() == "a"
                ? operation : throw new InvalidOperationException("load fixture"),
                () => true, () => Assert.Fail("must not initialize"));
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(AssetWarmupStatus.Failed, job.Status);
            Assert.AreEqual(1, operation.Releases);
            StringAssert.Contains(job.Error, "load fixture");
        }

        [TestMethod]
        public void FinalizationExceptionReleasesAssetsAndDoesNotRetry()
        {
            var operation = new WarmupOperation();
            int attempts = 0;
            var job = new AssetWarmupJob(new[] { "key" }, _ => operation, () => true,
                () => { attempts++; throw new InvalidOperationException("finalize fixture"); });
            Assert.IsTrue(job.MoveNext());
            Assert.IsFalse(job.MoveNext());
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(1, operation.Releases);
            Assert.AreEqual(AssetWarmupStatus.Failed, job.Status);
            StringAssert.Contains(job.Error, "finalize fixture");
        }

        [TestMethod]
        public void ExplicitDisposeCancelsAndAttemptsEveryReleaseDespiteException()
        {
            var operations = new[] { new WarmupOperation { ThrowOnRelease = true }, new WarmupOperation() };
            int next = 0;
            var job = new AssetWarmupJob(new[] { "a", "b" }, _ => operations[next++],
                () => true, () => Assert.Fail("disposed job must not initialize"));
            Assert.IsTrue(job.MoveNext());
            job.Dispose();
            Assert.IsFalse(job.MoveNext());
            job.Dispose();
            Assert.IsTrue(operations.All(o => o.Releases == 1));
            Assert.AreEqual(AssetWarmupStatus.Failed, job.Status);
            StringAssert.Contains(job.Error, "release fixture");
        }

        [TestMethod]
        public void NullAssetOperationFailsWithoutInitialization()
        {
            var job = new AssetWarmupJob(new[] { "key" }, _ => null, () => true,
                () => Assert.Fail("must not initialize"));
            Assert.IsFalse(job.MoveNext());
            Assert.AreEqual(AssetWarmupStatus.Failed, job.Status);
            StringAssert.Contains(job.Error, "no operation");
        }

        [TestMethod]
        public void InvalidWarmupBatchSizeAndKeysAreRejected()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new AssetWarmupJob(new[] { "a" },
                _ => new WarmupOperation(), () => true, () => { }, 0));
            Assert.ThrowsException<ArgumentException>(() => new AssetWarmupJob(new object[] { null },
                _ => new WarmupOperation(), () => true, () => { }));
        }

        [TestMethod]
        public void RealItemDisplayInitializerPassesPinnedContract()
        {
            // Resolve/fingerprint only; Unity methods are not executed by the CLR harness.
            _ = typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).Assembly;
            Assert.IsTrue(ItemDisplayWarmupContract.TryResolve(out var contract, out string reason), reason);
            Assert.IsNotNull(contract);
        }

        private static bool _fixtureInitialized;
        private static object _fixturePrefabs, _fixtureRules, _fixtureKeys;
        private static int _fixtureInitCalls;
        private static void FailingCacheInit()
        {
            _fixtureInitialized = true;
            if (_fixtureKeys is Dictionary<string, object> dictionary) dictionary["partial"] = new object();
            _fixturePrefabs = new object();
            _fixtureRules = new object();
            _fixtureKeys = new object();
            _fixtureInitCalls++;
            throw new InvalidOperationException("cache fixture");
        }

        private static ItemDisplayWarmupContract FixtureCacheContract()
        {
            string[] names = { "_fixtureInitialized", "_fixturePrefabs", "_fixtureRules", "_fixtureKeys" };
            return (ItemDisplayWarmupContract)typeof(ItemDisplayWarmupContract)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke(new object[] { typeof(PatchRegressionTests).GetMethod(nameof(FailingCacheInit), StaticPrivate),
                    names.Select(n => typeof(PatchRegressionTests).GetField(n, StaticPrivate)).ToArray() });
        }

        [TestMethod]
        public void FailedOriginalInitializerRestoresAllCacheStateForFallback()
        {
            _fixtureInitialized = false;
            object prefabs = _fixturePrefabs = new object();
            object rules = _fixtureRules = new object();
            var keys = new Dictionary<string, object> { ["existing"] = new object() };
            _fixtureKeys = keys;
            _fixtureInitCalls = 0;
            var contract = FixtureCacheContract();
            Assert.ThrowsException<TargetInvocationException>(() => contract.InitializeCache());
            Assert.IsFalse(contract.IsInitialized);
            Assert.IsFalse(_fixtureInitialized);
            Assert.AreSame(prefabs, _fixturePrefabs);
            Assert.AreSame(rules, _fixtureRules);
            Assert.AreSame(keys, _fixtureKeys);
            Assert.AreEqual(1, keys.Count);
            Assert.IsTrue(keys.ContainsKey("existing"));
            Assert.AreEqual(1, _fixtureInitCalls);
        }

        [TestMethod]
        public void AlreadyInitializedOriginalCacheIsNotInitializedAgain()
        {
            _fixtureInitialized = true;
            _fixtureInitCalls = 0;
            FixtureCacheContract().InitializeCache();
            Assert.AreEqual(0, _fixtureInitCalls);
        }
    }
}
