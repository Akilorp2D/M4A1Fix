using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoR2;
using Path = System.IO.Path;

namespace M4A1Fix.Tests
{
    public partial class PatchRegressionTests
    {
        private static readonly Type CurrentGame = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.CurrentRoR2ReviveContract", true);

        [TestMethod]
        public void GameFileValidationAcceptsMemoryLoadedAssembly()
        {
            Assembly memoryLoaded = Assembly.Load(File.ReadAllBytes(typeof(CharacterMaster).Assembly.Location));
            Assert.AreEqual(string.Empty, memoryLoaded.Location);
            object[] args = { memoryLoaded.Location, Path.GetDirectoryName(typeof(CharacterMaster).Assembly.Location), null };
            Assert.IsTrue((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args), (string)args[2]);
            Assert.IsNull(args[2]);
        }

        [TestMethod]
        public void GameFileValidationUsesReportedFileWithoutManagedFallback()
        {
            object[] args = { typeof(CharacterMaster).Assembly.Location, null, null };
            Assert.IsTrue((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args), (string)args[2]);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        public void MemoryLoadedGameWithoutManagedPathFailsClosed(string managedPath)
        {
            object[] args = { string.Empty, managedPath, null };
            Assert.IsFalse((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args));
            StringAssert.Contains((string)args[2], "ManagedPath is unavailable");
        }

        [TestMethod]
        public void MissingReportedGameFileDoesNotFallBackToAnotherFile()
        {
            object[] args = { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "RoR2.dll"),
                Path.GetDirectoryName(typeof(CharacterMaster).Assembly.Location), null };
            Assert.IsFalse((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args));
            StringAssert.Contains((string)args[2], "was not found");
        }

