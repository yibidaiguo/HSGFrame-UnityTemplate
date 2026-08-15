using System;
using System.Collections.Generic;

namespace HSGFrame.Resource
{
    /// <summary>资源作用域：一批资源跟着一个宿主走，作用域结束时把它们一次性释放。</summary>
    public sealed class AssetOwnerScope : IDisposable
    {
        private readonly AssetReferenceLedger _ledger;
        private readonly Dictionary<string, int> _held = new Dictionary<string, int>();
        private bool _disposed;

        /// <summary>用账本与宿主名构造。</summary>
        /// <param name="ledger">记账用的账本。</param>
        /// <param name="ownerName">宿主名，出问题时用来指认是谁没释放。</param>
        public AssetOwnerScope(AssetReferenceLedger ledger, string ownerName)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException(
                    nameof(ledger),
                    "位置：AssetOwnerScope 构造函数；原因：账本是 null；修复：传入非空的 AssetReferenceLedger；参考：参见 AssetOwnerScope 的 ledger 参数说明");
            }

            _ledger = ledger;
            OwnerName = ownerName;
        }

        /// <summary>宿主名。</summary>
        public string OwnerName { get; }

        /// <summary>本作用域当前持有的资源键，按序数序排列。</summary>
        public IReadOnlyList<string> HeldAssetKeys => new List<string>(new SortedSet<string>(_held.Keys, StringComparer.Ordinal));

        /// <summary>在本作用域内取用一个资源。</summary>
        public void Acquire(string assetKey)
        {
            EnsureNotDisposed();

            _ledger.Acquire(assetKey);
            _held.TryGetValue(assetKey, out var count);
            _held[assetKey] = count + 1;
        }

        /// <summary>提前释放本作用域里的某一个资源。</summary>
        public bool Release(string assetKey)
        {
            EnsureNotDisposed();

            if (!_held.TryGetValue(assetKey, out var count))
            {
                return false;
            }

            _ledger.Release(assetKey);
            if (count == 1)
            {
                _held.Remove(assetKey);
            }
            else
            {
                _held[assetKey] = count - 1;
            }

            return true;
        }

        /// <summary>结束作用域，把还持有的资源全部释放。重复 Dispose 是安全的。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var keys = new List<string>(_held.Keys);
            foreach (var key in keys)
            {
                var count = _held[key];
                for (var i = 0; i < count; i++)
                {
                    _ledger.Release(key);
                }
            }

            _held.Clear();
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(AssetOwnerScope),
                    "位置：AssetOwnerScope；原因：作用域已结束；修复：结束之后不要再取用或释放资源；参考：宿主 " + (OwnerName ?? "（未命名）"));
            }
        }
    }
}
