using System;
using System.Collections.Generic;

namespace HSGFrame.Resource
{
    /// <summary>一个资源当前的引用情况。</summary>
    public sealed class AssetReferenceRecord
    {
        internal AssetReferenceRecord(string assetKey, int referenceCount, int acquiredCount, int releasedCount)
        {
            AssetKey = assetKey;
            ReferenceCount = referenceCount;
            AcquiredCount = acquiredCount;
            ReleasedCount = releasedCount;
        }

        /// <summary>资源键，通常是资源路径。</summary>
        public string AssetKey { get; }

        /// <summary>当前引用计数。</summary>
        public int ReferenceCount { get; }

        /// <summary>累计取用次数，用来排查泄漏。</summary>
        public int AcquiredCount { get; }

        /// <summary>累计释放次数。</summary>
        public int ReleasedCount { get; }
    }

    /// <summary>资源引用账本：谁取了、取了几次、什么时候才真的能卸载。加载本身不在这里，这里只管账。</summary>
    public sealed class AssetReferenceLedger
    {
        private sealed class Entry
        {
            public int ReferenceCount;
            public int AcquiredCount;
            public int ReleasedCount;
        }

        private readonly Dictionary<string, Entry> _records = new Dictionary<string, Entry>();
        private readonly SortedSet<string> _readyToUnload = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>当前有引用的资源数量。</summary>
        public int TrackedAssetCount => _records.Count;

        /// <summary>取用一个资源，引用计数加一，返回加完之后的计数。</summary>
        public int Acquire(string assetKey)
        {
            EnsureValidKey(assetKey, nameof(Acquire));

            if (!_records.TryGetValue(assetKey, out var entry))
            {
                entry = new Entry();
                _records[assetKey] = entry;
            }

            // 归零后又被重新取用：卸载还没执行，新的使用者已经来了，这一次就不该卸载了。
            if (entry.ReferenceCount == 0)
            {
                _readyToUnload.Remove(assetKey);
            }

            entry.ReferenceCount++;
            entry.AcquiredCount++;
            return entry.ReferenceCount;
        }

        /// <summary>释放一个资源，引用计数减一，返回减完之后的计数。计数归零时触发 ReadyToUnload。</summary>
        public int Release(string assetKey)
        {
            EnsureValidKey(assetKey, nameof(Release));

            if (!_records.TryGetValue(assetKey, out var entry) || entry.ReferenceCount == 0)
            {
                throw new InvalidOperationException(
                    "位置：AssetReferenceLedger.Release；原因：释放次数超过取用次数；修复：确认每次 Release 都对应一次 Acquire；参考：资源键 " + assetKey + "，当前引用计数 " + (entry == null ? 0 : entry.ReferenceCount));
            }

            entry.ReferenceCount--;
            entry.ReleasedCount++;

            if (entry.ReferenceCount == 0)
            {
                // 真正的卸载是引擎那侧的异步动作：账本先标记、由壳确认之后再摘；
                // 中间这段时间里如果又有人 Acquire，计数从 0 涨回 1，这一次就不该卸载了。
                _readyToUnload.Add(assetKey);
                ReadyToUnload?.Invoke(assetKey);
            }

            return entry.ReferenceCount;
        }

        /// <summary>取某个资源的引用记录，没被取用过时返回 null。</summary>
        public AssetReferenceRecord Find(string assetKey)
        {
            if (!_records.TryGetValue(assetKey, out var entry))
            {
                return null;
            }

            return new AssetReferenceRecord(assetKey, entry.ReferenceCount, entry.AcquiredCount, entry.ReleasedCount);
        }

        /// <summary>某个资源当前的引用计数，没被取用过时返回 0。</summary>
        public int ReferenceCountOf(string assetKey)
        {
            return _records.TryGetValue(assetKey, out var entry) ? entry.ReferenceCount : 0;
        }

        /// <summary>当前引用计数归零、可以卸载的资源键，按序数序排列。</summary>
        public IReadOnlyList<string> ReadyToUnloadKeys => new List<string>(_readyToUnload);

        /// <summary>把一个已归零的资源从账本里摘掉，表示它真的被卸载了。</summary>
        public bool ConfirmUnloaded(string assetKey)
        {
            if (!_records.TryGetValue(assetKey, out var entry))
            {
                return false;
            }

            if (entry.ReferenceCount > 0)
            {
                return false;
            }

            _records.Remove(assetKey);
            _readyToUnload.Remove(assetKey);
            return true;
        }

        /// <summary>引用计数归零时触发，参数是资源键。</summary>
        public event Action<string> ReadyToUnload;

        /// <summary>清空账本。</summary>
        public void Clear()
        {
            _records.Clear();
            _readyToUnload.Clear();
        }

        private static void EnsureValidKey(string assetKey, string methodName)
        {
            if (string.IsNullOrEmpty(assetKey))
            {
                throw new ArgumentException(
                    "位置：AssetReferenceLedger." + methodName + "；原因：资源键是空串或 null；修复：传入非空的资源键；参考：参见 AssetReferenceLedger." + methodName + " 的 assetKey 参数说明");
            }
        }
    }
}
