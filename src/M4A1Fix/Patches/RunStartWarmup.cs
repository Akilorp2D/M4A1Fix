using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using M4A1Fix.Compatibility;
using M4A1Fix.Runtime;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace M4A1Fix.Patches
{
    // Prefetch presentation assets on both host and client; never spawn or grant gameplay state.
    internal sealed class RunStartWarmup : IDisposable
    {
        private readonly Plugin _owner;
        private readonly ManualLogSource _log;
        private readonly ItemDisplayWarmupContract _contract;
        private AssetWarmupJob _job;
        private Coroutine _coroutine;
        private bool _disposed;

        private RunStartWarmup(Plugin owner, ManualLogSource log, ItemDisplayWarmupContract contract)
        {
            _owner = owner;
            _log = log;
            _contract = contract;
            RoR2Application.onLoadFinished += Begin;
        }

        internal static bool TryCreate(Plugin owner, ManualLogSource log, out RunStartWarmup warmup, out string reason)
        {
            warmup = null;
            try
            {
                if (!ItemDisplayWarmupContract.TryResolve(out var contract, out reason)) return false;
                warmup = new RunStartWarmup(owner, log, contract);
                return true;
            }
            catch (Exception ex) { reason = ex.ToString(); return false; }
        }

        private void Begin()
        {
            RoR2Application.onLoadFinished -= Begin;
            if (_disposed) return;
            try { _coroutine = _owner.StartCoroutine(Prepare()); }
            catch (Exception ex) { _log.LogWarning("RunStartWarmup fallback: " + ex); }
        }

        private IEnumerator Prepare()
        {
            yield return null;
            // onLoadFinished means content packs have loaded, not that BodyCatalog.Init
            // has completed. Wait for the catalog's own readiness flag across frames.
            _log.LogInfo("RunStartWarmup: waiting for BodyCatalog.");
            IEnumerator readiness = WaitForCatalog(() => BodyCatalog.availability.available,
                () => !_disposed && Run.instance == null);
            while (readiness.MoveNext()) yield return null;
            if (_disposed || Run.instance != null)
            {
                _log.LogWarning("RunStartWarmup: Skipped while waiting for catalog; original first-run path retained.");
                yield break;
            }
            var timer = Stopwatch.StartNew();
            try
            {
                if (Run.instance != null || _contract.IsInitialized) yield break;
                List<object> keys = CollectKeys();
                _job = new AssetWarmupJob(keys, key => new AddressableOperation(key),
                    () => !_disposed && Run.instance == null, () =>
                    {
                        var finalizeTimer = Stopwatch.StartNew();
                        _contract.InitializeCache();
                        _log.LogInfo("RunStartWarmup: original cache initialized in " + finalizeTimer.ElapsedMilliseconds + " ms.");
                    });
                _log.LogInfo("RunStartWarmup: asynchronously preparing " + _job.Count + " display assets.");
            }
            catch (Exception ex) { _log.LogWarning("RunStartWarmup fallback: " + ex); }

            if (_job == null) yield break;
            while (_job.MoveNext()) yield return null;
            if (_job.Status == AssetWarmupStatus.Completed)
                _log.LogInfo("RunStartWarmup: ready before first run; background elapsed " + timer.ElapsedMilliseconds + " ms.");
            else
                _log.LogWarning("RunStartWarmup: " + _job.Status + "; original first-run path retained. " + _job.Error);
            _job.Dispose();
            _job = null;
            _coroutine = null;
        }

        internal static IEnumerator WaitForCatalog(Func<bool> ready, Func<bool> canContinue)
        {
            while (canContinue() && !ready()) yield return null;
        }

        private static List<object> CollectKeys()
        {
            GameObject body = BodyCatalog.FindBodyPrefab("MercBody");
            ModelLocator locator = body != null ? body.GetComponent<ModelLocator>() : null;
            CharacterModel model = locator != null && locator.modelTransform != null
                ? locator.modelTransform.GetComponent<CharacterModel>() : null;
            if (model == null || model.itemDisplayRuleSet == null)
                throw new InvalidOperationException("MercBody item displays missing after BodyCatalog readiness: body=" +
                    (body != null) + ", locator=" + (locator != null) + ", model=" + (model != null) + ".");
            var keys = new List<object>();
            foreach (var group in model.itemDisplayRuleSet.keyAssetRuleGroups)
            {
                if (group.displayRuleGroup.rules == null) continue;
                foreach (var rule in group.displayRuleGroup.rules)
                {
                    var address = rule.followerPrefabAddress;
                    if (rule.followerPrefab == null && address != null && address.RuntimeKeyIsValid())
                        keys.Add(address.RuntimeKey);
                }
            }
            return keys;
        }

        public void Dispose()
        {
            _disposed = true;
            RoR2Application.onLoadFinished -= Begin;
            try { if (_coroutine != null) _owner.StopCoroutine(_coroutine); }
            finally
            {
                _job?.Dispose();
                if (_job?.Error != null) _log.LogWarning("RunStartWarmup cleanup: " + _job.Error);
                _job = null;
                _coroutine = null;
            }
        }

        private sealed class AddressableOperation : IAssetWarmupOperation
        {
            private AsyncOperationHandle<UnityEngine.Object> _handle;
            internal AddressableOperation(object key) { _handle = Addressables.LoadAssetAsync<UnityEngine.Object>(key); }
            public bool IsDone => _handle.IsDone;
            public bool Succeeded => _handle.Status == AsyncOperationStatus.Succeeded && _handle.Result is GameObject;
            public void Dispose() { if (_handle.IsValid()) Addressables.Release(_handle); }
        }
    }
}
