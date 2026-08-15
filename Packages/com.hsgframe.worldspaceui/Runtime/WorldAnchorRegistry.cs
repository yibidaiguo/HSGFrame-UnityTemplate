using System;
using System.Collections.Generic;

namespace HSGFrame.WorldSpaceUI
{
    /// <summary>世界空间锚点登记表：登记一批锚点，每帧统一算出各自的呈现结论。</summary>
    public sealed class WorldAnchorRegistry
    {
        private readonly WorldAnchorPolicy _policy;
        private readonly Dictionary<string, WorldPoint> _positions = new Dictionary<string, WorldPoint>();
        private readonly Dictionary<string, Handle> _handles = new Dictionary<string, Handle>();

        /// <summary>用一份策略构造。</summary>
        public WorldAnchorRegistry(WorldAnchorPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>当前登记的锚点数量。</summary>
        public int AnchorCount => _positions.Count;

        /// <summary>登记一个锚点，返回句柄，Dispose 即注销。</summary>
        public IDisposable Register(string anchorId, WorldPoint position)
        {
            ValidateAnchorId(anchorId);

            var handle = new Handle(this, anchorId);
            _positions[anchorId] = position;
            _handles[anchorId] = handle;
            return handle;
        }

        /// <summary>更新一个已登记锚点的位置；标识不存在时返回 false。</summary>
        public bool UpdatePosition(string anchorId, WorldPoint position)
        {
            if (!_positions.ContainsKey(anchorId))
            {
                return false;
            }

            _positions[anchorId] = position;
            return true;
        }

        /// <summary>按相机位姿把全部锚点算一遍，结果按锚点标识的序数序排列。</summary>
        public IReadOnlyList<KeyValuePair<string, WorldAnchorPresentation>> Resolve(
            WorldPoint cameraPosition, WorldPoint cameraForward)
        {
            var ids = SortedIds();
            var result = new List<KeyValuePair<string, WorldAnchorPresentation>>(ids.Count);
            foreach (var id in ids)
            {
                result.Add(new KeyValuePair<string, WorldAnchorPresentation>(
                    id, _policy.Resolve(_positions[id], cameraPosition, cameraForward)));
            }

            return result;
        }

        /// <summary>当前可见的锚点标识，按序数序排列。</summary>
        public IReadOnlyList<string> ResolveVisibleIds(WorldPoint cameraPosition, WorldPoint cameraForward)
        {
            var ids = SortedIds();
            var result = new List<string>();
            foreach (var id in ids)
            {
                if (_policy.Resolve(_positions[id], cameraPosition, cameraForward).IsVisible)
                {
                    result.Add(id);
                }
            }

            return result;
        }

        /// <summary>注销全部锚点。</summary>
        public void ClearAll()
        {
            _positions.Clear();
            _handles.Clear();
        }

        private List<string> SortedIds()
        {
            var ids = new List<string>(_positions.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private void Unregister(string anchorId, Handle handle)
        {
            // 仅当该标识当前登记的有效句柄正是这个句柄时才移除，
            // 避免重复登记覆盖后，旧句柄 Dispose 误删新锚点。
            if (_handles.TryGetValue(anchorId, out var current) && ReferenceEquals(current, handle))
            {
                _positions.Remove(anchorId);
                _handles.Remove(anchorId);
            }
        }

        private static void ValidateAnchorId(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                throw new ArgumentException(
                    "位置：WorldAnchorRegistry.Register；原因：anchorId 是空串或 null；修复：传入非空的锚点标识；参考：参见 Register 的 anchorId 参数说明");
            }
        }

        /// <summary>一个已登记锚点的注销句柄，Dispose 即注销。</summary>
        private sealed class Handle : IDisposable
        {
            private readonly WorldAnchorRegistry _registry;
            private readonly string _anchorId;
            private bool _disposed;

            public Handle(WorldAnchorRegistry registry, string anchorId)
            {
                _registry = registry;
                _anchorId = anchorId;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _registry.Unregister(_anchorId, this);
            }
        }
    }
}
