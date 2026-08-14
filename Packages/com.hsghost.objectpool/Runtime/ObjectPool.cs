using System;
using System.Collections.Generic;

namespace HSGhost.ObjectPool
{
    /// <summary>按需创建、用后归还的泛型对象池，避免高频分配。</summary>
    public sealed class ObjectPool<TItem>
    {
        private readonly Func<TItem> _factory;
        private readonly Stack<TItem> _available;

        /// <summary>以工厂委托与初始容量构造对象池，初始容量个对象在构造时预创建。</summary>
        public ObjectPool(Func<TItem> factory, int initialCapacity)
        {
            _factory = factory;
            _available = new Stack<TItem>(initialCapacity);
            for (var index = 0; index < initialCapacity; index++)
            {
                _available.Push(factory());
            }
        }

        /// <summary>当前可用对象的数量。</summary>
        public int CountAvailable => _available.Count;

        /// <summary>取出一个可用对象，池空时用工厂新建。</summary>
        public TItem Rent() => _available.Count > 0 ? _available.Pop() : _factory();

        /// <summary>归还一个对象，供后续再次取出。</summary>
        public void Return(TItem item) => _available.Push(item);
    }
}
