using System;

namespace HSGFrame.ObjectPool
{
    /// <summary>按需创建、用后归还的泛型对象池，内部委托给 ObjectPoolSystem，避免高频分配。</summary>
    public sealed class ObjectPool<TItem> where TItem : class
    {
        private readonly ObjectPoolSystem _system = new ObjectPoolSystem();
        private readonly string _key = typeof(TItem).FullName;

        /// <summary>以工厂委托与初始容量构造对象池，初始容量个对象在构造时预创建。</summary>
        public ObjectPool(Func<TItem> factory, int initialCapacity)
        {
            _system.Register(_key, () => factory(), maximumCapacity: -1, warmUpCount: initialCapacity);
        }

        /// <summary>当前可用对象的数量。</summary>
        public int CountAvailable => _system.FindBucket(_key)?.IdleCount ?? 0;

        /// <summary>取出一个可用对象，池空时用工厂新建。</summary>
        public TItem Rent() => _system.Take<TItem>();

        /// <summary>归还一个对象，供后续再次取出。</summary>
        public void Return(TItem item) => _system.Return(_key, item);
    }
}
