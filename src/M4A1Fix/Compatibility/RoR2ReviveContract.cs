using System;
using System.Reflection;
using M4A1Fix.Compatibility.IL;
using RoR2;

namespace M4A1Fix.Compatibility
{
    // Documents and validates the vanilla Shrine/Dio decision, respawn and pending-state contracts.
    internal static class RoR2ReviveContract
    {
        private const int ExpectedTryReviveCodeSize = 632;
        private const int ExpectedRespawnExtraLifeShrineCodeSize = 264;
        private const int ExpectedPendingStateCodeSize = 75;
        private const string ExpectedTryReviveCanonicalSha256 = "43D299DDC88201E148A1D914092175CB58285EDB1683822D825FDCC8E4163395";
        private const string ExpectedRespawnExtraLifeShrineCanonicalSha256 = "28D947645DE9C1E53D7C25591434D8B75ECB2E2E42492DF482D9A57296428E1F";
        private const string ExpectedPendingStateCanonicalSha256 = "7DDA7E015613A20CBD68394435B95ECE885557F7875832D063F853B764157F74";

        private delegate bool MethodValidator(MethodInfo method, out string reason);
        private static Func<CharacterBody, BuffDef, bool> _hasShrineBuff;

        internal static bool HasShrineBuff(CharacterBody body)
        {
            return _hasShrineBuff != null && _hasShrineBuff(body, DLC2Content.Buffs.ExtraLifeBuff);
        }

        internal static bool TryValidateTypeIdentity(out string reason)
        {
            reason = null;
            if (typeof(CharacterMaster).Assembly == typeof(CharacterBody).Assembly)
                return true;

            reason = "RoR2 CharacterMaster and CharacterBody are not from the same assembly.";
            return false;
        }

        internal static bool TryValidate(bool currentUpstream, out string reason)
        {
            _hasShrineBuff = null;
            if (currentUpstream)
                return CurrentRoR2ReviveContract.TryValidate(out _hasShrineBuff, out reason);

            bool valid = TryValidateMethod("TryReviveOnBodyDeath", typeof(bool), new[] { typeof(CharacterBody) },
                       ValidateTryReviveStructure, out reason) &&
                   TryValidateMethod("RespawnExtraLifeShrine", typeof(void), Type.EmptyTypes,
                       ValidateShrineRespawnStructure, out reason) &&
                   TryValidateMethod("IsExtraLifePendingServer", typeof(bool), Type.EmptyTypes,
                       ValidatePendingStateStructure, out reason);
            if (valid)
                _hasShrineBuff = (body, buff) => body.HasBuff(buff);
            return valid;
        }

        private static bool TryValidateMethod(string name, Type returnType, Type[] arguments,
            MethodValidator validateBody, out string reason)
        {
            MethodInfo method = typeof(CharacterMaster).GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, arguments, null);
            reason = null;
            if (method == null || method.ReturnType != returnType)
            {
                reason = "RoR2 " + name + " signature mismatch.";
                return false;
            }

            return validateBody(method, out reason);
        }

        private static bool ValidateTryReviveStructure(MethodInfo method, out string reason)
        {
            if (!MethodFingerprint.TryValidate(
                    method, ExpectedTryReviveCodeSize, ExpectedTryReviveCanonicalSha256,
                    "RoR2 TryReviveOnBodyDeath", out var instructions, out reason))
                return false;

            int shrineBuff = IlMethodReader.FindMember(instructions, "DLC2Content+Buffs::ExtraLifeBuff", 0);
            int hasBuff = IlMethodReader.FindMember(instructions, "CharacterBody::HasBuff", shrineBuff + 1);
            int removeBuff = IlMethodReader.FindMember(instructions, "CharacterBody::RemoveBuff", hasBuff + 1);
            int shrineString = IlMethodReader.FindString(instructions, "RespawnExtraLifeShrine", removeBuff + 1);
            int shrineInvoke = IlMethodReader.FindMember(instructions, "UnityEngine.MonoBehaviour::Invoke(System.String,System.Single)", shrineString + 1);
            int sfxString = IlMethodReader.FindString(instructions, "PlayExtraLifeSFX", shrineInvoke + 1);
            int sfxInvoke = IlMethodReader.FindMember(instructions, "UnityEngine.MonoBehaviour::Invoke(System.String,System.Single)", sfxString + 1);
            int dioTake = IlMethodReader.FindMember(instructions, "ItemTransformation::TryTake", 0);

            if (shrineBuff < 0 ||
                hasBuff <= shrineBuff ||
                removeBuff <= hasBuff ||
                shrineString <= removeBuff ||
                shrineInvoke <= shrineString ||
                sfxString <= shrineInvoke ||
                sfxInvoke <= sfxString ||
                (dioTake >= 0 && dioTake <= sfxInvoke))
            {
                reason = "RoR2 TryReviveOnBodyDeath normalized structural fingerprint mismatch in Shrine-before-Dio decision landmarks.";
                return false;
            }

            return true;
        }

        private static bool ValidateShrineRespawnStructure(MethodInfo method, out string reason)
        {
            if (!MethodFingerprint.TryValidate(
                    method, ExpectedRespawnExtraLifeShrineCodeSize, ExpectedRespawnExtraLifeShrineCanonicalSha256,
                    "RoR2 RespawnExtraLifeShrine", out var instructions, out reason))
                return false;

            int respawn = IlMethodReader.FindMember(instructions, "CharacterMaster::Respawn", 0);
            int immune = IlMethodReader.FindMember(instructions, "RoR2Content+Buffs::Immune", 0);
            int addTimedBuff = IlMethodReader.FindMember(instructions, "CharacterBody::AddTimedBuff", immune < 0 ? 0 : immune);

            if (respawn < 0 || immune < 0 || addTimedBuff < immune)
            {
                reason = "RoR2 RespawnExtraLifeShrine normalized structural fingerprint mismatch in respawn/immunity landmarks.";
                return false;
            }

            return true;
        }

        private static bool ValidatePendingStateStructure(MethodInfo method, out string reason)
        {
            if (!MethodFingerprint.TryValidate(
                    method, ExpectedPendingStateCodeSize, ExpectedPendingStateCanonicalSha256,
                    "RoR2 IsExtraLifePendingServer", out var instructions, out reason))
                return false;

            int behavior = IlMethodReader.FindMember(instructions, "ExtraLifeServerBehavior", 0);
            int seeker = IlMethodReader.FindString(instructions, "RespawnSeekerRevive", 0);
            int shrine = IlMethodReader.FindString(instructions, "RespawnExtraLifeShrine", seeker + 1);
            int heal = IlMethodReader.FindString(instructions, "RespawnExtraLifeHealAndRevive", shrine + 1);

            if (behavior < 0 || seeker < 0 || shrine <= seeker || heal <= shrine)
            {
                reason = "RoR2 IsExtraLifePendingServer normalized structural fingerprint mismatch in pending-revive landmarks.";
                return false;
            }

            return true;
        }
    }
}