        [TestMethod]
        public void MemoryLoadedGameFallbackRejectsMissingOrUnknownBytes()
        {
            string directory = Path.Combine(Path.GetTempPath(), "m4a1fix-game-path-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "RoR2.dll");
            try
            {
                object[] args = { string.Empty, directory, null };
                Assert.IsFalse((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args));
                StringAssert.Contains((string)args[2], "was not found");
                File.WriteAllText(file, "unknown game bytes");
                Assert.IsFalse((bool)CurrentGame.GetMethod("TryValidateGameFile", StaticPrivate).Invoke(null, args));
                StringAssert.Contains((string)args[2], "SHA-256");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        [DataTestMethod]
        [DataRow("600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154", true)]
        [DataRow("023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F0", true)]
        [DataRow("023b3fe8f86f9dce8fd9f203ba3580d565a9940d2e0e82ab3fa129edc33cc6f0", true)]
        [DataRow("023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F1", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void UpstreamAllowlistRejectsUnknownHashes(string hash, bool accepted)
        {
            Assert.AreEqual(accepted, (bool)Upstream.GetMethod("IsSupportedHash", StaticPrivate).Invoke(null, new object[] { hash }));
        }

        [DataTestMethod]
        [DataRow("A5F7D83C7E6872FDD6099FDC299ADB7D4FA03CBF465E526D6F5EFCC35C2C281D", true)]
        [DataRow("A5F7D83C7E6872FDD6099FDC299ADB7D4FA03CBF465E526D6F5EFCC35C2C281E", false)]
        [DataRow(null, false)]
        public void CurrentGameAllowlistRejectsUnknownHashes(string hash, bool accepted)
        {
            Assert.AreEqual(accepted, (bool)CurrentGame.GetMethod("IsSupportedHash", StaticPrivate).Invoke(null, new object[] { hash }));
        }

        [TestMethod]
        public void InspectedCurrentPairPassesFullCompatibilityGate()
        {
            object[] args = { null, null };
            Assert.IsTrue((bool)Compatibility.GetMethod("TryValidateRevivePriorityTarget", StaticPrivate).Invoke(null, args), (string)args[1]);
            MethodInfo target = (MethodInfo)args[0];
            Assert.AreEqual("CharacterMaster_OnBodyDeath", target.Name);
            Assert.AreEqual(typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).Assembly, target.DeclaringType.Assembly);
        }

        [TestMethod]
        public void CurrentShrineReaderIsTheExactVanillaUnmanagedBuffMethod()
        {
            object[] args = { null, null };
            Assert.IsTrue((bool)CurrentGame.GetMethod("TryValidate", StaticPrivate).Invoke(null, args), (string)args[1]);
            var reader = (Func<CharacterBody, BuffDef, bool>)args[0];
            Assert.AreEqual(typeof(CharacterBody).GetMethod("HasUnmanagedBuff", new[] { typeof(BuffDef) }), reader.Method);
            Assert.IsNull(reader.Target, "Reader must be open-instance, not tied to a stale body.");
        }

        [DataTestMethod]
        [DataRow(0, 0, false)]
        [DataRow(0, 1, false)]
        [DataRow(1, 0, true)]
        [DataRow(2, 0, true)]
        public void CurrentShrineReaderUsesServerStorageWithoutConsumingBuff(int serverCount, int visibleCount, bool expected)
        {
            object[] args = { null, null };
            Assert.IsTrue((bool)CurrentGame.GetMethod("TryValidate", StaticPrivate).Invoke(null, args), (string)args[1]);
            var reader = (Func<CharacterBody, BuffDef, bool>)args[0];
            // These readers use managed arrays only. Do not construct or execute Unity objects.
            var body = (CharacterBody)FormatterServices.GetUninitializedObject(typeof(CharacterBody));
            var buff = (BuffDef)FormatterServices.GetUninitializedObject(typeof(BuffDef));
            typeof(BuffDef).GetProperty("buffIndex").SetValue(buff, (BuffIndex)0);
            FieldInfo storage = typeof(CharacterBody).GetField("unmanagedBuffsServer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            FieldInfo visible = typeof(CharacterBody).GetField("buffs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Array server = Array.CreateInstance(storage.FieldType.GetElementType(), 1);
            Array display = Array.CreateInstance(visible.FieldType.GetElementType(), 1);
            server.SetValue(Convert.ChangeType(serverCount, storage.FieldType.GetElementType()), 0);
            display.SetValue(Convert.ChangeType(visibleCount, visible.FieldType.GetElementType()), 0);
            storage.SetValue(body, server);
            visible.SetValue(body, display);
            Assert.AreEqual(expected, reader(body, buff));
            Assert.AreEqual(serverCount, Convert.ToInt32(server.GetValue(0)), "The fix must leave Shrine consumption to vanilla.");
            Assert.AreEqual(visibleCount, Convert.ToInt32(display.GetValue(0)));
        }

        [TestMethod]
        public void LegacyContractRejectsCurrentGameAndClearsShrineReader()
        {
            Type game = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.RoR2ReviveContract", true);
            object[] current = { true, null };
            Assert.IsTrue((bool)game.GetMethod("TryValidate", StaticPrivate).Invoke(null, current), (string)current[1]);
            object[] legacy = { false, null };
            Assert.IsFalse((bool)game.GetMethod("TryValidate", StaticPrivate).Invoke(null, legacy));
            StringAssert.Contains((string)legacy[1], "IL code-size mismatch");
            Assert.IsNull(game.GetField("_hasShrineBuff", StaticPrivate).GetValue(null));
        }

        [TestMethod]
        public void CurrentPriorityHookInstallsOnRealUpstreamTarget()
        {
            var priority = new Harmony("com.m4a1fix.revivepriority");
            try
            {
                object[] args = { priority, null, null };
                Assert.IsTrue((bool)Priority.GetMethod("TryInstall", StaticPrivate).Invoke(null, args), (string)args[2]);
                MethodInfo death = (MethodInfo)args[1];
                Assert.IsTrue((bool)Priority.GetMethod("IsHealthy", StaticPrivate).Invoke(null, new object[] { death }));
                Assert.AreEqual(1, Harmony.GetPatchInfo(death).Transpilers.Count(p => p.owner == priority.Id));
            }
            finally
            {
                priority.UnpatchSelf();
            }
        }
    }
}
