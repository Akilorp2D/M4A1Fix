using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using M4A1Fix.Compatibility;
using RoR2;

namespace M4A1Fix.Patches.IL
{
    // Only this component understands the transformed death-hook instruction stream.
    internal static class RevivePriorityTranspiler
    {
        private const int OriginalCallbackArgumentCount = 3;
        private const int BodyArgumentIndex = 2;

        private readonly struct DeathHookDecision
        {
            internal DeathHookDecision(int originalCallStart, int decisionLoadIndex)
            {
                OriginalCallStart = originalCallStart;
                DecisionLoadIndex = decisionLoadIndex;
            }

            internal int OriginalCallStart { get; }
            internal int DecisionLoadIndex { get; }
        }

        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            DeathHookDecision decision = FindDecision(codes);
            InjectShrineDecision(codes, generator, decision);
            return codes;
        }

        private static DeathHookDecision FindDecision(List<CodeInstruction> codes)
        {
            int origInvokeIndex = FindOrigOnBodyDeathInvoke(codes);
            int getDummyLinkIndex = FindGetSomeChildDummyLink(codes);

            if (origInvokeIndex < OriginalCallbackArgumentCount || getDummyLinkIndex < 1)
                throw PatternMismatch("original callback or Dummy Link call is missing");

            int origCallStart = origInvokeIndex - OriginalCallbackArgumentCount;
            int dummyCallStart = getDummyLinkIndex - 1;

            if (origCallStart >= dummyCallStart)
                throw PatternMismatch("original callback does not precede Dummy Link selection");

            if (!IsLdarg(codes[origCallStart], 0) ||
                !IsLdarg(codes[origCallStart + 1], 1) ||
                !IsLdarg(codes[origCallStart + 2], BodyArgumentIndex) ||
                !IsLdarg(codes[dummyCallStart], BodyArgumentIndex))
            {
                throw PatternMismatch("callback or Dummy Link argument loads changed");
            }

            int conditionalBranchIndex = FindConditionalBranchTo(codes, dummyCallStart, origCallStart);
            if (conditionalBranchIndex <= 0 || !IsLocalLoad(codes[conditionalBranchIndex - 1]) ||
                (codes[conditionalBranchIndex].opcode != OpCodes.Brfalse &&
                 codes[conditionalBranchIndex].opcode != OpCodes.Brfalse_S))
            {
                throw PatternMismatch("expected local Boolean/brfalse decision changed");
            }

            int decisionLoadIndex = conditionalBranchIndex - 1;
            return new DeathHookDecision(origCallStart, decisionLoadIndex);
        }

        private static void InjectShrineDecision(List<CodeInstruction> codes,
            ILGenerator generator, DeathHookDecision decision)
        {
            // Preserve the upstream non-Dummy route. Only the player/Dummy route gets
            // a second choice: Shrine delegates to the existing vanilla callback.
            // This is the C# decision documented in HOW_THE_FIXES_WORK.md.

            Label delegateToVanillaLabel = generator.DefineLabel();
            Label continueOriginalDecisionLabel = generator.DefineLabel();

            codes[decision.OriginalCallStart].labels.Add(delegateToVanillaLabel);
            codes[decision.DecisionLoadIndex].labels.Add(continueOriginalDecisionLabel);

            CodeInstruction clonedDecisionLoad = CloneWithoutLabelsOrBlocks(codes[decision.DecisionLoadIndex]);
            MethodInfo delegateMethod = AccessTools.Method(typeof(RevivePriorityPolicy), nameof(RevivePriorityPolicy.ShouldDelegateShrineToVanilla));
            if (delegateMethod == null)
                throw PatternMismatch("Shrine decision helper is missing");

            var injected = new List<CodeInstruction>
            {
                clonedDecisionLoad,
                new CodeInstruction(OpCodes.Brtrue, continueOriginalDecisionLabel),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, delegateMethod),
                new CodeInstruction(OpCodes.Brtrue, delegateToVanillaLabel)
            };

            MoveEntryMetadata(codes[decision.DecisionLoadIndex], injected[0], continueOriginalDecisionLabel);

            codes.InsertRange(decision.DecisionLoadIndex, injected);
        }

        private static void MoveEntryMetadata(CodeInstruction originalDecisionLoad,
            CodeInstruction newEntry, Label continueOriginalDecisionLabel)
        {
            // Incoming jumps and exception boundaries must enter the new decision,
            // while the internal continuation re-enters the original local load.
            newEntry.labels.AddRange(
                originalDecisionLoad.labels.Where(label => !label.Equals(continueOriginalDecisionLabel)));
            originalDecisionLoad.labels.RemoveAll(label => !label.Equals(continueOriginalDecisionLabel));

            newEntry.blocks.AddRange(originalDecisionLoad.blocks);
            originalDecisionLoad.blocks.Clear();
        }

        private static InvalidOperationException PatternMismatch(string detail)
        {
            return new InvalidOperationException("M4A1 revive-priority IL pattern mismatch: " + detail + ".");
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
            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                MethodInfo method = codes[i].operand as MethodInfo;
                if (method != null &&
                    method.Name == "GetSomeChildDummyLink" &&
                    method.DeclaringType != null &&
                    method.DeclaringType.FullName == M4A1AssemblyContract.UtilsTypeName)
                {
                    if (match >= 0)
                        throw PatternMismatch("Dummy Link selection is ambiguous");
                    match = i;
                }
            }

            return match;
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
    }
}
