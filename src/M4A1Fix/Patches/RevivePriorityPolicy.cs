using RoR2;
using M4A1Fix.Compatibility;
using UnityEngine.Networking;

namespace M4A1Fix.Patches
{
    internal static class RevivePriorityPolicy
    {
        // Vanilla owns Shrine consumption and scheduling; keep all resources untouched here.
        internal static bool ShouldDelegateShrineToVanilla(CharacterBody body)
        {
            return NetworkServer.active && body != null && RoR2ReviveContract.HasShrineBuff(body);
        }
    }
}
