using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using M4A1Fix.Compatibility.IL;

namespace M4A1Fix.Compatibility
{
    internal sealed class ItemDisplayWarmupContract
    {
        private readonly MethodInfo _lazyInit;
        private readonly FieldInfo[] _cacheFields;
        internal bool IsInitialized => (bool)_cacheFields[0].GetValue(null);

        private ItemDisplayWarmupContract(MethodInfo lazyInit, FieldInfo[] cacheFields)
        {
            _lazyInit = lazyInit;
            _cacheFields = cacheFields;
        }

        internal static bool TryResolve(out ItemDisplayWarmupContract contract, out string reason)
        {
            contract = null;
            if (!M4A1AssemblyContract.TryGetSupportedAssembly(out Assembly assembly, out reason)) return false;
            if (!M4A1AssemblyContract.IsCurrentAssembly(assembly))
            {
                reason = "Item-display warmup requires the inspected M4A1 1.1.5.";
                return false;
            }
            Type type = assembly.GetType("M4A1Mod.Modules.ItemDisplays", false);
            if (type == null) { reason = "Original item-display type is missing."; return false; }
            string[] names = { "LazyInit", "PopulateDisplays", "PopulateFromBody" };
            string[] hashes = {
                "24E9E0D92BED91043AE8B96C4032D02378E33B75ABA5A51482E21EC097C3A948",
                "435C8CD38B347B07F1BA808C0E9198DDDAB4382E1D136E8D98AFC1E21089517B",
                "5FA7B92F2D2AC90B56BEB4D85A623082A74FB6567BBE03CB07EC7B7A23C50276" };
            MethodInfo lazyInit = null;
            for (int i = 0; i < names.Length; i++)
            {
                if (!MethodLookup.TryResolveSingleDeclaredMethod(type, names[i], out MethodInfo method, out reason)) return false;
                if (!method.IsStatic || method.ReturnType != typeof(void) ||
                    method.GetParameters().Length != (i == 2 ? 1 : 0) ||
                    (i == 2 && method.GetParameters()[0].ParameterType != typeof(string)) ||
                    !MethodFingerprint.TryValidateRaw(method, hashes[i], names[i], out reason))
                {
                    reason = reason ?? names[i] + " signature mismatch.";
                    return false;
                }
                if (i == 0) lazyInit = method;
            }
            string[] fields = { "initialized", "itemDisplayPrefabs", "KeyAssetDisplayPrefabs", "KeyAssets" };
            var cacheFields = new FieldInfo[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                cacheFields[i] = type.GetField(fields[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (cacheFields[i] == null) { reason = "Item-display cache field missing: " + fields[i]; return false; }
                if (i > 0 && !typeof(IDictionary).IsAssignableFrom(cacheFields[i].FieldType))
                { reason = "Item-display dictionary field changed: " + fields[i]; return false; }
            }
            if (cacheFields[0].FieldType != typeof(bool)) { reason = "Item-display initialized flag changed."; return false; }
            contract = new ItemDisplayWarmupContract(lazyInit, cacheFields);
            return true;
        }

        internal void InitializeCache()
        {
            if (IsInitialized) return;
            var snapshot = new object[_cacheFields.Length];
            var entries = new List<DictionaryEntry>[snapshot.Length];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = _cacheFields[i].GetValue(null);
                if (snapshot[i] is IDictionary dictionary)
                {
                    entries[i] = new List<DictionaryEntry>();
                    foreach (DictionaryEntry entry in dictionary) entries[i].Add(entry);
                }
            }
            try { _lazyInit.Invoke(null, null); }
            catch
            {
                // LazyInit sets initialized before loading. Restore the untouched original
                // cache on failure so the real first-run path can still try normally.
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i] is IDictionary dictionary)
                    {
                        dictionary.Clear();
                        foreach (DictionaryEntry entry in entries[i]) dictionary.Add(entry.Key, entry.Value);
                    }
                    _cacheFields[i].SetValue(null, snapshot[i]);
                }
                throw;
            }
        }
    }
}
