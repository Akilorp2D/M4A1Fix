using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using M4A1Fix.Compatibility;
using M4A1Fix.Runtime;
using RoR2;
using UnityEngine.Networking;

namespace M4A1Fix.Patches
{
    internal static class ShrineAiRecoveryPatch
    {
        private const string M4A1BodyName = "M4A1Body";
        private const string DummyLinkDeathMethodName = "CharacterMaster_OnBodyDeath";
        private const string ShrineRespawnMethodName = "RespawnExtraLifeShrine";
        private const string HarmonyOwner = Plugin.PluginGuid + "/shrine-ai-recovery";

        private static ManualLogSource _log;
        private static MethodInfo _revivePriorityTarget;

        internal static bool TryInstall(
            ManualLogSource log,
            MethodInfo revivePriorityTarget,
            out Harmony harmony,
            out string reason)
        {
            _log = log;
            harmony = null;
            reason = null;
            _revivePriorityTarget = null;

            try
            {
                if (!RevivePriorityPatch.IsHealthy(revivePriorityTarget))
                {
                    reason = "RevivePriority is not healthy on the exact M4A1 death target.";
                    return false;
                }

                if (!M4A1AssemblyContract.TryValidateLoadedPlugin(out Assembly m4a1Assembly, out reason))
                    return false;

                Type dummyLinkType = m4a1Assembly.GetType(M4A1AssemblyContract.DummyLinkHooksTypeName, false);
                if (dummyLinkType == null)
                {
                    reason = "exact M4A1 DummyLink type is missing.";
                    return false;
                }

                if (!MethodLookup.TryResolveSingleDeclaredMethod(dummyLinkType, DummyLinkDeathMethodName, out MethodInfo deathTarget, out reason))
                    return false;

                if (!deathTarget.Equals(revivePriorityTarget) || !RevivePriorityPatch.IsHealthy(deathTarget))
                {
                    reason = "RevivePriority is not installed on the exact M4A1 CharacterMaster_OnBodyDeath target.";
                    return false;
                }

                if (!MethodLookup.TryResolveSingleDeclaredMethod(typeof(CharacterMaster), ShrineRespawnMethodName, out MethodInfo shrineTarget, out reason))
                    return false;

                MethodInfo postfix = typeof(ShrineAiRecoveryPatch).GetMethod(
                    nameof(RespawnExtraLifeShrinePostfix), BindingFlags.NonPublic | BindingFlags.Static);
                if (postfix == null)
                {
                    reason = "ShrineAIRecovery postfix method could not be resolved.";
                    return false;
                }

                harmony = new Harmony(HarmonyOwner);
                _revivePriorityTarget = revivePriorityTarget;
                harmony.Patch(shrineTarget, postfix: new HarmonyMethod(postfix));
                if (!HarmonyPatchLifecycle.HasPostfix(shrineTarget, HarmonyOwner))
                {
                    throw new InvalidOperationException("Harmony did not report the ShrineAIRecovery postfix after installation.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _revivePriorityTarget = null;
                reason = "installation exception: " + ex;
                if (harmony == null || HarmonyPatchLifecycle.TryUnpatch(harmony, out string cleanupReason))
                    harmony = null;
                else
                    reason += " Cleanup failed: " + cleanupReason;
                // Keep the owner handle when rollback fails so OnDestroy can retry.
                return false;
            }
        }

        private static void RespawnExtraLifeShrinePostfix(CharacterMaster __instance)
        {
            if (!NetworkServer.active || __instance == null)
                return;

            CharacterBody revivedBody = __instance.GetBody();
            if (revivedBody == null || revivedBody.healthComponent == null || !revivedBody.healthComponent.alive)
                return;

            BodyIndex exactM4A1BodyIndex = BodyCatalog.FindBodyIndex(M4A1BodyName);
            if (exactM4A1BodyIndex == BodyIndex.None || revivedBody.bodyIndex != exactM4A1BodyIndex)
                return;

            try
            {
                if (!RevivePriorityPatch.IsHealthy(_revivePriorityTarget))
                {
                    if (_log != null)
                        _log.LogError("ShrineAIRecovery skipped: RevivePriority is no longer healthy on the exact death target.");
                    return;
                }
                M4A1Mod.Survivors.M4A1.Utils.M4A1.ReassignDummyLinks(revivedBody);
                if (_log != null)
                    _log.LogInfo("ShrineAIRecovery: dummy leaders reassigned after Shrine of Shaping revive.");
            }
            catch (Exception ex)
            {
                if (_log != null)
                    _log.LogError("ShrineAIRecovery repair failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
