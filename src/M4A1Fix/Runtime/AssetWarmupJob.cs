using System;
using System.Collections;
using System.Collections.Generic;

namespace M4A1Fix.Runtime
{
    internal interface IAssetWarmupOperation : IDisposable
    {
        bool IsDone { get; }
        bool Succeeded { get; }
    }

    internal enum AssetWarmupStatus { Loading, Completed, Skipped, Failed }

    // One MoveNext is one frame. Never wait synchronously for a pending load.
    internal sealed class AssetWarmupJob : IEnumerator, IDisposable
    {
        private readonly List<object> _keys = new List<object>();
        private readonly List<IAssetWarmupOperation> _operations = new List<IAssetWarmupOperation>();
        private readonly Func<object, IAssetWarmupOperation> _load;
        private readonly Func<bool> _canContinue;
        private readonly Action _finalize;
        private readonly int _batchSize;
        private int _next;
        private bool _stopped;

        internal AssetWarmupStatus Status { get; private set; } = AssetWarmupStatus.Loading;
        internal string Error { get; private set; }
        internal int Count => _keys.Count;
        public object Current => null;

        internal AssetWarmupJob(IEnumerable<object> keys, Func<object, IAssetWarmupOperation> load,
            Func<bool> canContinue, Action finalize, int batchSize = 8)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            _finalize = finalize ?? throw new ArgumentNullException(nameof(finalize));
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
            _batchSize = batchSize;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object key in keys)
            {
                if (key == null || string.IsNullOrEmpty(key.ToString()))
                    throw new ArgumentException("Asset keys must be nonempty.", nameof(keys));
                if (seen.Add(key.ToString())) _keys.Add(key);
            }
        }

        public bool MoveNext()
        {
            if (_stopped) return false;
            try
            {
                if (!_canContinue()) return Stop(AssetWarmupStatus.Skipped);
                if (_next < _keys.Count)
                {
                    int end = Math.Min(_next + _batchSize, _keys.Count);
                    while (_next < end)
                    {
                        IAssetWarmupOperation operation = _load(_keys[_next++]);
                        if (operation == null) throw new InvalidOperationException("Asset loader returned no operation.");
                        _operations.Add(operation);
                    }
                    return true;
                }
                foreach (IAssetWarmupOperation operation in _operations)
                {
                    if (!operation.IsDone) return true;
                    if (!operation.Succeeded) throw new InvalidOperationException("An item-display asset failed to load.");
                }
                if (!_canContinue()) return Stop(AssetWarmupStatus.Skipped);
                _finalize();
                return Stop(AssetWarmupStatus.Completed);
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ": " + ex.Message;
                return Stop(AssetWarmupStatus.Failed);
            }
        }

        private bool Stop(AssetWarmupStatus status)
        {
            Status = status;
            Dispose();
            return false;
        }

        public void Dispose()
        {
            if (_stopped) return;
            _stopped = true;
            if (Status == AssetWarmupStatus.Loading) Status = AssetWarmupStatus.Skipped;
            foreach (IAssetWarmupOperation operation in _operations)
            {
                try { operation.Dispose(); }
                catch (Exception ex)
                {
                    Status = AssetWarmupStatus.Failed;
                    Error = (Error == null ? "" : Error + " ") + "Asset release failed: " + ex.Message;
                }
            }
            _operations.Clear();
        }

        public void Reset() => throw new NotSupportedException();
    }
}
