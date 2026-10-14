using BepInEx;
using HarmonyLib;
using M4A1Fix.Patches;
using M4A1Fix.Runtime;

namespace M4A1Fix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(M4A1PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "com.m4a1fix.revivepriority";
        internal const string PluginName = "M4A1Fix";
        internal const string PluginVersion = "1.1.0";
        internal const string M4A1PluginGuid = "com.Snoresville.M4A1";

        private Harmony _revivePriorityHarmony;
        private Harmony _shrineAiRecoveryHarmony;
        private RunStartWarmup _runStartWarmup;

        private void Awake()
        {
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");

            _revivePriorityHarmony = new Harmony(PluginGuid);
            if (!RevivePriorityPatch.TryInstall(_revivePriorityHarmony, out var reviveTarget, out var reason))
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

            if (RunStartWarmup.TryCreate(this, Logger, out _runStartWarmup, out reason))
                Logger.LogInfo("RunStartWarmup enabled; waiting for game content.");
            else
                Logger.LogWarning("RunStartWarmup disabled: " + reason);
        }

        private void OnDestroy()
        {
            try { _runStartWarmup?.Dispose(); }
            catch (System.Exception ex) { Logger.LogError("RunStartWarmup cleanup failed: " + ex); }
            if (_shrineAiRecoveryHarmony != null)
            {
                if (!HarmonyPatchLifecycle.TryUnpatch(_shrineAiRecoveryHarmony, out string reason))
                    Logger.LogError("ShrineAIRecovery cleanup failed: " + reason);
            }

            if (_revivePriorityHarmony != null)
            {
                if (!HarmonyPatchLifecycle.TryUnpatch(_revivePriorityHarmony, out string reason))
                    Logger.LogError("RevivePriority cleanup failed: " + reason);
            }
        }
    }
}
