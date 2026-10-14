using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoR2;
using Path = System.IO.Path;

namespace M4A1Fix.Tests
{
    [TestClass]
    [DoNotParallelize]
    public partial class PatchRegressionTests
    {
        private delegate void orig_OnBodyDeath(CharacterMaster master, CharacterBody body);
        private static readonly Type Priority = typeof(Plugin).Assembly.GetType("M4A1Fix.Patches.RevivePriorityPatch", true);
        private static readonly Type Compatibility = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.M4A1Compatibility", true);
        private static readonly Type Reader = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.IL.IlMethodReader", true);
        private static readonly Type Formatter = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.IL.CanonicalMethodFormatter", true);
        private static readonly Type Lifecycle = typeof(Plugin).Assembly.GetType("M4A1Fix.Runtime.HarmonyPatchLifecycle", true);
        private static readonly Type Upstream = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.M4A1AssemblyContract", true);
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        [ClassInitialize]
        public static void ResolveExternalDependencies(TestContext context)
        {
            string[] directories = typeof(PatchRegressionTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>().Select(a => a.Value).ToArray();
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string filename = new AssemblyName(args.Name).Name + ".dll";
                string path = directories.Select(d => Path.Combine(d, filename)).FirstOrDefault(File.Exists);
                return path == null ? null : Assembly.LoadFrom(path);
            };
        }

