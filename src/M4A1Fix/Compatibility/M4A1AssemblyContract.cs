using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using M4A1Fix.Compatibility.IL;
using RoR2;

namespace M4A1Fix.Compatibility
{
    // Binds the fix to the supported original binary and the active BepInEx plugin instance.
    internal static class M4A1AssemblyContract
    {
        internal const string ExpectedAssemblySha256 = "600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154";
        internal const string CurrentAssemblySha256 = "023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F0";
        private const string ExpectedDeathHookIlSha256 = "79150F880D5A6361BEF0E94A78AABB57B7A56881565C843E8A3D2614D65FE5E4";
        private const string CurrentDeathHookIlSha256 = "702CCE7C58854F4AB9123B01E59E87B363536F46CCB2EE958C78AB5261ECB0C9";
        internal const string DummyLinkHooksTypeName = "M4A1Mod.Survivors.M4A1.Hooks.DummyLink";
        internal const string UtilsTypeName = "M4A1Mod.Survivors.M4A1.Utils.M4A1";

        internal static bool TryGetSupportedAssembly(out Assembly assembly, out string reason)
        {
            assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "M4A1Mod", StringComparison.Ordinal));
            reason = null;
            if (assembly == null)
            {
                reason = "M4A1Mod assembly is not loaded.";
                return false;
            }

            return ValidateAssemblyHash(assembly, out reason);
        }

        internal static bool IsCurrentAssembly(Assembly assembly)
        {
            return string.Equals(Sha256Hash.ComputeFile(assembly.Location), CurrentAssemblySha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetDeathHook(Assembly assembly, out MethodInfo target, out string reason)
        {
            target = null;
            reason = null;
            if (!ValidateAssemblyHash(assembly, out reason))
                return false;
            Type type = assembly.GetType(DummyLinkHooksTypeName, false);
            if (type == null)
            {
                reason = "M4A1 DummyLink hook type is missing.";
                return false;
            }

            MethodInfo candidate = type.GetMethod("CharacterMaster_OnBodyDeath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (!ValidateM4A1TargetShape(candidate, out reason) ||
                !MethodFingerprint.TryValidateRaw(candidate, IsCurrentAssembly(assembly) ? CurrentDeathHookIlSha256 : ExpectedDeathHookIlSha256,
                    "M4A1 CharacterMaster_OnBodyDeath", out reason))
                return false;

            target = candidate;
            return true;
        }

        private static bool ValidateM4A1TargetShape(MethodInfo method, out string reason)
        {
            reason = null;
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void))
            {
                reason = "M4A1 CharacterMaster_OnBodyDeath method shape is missing or incompatible.";
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3 ||
                parameters[0].ParameterType.Name != "orig_OnBodyDeath" ||
                parameters[1].ParameterType != typeof(CharacterMaster) ||
                parameters[2].ParameterType != typeof(CharacterBody))
            {
                reason = "M4A1 CharacterMaster_OnBodyDeath parameter shape is incompatible.";
                return false;
            }

            return true;
        }

        private static bool ValidateAssemblyHash(Assembly assembly, out string reason)
        {
            const string displayName = "M4A1Mod.dll";
            reason = null;
            string location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                reason = displayName + " location is unavailable for compatibility hashing.";
                return false;
            }

            string actual;
            try
            {
                actual = Sha256Hash.ComputeFile(location);
            }
            catch (IOException ex)
            {
                reason = displayName + " hash read failed: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                reason = displayName + " hash read denied: " + ex.Message;
                return false;
            }

            if (!IsSupportedHash(actual))
            {
                reason = displayName + " SHA-256 mismatch. Expected verified M4A1 1.1.4 or 1.1.5, got " + actual + ".";
                return false;
            }

            return true;
        }

        internal static bool IsSupportedHash(string hash)
        {
            return string.Equals(hash, ExpectedAssemblySha256, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(hash, CurrentAssemblySha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryValidateLoadedPlugin(out Assembly assembly, out string reason)
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

            if (!IsSupportedHash(actualHash))
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
            // The PluginInfo path and live instance must identify the same supported bytes.
            if (!ValidateAssemblyHash(assembly, out reason))
                return false;
            if (!string.Equals(actualHash, Sha256Hash.ComputeFile(assembly.Location), StringComparison.OrdinalIgnoreCase))
            {
                reason = "M4A1 PluginInfo file and live instance assembly have different SHA-256 identities.";
                return false;
            }
            return true;
        }

        private static bool TrySha256(string filePath, out string hashHex, out string reason)
        {
            hashHex = null;
            reason = null;

            try
            {
                hashHex = Sha256Hash.ComputeFile(filePath);

                return true;
            }
            catch (Exception ex)
            {
                reason = "SHA-256 read failed for loaded M4A1 plugin bytes: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
