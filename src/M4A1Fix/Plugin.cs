using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using M4A1Fix.Patches;

namespace M4A1Fix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(M4A1PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "com.m4a1fix.revivepriority";
        internal const string PluginName = "M4A1Fix";
        internal const string PluginVersion = "1.0.0";
        internal const string M4A1PluginGuid = "com.Snoresville.M4A1";

        private Harmony _revivePriorityHarmony;
        private Harmony _shrineAiRecoveryHarmony;

        private void Awake()
        {
            Logger.LogInfo("M4A1Fix 1.0.0 loaded.");

            _revivePriorityHarmony = new Harmony(PluginGuid);
            if (!RevivePriorityPatch.TryInstall(_revivePriorityHarmony, Logger, out var reviveTarget, out var reason))
            {
                Logger.LogError("RevivePriority disabled: " + reason);
                return;
            }

            Logger.LogInfo("RevivePriority enabled.");

            if (!ShrineAiRecoveryPatch.TryInstall(Logger, reviveTarget, out _shrineAiRecoveryHarmony, out reason))
            {
                Logger.LogError("ShrineAIRecovery disabled: " + reason);
                return;
            }

            Logger.LogInfo("ShrineAIRecovery enabled.");
        }

        private void OnDestroy()
        {
            if (_shrineAiRecoveryHarmony != null)
                _shrineAiRecoveryHarmony.UnpatchSelf();

            if (_revivePriorityHarmony != null)
                _revivePriorityHarmony.UnpatchSelf();
        }
    }
}
