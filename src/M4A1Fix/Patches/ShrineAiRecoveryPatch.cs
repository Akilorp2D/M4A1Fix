using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using M4A1Fix.Compatibility;
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

        private const BindingFlags DeclaredMethodFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static ManualLogSource _log;

        internal static bool TryInstall(
            ManualLogSource log,
            MethodInfo revivePriorityTarget,
            out Harmony harmony,
            out string reason)
        {
            _log = log;
            harmony = null;
            reason = null;

            if (!RevivePriorityPatch.IsHealthy(revivePriorityTarget))
            {
                reason = "RevivePriority is not healthy on the exact M4A1 death target.";
                return false;
            }

            if (!TryValidateExactM4A1Plugin(out Assembly m4a1Assembly, out reason))
                return false;

            Type dummyLinkType = m4a1Assembly.GetType(M4A1Compatibility.M4A1DummyLinkHooksTypeName, false);
            if (dummyLinkType == null)
            {
                reason = "exact M4A1 DummyLink type is missing.";
                return false;
            }

            if (!TryResolveSingleDeclaredMethod(dummyLinkType, DummyLinkDeathMethodName, out MethodInfo deathTarget, out reason))
                return false;

            if (!deathTarget.Equals(revivePriorityTarget) || !HasOwnedTranspiler(deathTarget, Plugin.PluginGuid))
            {
                reason = "RevivePriority is not installed on the exact M4A1 CharacterMaster_OnBodyDeath target.";
                return false;
            }

            if (!TryResolveSingleDeclaredMethod(typeof(CharacterMaster), ShrineRespawnMethodName, out MethodInfo shrineTarget, out reason))
                return false;

            MethodInfo postfix = typeof(ShrineAiRecoveryPatch).GetMethod(
                nameof(RespawnExtraLifeShrinePostfix), BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null)
            {
                reason = "ShrineAIRecovery postfix method could not be resolved.";
                return false;
            }

            try
            {
                harmony = new Harmony(HarmonyOwner);
                harmony.Patch(shrineTarget, postfix: new HarmonyMethod(postfix));
                if (!HasOwnedPostfix(shrineTarget, HarmonyOwner))
                {
                    harmony.UnpatchSelf();
                    harmony = null;
                    reason = "Harmony did not report the ShrineAIRecovery postfix after installation.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (harmony != null)
                        harmony.UnpatchSelf();
                }
                catch
                {
                    // Internal Harmony ownership isolates this cleanup from RevivePriority.
                }

                harmony = null;
                reason = "installation exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryValidateExactM4A1Plugin(out Assembly assembly, out string reason)
        {
            assembly = null;
            reason = null;

            if (!Chainloader.PluginInfos.TryGetValue(Plugin.M4A1PluginGuid, out PluginInfo info) || info == null)
            {
                reason = "required M4A1 plugin is not loaded.";
                return false;
            }

            string location;
            try
            {
                location = info.Location;
            }
            catch (Exception ex)
            {
                reason = "M4A1 PluginInfo.Location read failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
            {
                reason = "loaded M4A1 plugin file location is missing or unreadable.";
                return false;
            }

            if (!TrySha256(location, out string actualHash, out reason))
                return false;

            if (!string.Equals(actualHash, M4A1Compatibility.ExpectedM4A1AssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                reason = "loaded M4A1 plugin SHA-256 mismatch.";
                return false;
            }

            if (info.Instance == null)
            {
                reason = "loaded M4A1 PluginInfo has no live plugin instance.";
                return false;
            }

            assembly = info.Instance.GetType().Assembly;
            return true;
        }

        private static bool TrySha256(string filePath, out string hashHex, out string reason)
        {
            hashHex = null;
            reason = null;

            try
            {
                using (FileStream stream = File.OpenRead(filePath))
                using (SHA256 sha = SHA256.Create())
                {
                    hashHex = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "SHA-256 read failed for loaded M4A1 plugin bytes: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryResolveSingleDeclaredMethod(Type type, string methodName, out MethodInfo method, out string reason)
        {
            method = null;
            reason = null;
            int count = 0;

            foreach (MethodInfo candidate in type.GetMethods(DeclaredMethodFlags))
            {
                if (candidate.Name != methodName)
                    continue;

                method = candidate;
                count++;
            }

            if (count == 1)
                return true;

            method = null;
            reason = count == 0
                ? "exact method is missing: " + type.FullName + "." + methodName
                : "exact method resolution is ambiguous: " + type.FullName + "." + methodName + " (count=" + count + ")";
            return false;
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

        private static bool HasOwnedPostfix(MethodBase target, string owner)
        {
            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null || patchInfo.Postfixes == null)
                return false;

            foreach (Patch patch in patchInfo.Postfixes)
            {
                if (patch != null && patch.owner == owner)
                    return true;
            }

            return false;
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
