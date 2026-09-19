using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using M4A1Fix.Compatibility;
using RoR2;
using UnityEngine.Networking;

namespace M4A1Fix.Patches
{
    internal static class RevivePriorityPatch
    {
        private static bool _transpilerMatched;

        internal static bool TryInstall(Harmony harmony, ManualLogSource log, out MethodInfo target, out string reason)
        {
            target = null;
            reason = null;

            if (harmony == null)
            {
                reason = "Harmony owner was not created.";
                return false;
            }

            if (!M4A1Compatibility.TryValidateRevivePriorityTarget(out target, out reason))
                return false;

            _transpilerMatched = false;
            MethodInfo transpiler = AccessTools.Method(typeof(RevivePriorityPatch), nameof(CharacterMasterOnBodyDeathTranspiler));
            if (transpiler == null)
            {
                reason = "revive-priority transpiler method could not be resolved.";
                return false;
            }

            harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            if (!_transpilerMatched || !HasOwnedTranspiler(target, Plugin.PluginGuid))
            {
                harmony.UnpatchSelf();
                reason = "exact M4A1 OnBodyDeath IL pattern did not match; no targeted patch remains installed.";
                return false;
            }

            return true;
        }

        internal static bool IsHealthy(MethodInfo exactTarget)
        {
            return _transpilerMatched && exactTarget != null && HasOwnedTranspiler(exactTarget, Plugin.PluginGuid);
        }

        private static bool ShouldDelegateShrineToVanilla(CharacterBody body)
        {
            return NetworkServer.active && body != null && body.HasBuff(DLC2Content.Buffs.ExtraLifeBuff);
        }

        private static IEnumerable<CodeInstruction> CharacterMasterOnBodyDeathTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            int origInvokeIndex = FindOrigOnBodyDeathInvoke(codes);
            int getDummyLinkIndex = FindGetSomeChildDummyLink(codes);

            if (origInvokeIndex < 3 || getDummyLinkIndex < 1)
                return codes;

            int origCallStart = origInvokeIndex - 3;
            int dummyCallStart = getDummyLinkIndex - 1;

            if (!IsLdarg(codes[origCallStart], 0) ||
                !IsLdarg(codes[origCallStart + 1], 1) ||
                !IsLdarg(codes[origCallStart + 2], 2) ||
                !IsLdarg(codes[dummyCallStart], 2))
            {
                return codes;
            }

            int conditionalBranchIndex = FindConditionalBranchTo(codes, dummyCallStart, origCallStart);
            if (conditionalBranchIndex <= 0 || !IsLocalLoad(codes[conditionalBranchIndex - 1]))
                return codes;

            int decisionLoadIndex = conditionalBranchIndex - 1;
            Label delegateToVanillaLabel = generator.DefineLabel();
            Label continueOriginalDecisionLabel = generator.DefineLabel();

            codes[origCallStart].labels.Add(delegateToVanillaLabel);
            codes[decisionLoadIndex].labels.Add(continueOriginalDecisionLabel);

            CodeInstruction clonedDecisionLoad = CloneWithoutLabelsOrBlocks(codes[decisionLoadIndex]);
            MethodInfo delegateMethod = AccessTools.Method(typeof(RevivePriorityPatch), nameof(ShouldDelegateShrineToVanilla));

            var injected = new List<CodeInstruction>
            {
                clonedDecisionLoad,
                new CodeInstruction(OpCodes.Brtrue, continueOriginalDecisionLabel),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, delegateMethod),
                new CodeInstruction(OpCodes.Brtrue, delegateToVanillaLabel)
            };

            injected[0].labels.AddRange(
                codes[decisionLoadIndex].labels.Where(label => !label.Equals(continueOriginalDecisionLabel)));
            codes[decisionLoadIndex].labels.RemoveAll(label => !label.Equals(continueOriginalDecisionLabel));

            injected[0].blocks.AddRange(codes[decisionLoadIndex].blocks);
            codes[decisionLoadIndex].blocks.Clear();

            codes.InsertRange(decisionLoadIndex, injected);
            _transpilerMatched = true;
            return codes;
        }

        private static int FindOrigOnBodyDeathInvoke(List<CodeInstruction> codes)
        {
            for (int i = 0; i < codes.Count; i++)
            {
                MethodInfo method = codes[i].operand as MethodInfo;
                if (method == null || method.Name != "Invoke" || method.DeclaringType == null ||
                    method.DeclaringType.Name != "orig_OnBodyDeath")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (method.ReturnType == typeof(void) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(CharacterMaster) &&
                    parameters[1].ParameterType == typeof(CharacterBody))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindGetSomeChildDummyLink(List<CodeInstruction> codes)
        {
            for (int i = 0; i < codes.Count; i++)
            {
                MethodInfo method = codes[i].operand as MethodInfo;
                if (method != null &&
                    method.Name == "GetSomeChildDummyLink" &&
                    method.DeclaringType != null &&
                    method.DeclaringType.FullName == M4A1Compatibility.M4A1UtilsTypeName)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindConditionalBranchTo(List<CodeInstruction> codes, int targetIndex, int searchEndExclusive)
        {
            List<Label> targetLabels = codes[targetIndex].labels;
            for (int i = searchEndExclusive - 1; i >= 0; i--)
            {
                if (codes[i].opcode.FlowControl == FlowControl.Cond_Branch &&
                    codes[i].operand is Label label &&
                    targetLabels.Contains(label))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsLdarg(CodeInstruction instruction, int index)
        {
            if (index == 0 && instruction.opcode == OpCodes.Ldarg_0) return true;
            if (index == 1 && instruction.opcode == OpCodes.Ldarg_1) return true;
            if (index == 2 && instruction.opcode == OpCodes.Ldarg_2) return true;
            if (index == 3 && instruction.opcode == OpCodes.Ldarg_3) return true;

            if (instruction.opcode != OpCodes.Ldarg && instruction.opcode != OpCodes.Ldarg_S)
                return false;

            if (instruction.operand is byte byteIndex)
                return byteIndex == index;
            if (instruction.operand is short shortIndex)
                return shortIndex == index;
            if (instruction.operand is ParameterInfo parameter)
                return parameter.Position == index;

            return false;
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                   instruction.opcode == OpCodes.Ldloc_S ||
                   instruction.opcode == OpCodes.Ldloc_0 ||
                   instruction.opcode == OpCodes.Ldloc_1 ||
                   instruction.opcode == OpCodes.Ldloc_2 ||
                   instruction.opcode == OpCodes.Ldloc_3;
        }

        private static CodeInstruction CloneWithoutLabelsOrBlocks(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }

        private static bool HasOwnedTranspiler(MethodBase target, string owner)
        {
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null || patchInfo.Transpilers == null)
                return false;

            foreach (Patch patch in patchInfo.Transpilers)
            {
                if (patch != null && patch.owner == owner)
                    return true;
            }

            return false;
        }
    }
}
