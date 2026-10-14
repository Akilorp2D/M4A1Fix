using System;
using System.Reflection;
using HarmonyLib;

namespace M4A1Fix.Runtime
{
    internal static class HarmonyPatchLifecycle
    {
        internal static bool TryUnpatch(Harmony harmony, out string reason)
        {
            reason = null;
            try
            {
                harmony.UnpatchSelf();
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.ToString();
                return false;
            }
        }

        internal static bool HasTranspiler(MethodBase target, string owner, MethodInfo method)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info == null || info.Transpilers == null)
                return false;

            foreach (Patch patch in info.Transpilers)
                if (patch != null && patch.owner == owner && patch.PatchMethod == method)
                    return true;
            return false;
        }

        internal static bool HasPostfix(MethodBase target, string owner)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            if (info == null || info.Postfixes == null)
                return false;

            foreach (Patch patch in info.Postfixes)
                if (patch != null && patch.owner == owner)
                    return true;
            return false;
        }
    }
}