        private static List<CodeInstruction> ValidStream(ILGenerator generator, OpCode branch)
        {
            Label dummy = generator.DefineLabel();
            var dummyArgument = new CodeInstruction(OpCodes.Ldarg_2);
            dummyArgument.labels.Add(dummy);
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(branch, dummy),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Callvirt, typeof(orig_OnBodyDeath).GetMethod("Invoke")),
                new CodeInstruction(OpCodes.Ret),
                dummyArgument,
                new CodeInstruction(OpCodes.Call, typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).GetMethod("GetSomeChildDummyLink")),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ret)
            };
        }

        private static ILGenerator Generator()
        {
            return new DynamicMethod("RegressionStream", typeof(void), Type.EmptyTypes).GetILGenerator();
        }

        private static List<CodeInstruction> Transpile(List<CodeInstruction> stream, ILGenerator generator)
        {
            return ((IEnumerable<CodeInstruction>)Priority.GetMethod("CharacterMasterOnBodyDeathTranspiler", StaticPrivate)
                .Invoke(null, new object[] { stream, generator })).ToList();
        }

        private static bool Matched => (bool)Priority.GetField("_transpilerMatched", StaticPrivate).GetValue(null);

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MatchingBranchPreservesEntryLabelsAndExceptionBlocks(bool shortBranch)
        {
            ILGenerator generator = Generator();
            List<CodeInstruction> stream = ValidStream(generator, shortBranch ? OpCodes.Brfalse_S : OpCodes.Brfalse);
            Label entry = generator.DefineLabel();
            stream[0].labels.Add(entry);
            stream[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            CodeInstruction originalDecision = stream[0];
            CodeInstruction originalCallback = stream[2];
            List<CodeInstruction> result = Transpile(stream, generator);

            Assert.AreEqual(stream.Count + 5, result.Count);
            Assert.IsTrue(Matched);
            CollectionAssert.Contains(result[0].labels, entry);
            Assert.AreEqual(ExceptionBlockType.BeginExceptionBlock, result[0].blocks.Single().blockType);
            Assert.AreEqual(0, originalDecision.blocks.Count);
            Assert.IsFalse(originalDecision.labels.Contains(entry));
            Assert.AreEqual(OpCodes.Brtrue, result[1].opcode);
            CollectionAssert.Contains(originalDecision.labels, (Label)result[1].operand);
            Assert.AreEqual("ShouldDelegateShrineToVanilla", ((MethodInfo)result[3].operand).Name);
            CollectionAssert.Contains(originalCallback.labels, (Label)result[4].operand);
            Assert.AreEqual(1, result.Count(c => c.operand is MethodInfo m && m.Name == "ShouldDelegateShrineToVanilla"));
        }

        [TestMethod]
        public void FailedSecondPassClearsPreviousSuccess()
        {
            ILGenerator generator = Generator();
            Transpile(ValidStream(generator, OpCodes.Brfalse), generator);
            Assert.IsTrue(Matched);
            TargetInvocationException error = Assert.ThrowsException<TargetInvocationException>(
                () => Transpile(new List<CodeInstruction>(), Generator()));
            Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
            StringAssert.Contains(error.InnerException.Message, "IL pattern mismatch");
            Assert.IsFalse(Matched);
        }

        [TestMethod]
        public void OppositeBranchIsRejectedWithoutMutatingInput()
        {
            ILGenerator generator = Generator();
            List<CodeInstruction> stream = ValidStream(generator, OpCodes.Brtrue);
            int labels = stream.Sum(c => c.labels.Count);
            TargetInvocationException error = Assert.ThrowsException<TargetInvocationException>(() => Transpile(stream, generator));
            StringAssert.Contains(error.InnerException.Message, "brfalse decision changed");
            Assert.IsFalse(Matched);
            Assert.AreEqual(labels, stream.Sum(c => c.labels.Count));
        }

        [TestMethod]
        public void AmbiguousDummySelectionIsRejected()
        {
            ILGenerator generator = Generator();
            List<CodeInstruction> stream = ValidStream(generator, OpCodes.Brfalse);
            stream.Add(new CodeInstruction(stream[8]));
            TargetInvocationException error = Assert.ThrowsException<TargetInvocationException>(() => Transpile(stream, generator));
            StringAssert.Contains(error.InnerException.Message, "ambiguous");
            Assert.IsFalse(Matched);
        }

        [TestMethod]
        public void ChangedArgumentLoadIsRejected()
        {
            ILGenerator generator = Generator();
            List<CodeInstruction> stream = ValidStream(generator, OpCodes.Brfalse);
            stream[3] = new CodeInstruction(OpCodes.Ldarg_2);
            TargetInvocationException error = Assert.ThrowsException<TargetInvocationException>(() => Transpile(stream, generator));
            StringAssert.Contains(error.InnerException.Message, "argument loads changed");
            Assert.IsFalse(Matched);
        }

        [TestMethod]
        public void MissingOwnerReturnsFailureAndClearsPreviousMatch()
        {
            ILGenerator generator = Generator();
            Transpile(ValidStream(generator, OpCodes.Brfalse), generator);
            object[] args = { null, null, null };
            bool installed = (bool)Priority.GetMethod("TryInstall", StaticPrivate).Invoke(null, args);
            Assert.IsFalse(installed);
            Assert.IsNull(args[1]);
            Assert.AreEqual("Harmony owner was not created.", args[2]);
            Assert.IsFalse(Matched);
        }

        [TestMethod]
        public void CleanupExceptionReturnsDiagnosticInsteadOfSuccess()
        {
            object[] args = { null, null };
            bool removed = (bool)Lifecycle.GetMethod("TryUnpatch", StaticPrivate).Invoke(null, args);
            Assert.IsFalse(removed);
            StringAssert.Contains((string)args[1], "NullReferenceException");
        }

        private static void LifecycleTarget() { }

        private static bool SupplyPriorityTarget(ref MethodInfo target, ref string reason, ref bool __result)
        {
            target = typeof(PatchRegressionTests).GetMethod(nameof(LifecycleTarget), StaticPrivate);
            reason = null;
            __result = true;
            return false;
        }

        private static bool SupplyHealthyPriority(ref bool __result)
        {
            __result = true;
            return false;
        }

        private static bool SupplyUpstreamAssembly(ref Assembly assembly, ref string reason, ref bool __result)
        {
            assembly = typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).Assembly;
            reason = null;
            __result = true;
            return false;
        }

        private static void RejectRecoveryPatch(Harmony __instance)
        {
            if (__instance.Id == "com.m4a1fix.revivepriority/shrine-ai-recovery")
                throw new InvalidOperationException("Injected recovery installation failure");
        }

        private static void RejectRecoveryCleanup(string __0)
        {
            if (__0 == "com.m4a1fix.revivepriority/shrine-ai-recovery")
                throw new InvalidOperationException("Injected recovery rollback failure");
        }

        private static HarmonyMethod Prefix(string name)
        {
            return new HarmonyMethod(typeof(PatchRegressionTests).GetMethod(name, StaticPrivate));
        }

        [TestMethod]
        public void TranspilerInstallationExceptionRollsBackOwnedPatch()
        {
            // Replace only the compatibility gate in this isolated test process.
            // The real Harmony transpiler must reject the deliberately invalid target.
            var faults = new Harmony("m4a1fix.tests.installation-fault");
            MethodInfo target = typeof(PatchRegressionTests).GetMethod(nameof(LifecycleTarget), StaticPrivate);
            var owner = new Harmony("com.m4a1fix.revivepriority");
            try
            {
                faults.Patch(Compatibility.GetMethod("TryValidateRevivePriorityTarget", StaticPrivate), prefix: Prefix(nameof(SupplyPriorityTarget)));
                object[] args = { owner, null, null };
                bool installed = (bool)Priority.GetMethod("TryInstall", StaticPrivate).Invoke(null, args);
                Assert.IsFalse(installed);
                Assert.IsNull(args[1]);
                StringAssert.Contains((string)args[2], "IL pattern mismatch");
                Assert.IsFalse(Matched);
                HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
                Assert.IsTrue(info == null || info.Transpilers.All(p => p.owner != owner.Id), "Failed installation left its transpiler registered.");
            }
            finally
            {
                faults.UnpatchSelf();
                owner.UnpatchSelf();
            }
        }

        [TestMethod]
        public void RecoveryRollbackFailureRetainsOwnerForCleanupRetry()
        {
            var faults = new Harmony("m4a1fix.tests.recovery-fault");
            Type recovery = typeof(Plugin).Assembly.GetType("M4A1Fix.Patches.ShrineAiRecoveryPatch", true);
            MethodInfo death = typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).Assembly
                .GetType("M4A1Mod.Survivors.M4A1.Hooks.DummyLink", true)
                .GetMethod("CharacterMaster_OnBodyDeath", StaticPrivate | BindingFlags.Public);
            object[] args = { null, death, null, null };
            try
            {
                faults.Patch(Priority.GetMethod("IsHealthy", StaticPrivate), prefix: Prefix(nameof(SupplyHealthyPriority)));
                faults.Patch(Upstream.GetMethod("TryValidateLoadedPlugin", StaticPrivate), prefix: Prefix(nameof(SupplyUpstreamAssembly)));
                MethodInfo patch = typeof(Harmony).GetMethods().Single(m => m.Name == nameof(Harmony.Patch) && m.GetParameters().Length == 6);
                faults.Patch(patch, prefix: Prefix(nameof(RejectRecoveryPatch)));
                // UnpatchSelf is a tiny wrapper and can already be inlined by the CLR.
                faults.Patch(typeof(Harmony).GetMethod("UnpatchID"), prefix: Prefix(nameof(RejectRecoveryCleanup)));
                bool installed = (bool)recovery.GetMethod("TryInstall", StaticPrivate).Invoke(null, args);
                Assert.IsFalse(installed);
                Assert.IsNotNull(args[2], (string)args[3]);
                Assert.AreEqual("com.m4a1fix.revivepriority/shrine-ai-recovery", ((Harmony)args[2]).Id);
                StringAssert.Contains((string)args[3], "Injected recovery installation failure");
                StringAssert.Contains((string)args[3], "Injected recovery rollback failure");
                Assert.IsNull(recovery.GetField("_revivePriorityTarget", StaticPrivate).GetValue(null));
            }
            finally
            {
                faults.UnpatchSelf();
                if (args[2] is Harmony owner)
                {
                    object[] retry = { owner, null };
                    Assert.IsTrue((bool)Lifecycle.GetMethod("TryUnpatch", StaticPrivate).Invoke(null, retry), (string)retry[1]);
                }
            }
        }

        [TestMethod]
        public void UnhealthyPriorityPreventsAiRecoveryInstallation()
        {
            Priority.GetField("_transpilerMatched", StaticPrivate).SetValue(null, false);
            Type recovery = typeof(Plugin).Assembly.GetType("M4A1Fix.Patches.ShrineAiRecoveryPatch", true);
            object[] args = { null, null, null, null };
            bool installed = (bool)recovery.GetMethod("TryInstall", StaticPrivate).Invoke(null, args);
            Assert.IsFalse(installed);
            Assert.IsNull(args[2]);
            StringAssert.Contains((string)args[3], "RevivePriority is not healthy");
        }

        [DataTestMethod]
        [DataRow("TryReviveOnBodyDeath")]
        [DataRow("RespawnExtraLifeShrine")]
        [DataRow("IsExtraLifePendingServer")]
        public void DecoderReusePreservesCanonicalBytesOfReleasedImplementation(string name)
        {
            Assembly baseline;
            using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baseline.zip")))
            using (Stream input = zip.GetEntry("plugins/M4A1Fix/M4A1Fix.dll").Open())
            using (var bytes = new MemoryStream())
            {
                input.CopyTo(bytes);
                baseline = Assembly.Load(bytes.ToArray());
            }
            MethodInfo target = typeof(CharacterMaster).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            int size = target.GetMethodBody().GetILAsByteArray().Length;
            Type oldCompatibility = baseline.GetType("M4A1Fix.Compatibility.M4A1Compatibility", true);
            object[] oldArgs = { target, size, "comparison", null, null };
            Assert.IsTrue((bool)oldCompatibility.GetMethod("TryBuildCanonicalMethod", StaticPrivate).Invoke(null, oldArgs), (string)oldArgs[4]);
            object[] decode = { target, size, "comparison", null, null };
            Assert.IsTrue((bool)Reader.GetMethod("TryRead", StaticPrivate).Invoke(null, decode), (string)decode[4]);
            object[] newArgs = { target, size, "comparison", decode[3], null, null };
            Assert.IsTrue((bool)Formatter.GetMethod("TryBuildCanonicalMethod", StaticPrivate).Invoke(null, newArgs), (string)newArgs[5]);
            Assert.AreEqual((string)oldArgs[3], (string)newArgs[4]);
        }
    }
}
