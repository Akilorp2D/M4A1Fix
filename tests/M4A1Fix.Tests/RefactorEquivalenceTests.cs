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
    public partial class PatchRegressionTests
    {
        private static Assembly LoadReleasedPlugin()
        {
            using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baseline.zip")))
            using (Stream input = zip.GetEntry("plugins/M4A1Fix/M4A1Fix.dll").Open())
            using (var bytes = new MemoryStream())
            {
                input.CopyTo(bytes);
                return Assembly.Load(bytes.ToArray());
            }
        }

        private static string Canonical(MethodInfo method)
        {
            int size = method.GetMethodBody().GetILAsByteArray().Length;
            object[] decode = { method, size, "equivalence", null, null };
            Assert.IsTrue((bool)Reader.GetMethod("TryRead", StaticPrivate).Invoke(null, decode), (string)decode[4]);
            object[] format = { method, size, "equivalence", decode[3], null, null };
            Assert.IsTrue((bool)Formatter.GetMethod("TryBuildCanonicalMethod", StaticPrivate).Invoke(null, format), (string)format[5]);
            return (string)format[4];
        }

        private static string[] InstructionGraph(List<CodeInstruction> codes)
        {
            return codes.Select(code =>
            {
                string operand;
                if (code.operand is Label label)
                    operand = "BR@" + codes.FindIndex(c => c.labels.Contains(label));
                else if (code.operand is MethodInfo method)
                    operand = method.Name == "ShouldDelegateShrineToVanilla"
                        ? "ShrinePolicy" : method.DeclaringType.FullName + "." + method;
                else
                    operand = Convert.ToString(code.operand);
                return code.opcode + " " + operand + " EH=" + string.Join(",", code.blocks.Select(b => b.blockType));
            }).ToArray();
        }

        [TestMethod]
        public void RewrittenInstructionGraphMatchesStableRelease()
        {
            ILGenerator originalGenerator = Generator();
            List<CodeInstruction> originalStream = ValidStream(originalGenerator, OpCodes.Brfalse);
            originalStream[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            Type originalPriority = LoadReleasedPlugin().GetType("M4A1Fix.Patches.RevivePriorityPatch", true);
            var originalResult = ((IEnumerable<CodeInstruction>)originalPriority
                .GetMethod("CharacterMasterOnBodyDeathTranspiler", StaticPrivate)
                .Invoke(null, new object[] { originalStream, originalGenerator })).ToList();

            ILGenerator generator = Generator();
            List<CodeInstruction> stream = ValidStream(generator, OpCodes.Brfalse);
            stream[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            CollectionAssert.AreEqual(InstructionGraph(originalResult), InstructionGraph(Transpile(stream, generator)));
        }

        [TestMethod]
        public void ServerAndBodyGuardsRemainBeforeVersionSpecificShrineReader()
        {
            MethodInfo original = LoadReleasedPlugin().GetType("M4A1Fix.Patches.RevivePriorityPatch", true)
                .GetMethod("ShouldDelegateShrineToVanilla", StaticPrivate);
            MethodInfo current = typeof(Plugin).Assembly.GetType("M4A1Fix.Patches.RevivePriorityPolicy", true)
                .GetMethod("ShouldDelegateShrineToVanilla", StaticPrivate);
            // Compatibility intentionally changes the buff reader, not the server/body gates.
            var calls = PatchProcessor.GetOriginalInstructions(current).Where(c => c.operand is MethodInfo)
                .Select(c => ((MethodInfo)c.operand).Name).ToArray();
            CollectionAssert.AreEqual(new[] { "get_active", "op_Inequality", "HasShrineBuff" }, calls);
            var oldCalls = PatchProcessor.GetOriginalInstructions(original).Where(c => c.operand is MethodInfo)
                .Select(c => ((MethodInfo)c.operand).Name).Take(2).ToArray();
            CollectionAssert.AreEqual(oldCalls, calls.Take(2).ToArray());
            Assert.AreEqual(typeof(bool), current.ReturnType);
            Assert.AreEqual(typeof(CharacterBody), current.GetParameters().Single().ParameterType);
        }

        [DataTestMethod]
        [DataRow(false, false, false)]
        [DataRow(false, false, true)]
        [DataRow(false, true, false)]
        [DataRow(false, true, true)]
        [DataRow(true, false, false)]
        [DataRow(true, false, true)]
        [DataRow(true, true, false)]
        [DataRow(true, true, true)]
        public void RewrittenDecisionChoosesExactlyOneExistingPath(bool originalVanillaDecision, bool server, bool shrine)
        {
            ILGenerator generator = Generator();
            List<CodeInstruction> result = Transpile(ValidStream(generator, OpCodes.Brfalse), generator);
            bool shouldUseVanilla = originalVanillaDecision || (server && shrine);
            (int vanilla, int dummy) = TraceDecision(result, originalVanillaDecision, server && shrine);
            Assert.AreEqual(shouldUseVanilla ? 1 : 0, vanilla);
            Assert.AreEqual(shouldUseVanilla ? 0 : 1, dummy);
            Assert.AreEqual(1, vanilla + dummy, "The rewrite must not invoke both existing paths.");
        }

        private static (int vanilla, int dummy) TraceDecision(List<CodeInstruction> codes, bool originalDecision, bool shrinePolicy)
        {
            // Interpret only the generated decision graph. No game methods or Unity objects run.
            var stack = new Stack<object>();
            int vanilla = 0;
            int dummy = 0;
            for (int index = 0, budget = 100; index < codes.Count && budget-- > 0; index++)
            {
                CodeInstruction code = codes[index];
                if (code.opcode == OpCodes.Ldloc_0) stack.Push(originalDecision);
                else if (code.opcode == OpCodes.Ldarg_0 || code.opcode == OpCodes.Ldarg_1 || code.opcode == OpCodes.Ldarg_2) stack.Push(null);
                else if (code.opcode == OpCodes.Brtrue || code.opcode == OpCodes.Brfalse)
                {
                    bool condition = (bool)stack.Pop();
                    if (condition == (code.opcode == OpCodes.Brtrue))
                    {
                        int destination = codes.FindIndex(c => c.labels.Contains((Label)code.operand));
                        Assert.IsTrue(destination >= 0);
                        index = destination - 1;
                    }
                }
                else if (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt)
                {
                    string name = ((MethodInfo)code.operand).Name;
                    if (name == "ShouldDelegateShrineToVanilla") { stack.Pop(); stack.Push(shrinePolicy); }
                    else if (name == "Invoke") { stack.Pop(); stack.Pop(); stack.Pop(); vanilla++; }
                    else if (name == "GetSomeChildDummyLink") { stack.Pop(); stack.Push(new object()); dummy++; }
                    else Assert.Fail("Unexpected call: " + name);
                }
                else if (code.opcode == OpCodes.Pop) stack.Pop();
                else if (code.opcode == OpCodes.Ret)
                {
                    Assert.AreEqual(0, stack.Count, "Both routes must leave a balanced stack.");
                    return (vanilla, dummy);
                }
                else Assert.Fail("Unexpected opcode: " + code.opcode);
            }
            Assert.Fail("Decision did not reach a return within the instruction budget.");
            return (vanilla, dummy);
        }

        [TestMethod]
        public void SupportedUpstreamDeathSignatureAndRawFingerprintStillMatch()
        {
            object[] args = { typeof(M4A1Mod.Survivors.M4A1.Utils.M4A1).Assembly, null, null };
            Assert.IsTrue((bool)Upstream.GetMethod("TryGetDeathHook", StaticPrivate).Invoke(null, args), (string)args[2]);
            MethodInfo target = (MethodInfo)args[1];
            Assert.IsTrue(target.IsStatic);
            Assert.AreEqual(typeof(void), target.ReturnType);
            Assert.AreEqual(3, target.GetParameters().Length);
            Assert.AreEqual(typeof(CharacterMaster), target.GetParameters()[1].ParameterType);
            Assert.AreEqual(typeof(CharacterBody), target.GetParameters()[2].ParameterType);
        }

        [TestMethod]
        public void CanonicalHashMismatchFailsClosed()
        {
            Type fingerprint = typeof(Plugin).Assembly.GetType("M4A1Fix.Compatibility.IL.MethodFingerprint", true);
            MethodInfo target = typeof(PatchRegressionTests).GetMethod(nameof(LifecycleTarget), StaticPrivate);
            object[] args = { target, target.GetMethodBody().GetILAsByteArray().Length, new string('0', 64), "fixture", null, null };
            Assert.IsFalse((bool)fingerprint.GetMethod("TryValidate", StaticPrivate).Invoke(null, args));
            StringAssert.Contains((string)args[5], "canonical IL SHA-256 mismatch");
        }

        private static int ExceptionFixture(int value)
        {
            try
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                switch (value)
                {
                    case 0: return 10;
                    case 1: return 20;
                    case 2: return 30;
                    default: return value + 1;
                }
            }
            catch (ArgumentOutOfRangeException) { return -1; }
            finally { GC.KeepAlive(value); }
        }

        [TestMethod]
        public void ExceptionRegionFormattingMatchesReleasedImplementation()
        {
            MethodInfo target = typeof(PatchRegressionTests).GetMethod(nameof(ExceptionFixture), StaticPrivate);
            Type original = LoadReleasedPlugin().GetType("M4A1Fix.Compatibility.M4A1Compatibility", true);
            object[] args = { target, target.GetMethodBody().GetILAsByteArray().Length, "equivalence", null, null };
            Assert.IsTrue((bool)original.GetMethod("TryBuildCanonicalMethod", StaticPrivate).Invoke(null, args), (string)args[4]);
            string actual = Canonical(target);
            Assert.AreEqual((string)args[3], actual);
            StringAssert.Contains(actual, "K=CATCH");
            StringAssert.Contains(actual, "K=FINALLY");
        }

        [TestMethod]
        public void MethodSizeMismatchFailsClosed()
        {
            MethodInfo target = typeof(PatchRegressionTests).GetMethod(nameof(ExceptionFixture), StaticPrivate);
            int actualSize = target.GetMethodBody().GetILAsByteArray().Length;
            object[] args = { target, actualSize + 1, "fixture", null, null };
            Assert.IsFalse((bool)Reader.GetMethod("TryRead", StaticPrivate).Invoke(null, args));
            Assert.IsNull(args[3]);
            StringAssert.Contains((string)args[4], "IL code-size mismatch");
        }
    }
}
