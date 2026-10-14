using System.Reflection;

namespace M4A1Fix.Compatibility
{
    // Compatibility is checked before patch installation; each contract has a separate owner.
    internal static class M4A1Compatibility
    {
        internal static bool TryValidateRevivePriorityTarget(out MethodInfo target, out string reason)
        {
            target = null;
            if (!M4A1AssemblyContract.TryGetSupportedAssembly(out Assembly assembly, out reason) ||
                !RoR2ReviveContract.TryValidateTypeIdentity(out reason) ||
                !M4A1AssemblyContract.TryGetDeathHook(assembly, out MethodInfo deathHook, out reason) ||
                !RoR2ReviveContract.TryValidate(M4A1AssemblyContract.IsCurrentAssembly(assembly), out reason))
                return false;

            target = deathHook;
            return true;
        }
    }
}
