using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using M4A1Fix.Compatibility.IL;
using RoR2;
using Path = System.IO.Path;

namespace M4A1Fix.Compatibility
{
    // M4A1 1.1.5 uses the inspected post-update game contract. Unknown builds stay disabled.
    internal static class CurrentRoR2ReviveContract
    {
        internal const string AssemblySha256 = "A5F7D83C7E6872FDD6099FDC299ADB7D4FA03CBF465E526D6F5EFCC35C2C281D";

        internal static bool TryValidate(out Func<CharacterBody, BuffDef, bool> hasShrineBuff, out string reason)
        {
            hasShrineBuff = null;
            if (!TryValidateGameFile(typeof(CharacterMaster).Assembly.Location, Paths.ManagedPath, out reason))
                return false;

            if (!Validate("OnBodyDeath", typeof(void), new[] { typeof(CharacterBody) }, 624,
                    "1050368A38FE8F544DDC254F0F4BC2B02F4FA42C2689E2DF06A5032D25C6D169", out var death, out reason) ||
                !Validate("TryReviveOnBodyDeath", typeof(bool), new[] { typeof(CharacterBody) }, 763,
                    "2F1B2850A74F9FFD70F21A5487E65FFB05872A4616BD6530940C3B6090BDCEAA", out var revive, out reason) ||
                !Validate("RespawnExtraLifeShrine", typeof(void), Type.EmptyTypes, 256,
                    "B3D08729347A0036302C1F6D2BD05EC0038CB7210F527E4990B1905AA7BDB8AA", out var respawn, out reason) ||
                !Validate("IsExtraLifePendingServer", typeof(bool), Type.EmptyTypes, 75,
                    "7DDA7E015613A20CBD68394435B95ECE885557F7875832D063F853B764157F74", out var pending, out reason))
                return false;

            // Vanilla remains responsible for all prohibitions, buff removal and scheduling.
            // In this build Shrine reads the server's unmanaged buff storage, not HasBuff.
            int buff = IlMethodReader.FindMember(revive, "DLC2Content+Buffs::ExtraLifeBuff", 0);
            int has = IlMethodReader.FindMember(revive, "CharacterBody::HasUnmanagedBuff", buff + 1);
            int remove = IlMethodReader.FindMember(revive, "CharacterBody::RemoveUnmanagedBuff", has + 1);
            int shrine = IlMethodReader.FindString(revive, "RespawnExtraLifeShrine", remove + 1);
            int dio = IlMethodReader.FindMember(revive, "ItemTransformation::TryTake", shrine + 1);
            if (buff < 0 || has <= buff || remove <= has || shrine <= remove || dio <= shrine ||
                IlMethodReader.FindMember(death, "CharacterMaster::TryReviveOnBodyDeath", 0) < 0 ||
                IlMethodReader.FindMember(respawn, "CharacterMaster::Respawn", 0) < 0 ||
                IlMethodReader.FindString(pending, "RespawnExtraLifeShrine", 0) < 0)
            {
                reason = "Current RoR2 Shrine-before-Dio/respawn/pending landmarks mismatch.";
                return false;
            }

            MethodInfo reader = typeof(CharacterBody).GetMethod("HasUnmanagedBuff",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(BuffDef) }, null);
            if (reader == null || reader.ReturnType != typeof(bool))
            {
                reason = "RoR2 HasUnmanagedBuff(BuffDef) signature mismatch.";
                return false;
            }

            // A typed open-instance delegate avoids reflection on every death and avoids a
            // direct new-API reference in the legacy path, which older Mono builds must load.
            hasShrineBuff = (Func<CharacterBody, BuffDef, bool>)Delegate.CreateDelegate(
                typeof(Func<CharacterBody, BuffDef, bool>), reader);
            reason = null;
            return true;
        }

        internal static bool IsSupportedHash(string hash)
        {
            return string.Equals(hash, AssemblySha256, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryValidateGameFile(string loadedLocation, string managedDirectory, out string reason)
        {
            reason = null;
            // BepInEx preloads rewritten game assemblies from bytes, so Location may be empty.
            // Hash the selected game's original file, then independently validate loaded IL above.
            string path = loadedLocation;
            if (string.IsNullOrEmpty(path))
            {
                if (string.IsNullOrWhiteSpace(managedDirectory))
                {
                    reason = "RoR2 assembly is memory-loaded and BepInEx ManagedPath is unavailable.";
                    return false;
                }
                path = Path.Combine(managedDirectory, "RoR2.dll");
            }
            if (!File.Exists(path))
            {
                reason = "RoR2.dll for compatibility hashing was not found: " + path;
                return false;
            }
            try
            {
                string actual = Sha256Hash.ComputeFile(path);
                if (IsSupportedHash(actual))
                    return true;
                reason = "M4A1 1.1.5 requires the inspected RoR2.dll SHA-256 " + AssemblySha256 + ", got " + actual + ".";
            }
            catch (IOException ex)
            {
                reason = "RoR2.dll compatibility hash read failed: " + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                reason = "RoR2.dll compatibility hash read denied: " + ex.Message;
            }
            return false;
        }

        private static bool Validate(string name, Type result, Type[] parameters, int size,
            string canonicalHash, out List<DecodedInstruction> instructions, out string reason)
        {
            instructions = null;
            MethodInfo method = typeof(CharacterMaster).GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
            if (method == null || method.ReturnType != result)
            {
                reason = "Current RoR2 " + name + " signature mismatch.";
                return false;
            }
            return MethodFingerprint.TryValidate(method, size, canonicalHash, "Current RoR2 " + name,
                out instructions, out reason);
        }
    }
}
