using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using M4A1Fix.Compatibility;
using M4A1Fix.Patches.IL;
using M4A1Fix.Runtime;

namespace M4A1Fix.Patches
{
    internal static class RevivePriorityPatch
    {
        private static bool _transpilerMatched;
        private static readonly MethodInfo TranspilerMethod =
            AccessTools.Method(typeof(RevivePriorityPatch), nameof(CharacterMasterOnBodyDeathTranspiler));

        internal static bool TryInstall(Harmony harmony, out MethodInfo target, out string reason)
        {
            target = null;
            reason = null;
            _transpilerMatched = false;

            if (harmony == null)
            {
                reason = "Harmony owner was not created.";
                return false;
            }

            bool patchAttempted = false;
            try
            {
                if (!M4A1Compatibility.TryValidateRevivePriorityTarget(out target, out reason))
                    return false;

                MethodInfo transpiler = TranspilerMethod;
                if (transpiler == null)
                    throw new InvalidOperationException("revive-priority transpiler method could not be resolved.");

                patchAttempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                if (!IsHealthy(target))
                    throw new InvalidOperationException("revive-priority transpiler was not confirmed on the exact target.");

                return true;
            }
            catch (Exception ex)
            {
                _transpilerMatched = false;
                reason = "installation failed: " + ex;
                if (patchAttempted && !HarmonyPatchLifecycle.TryUnpatch(harmony, out string cleanupReason))
                    reason += " Cleanup failed: " + cleanupReason;
                target = null;
                return false;
            }
        }

        internal static bool IsHealthy(MethodInfo exactTarget)
        {
            return _transpilerMatched && exactTarget != null && HarmonyPatchLifecycle.HasTranspiler(exactTarget, Plugin.PluginGuid, TranspilerMethod);
        }

        private static IEnumerable<CodeInstruction> CharacterMasterOnBodyDeathTranspiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // Harmony reruns transpilers when another owner changes the same target.
            _transpilerMatched = false;
            IEnumerable<CodeInstruction> rewritten = RevivePriorityTranspiler.Rewrite(instructions, generator);
            _transpilerMatched = true;
            return rewritten;
        }
    }
}
