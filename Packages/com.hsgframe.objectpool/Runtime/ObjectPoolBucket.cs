using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HSGFrame.ObjectPool
{
    /// <summary>一个类型的对象桶：按容量上限收纳闲置对象，取还都走这里。</summary>
    public sealed class ObjectPoolBucket
    {
        private readonly Stack<object> _idle = new Stack<object>();
        // 自带一个按引用比较的比较器，而不是用 BCL 的 ReferenceEqualityComparer：
        // 后者在 .NET 5+ 才公开，Unity 的 .NET Standard 2.1 档里取不到，
        // 纯 .NET 侧编得过、Unity 侧一编就报 CS0122。
        private readonly HashSet<object> _idleSet = new HashSet<object>(InstanceReferenceComparer.Shared);

        /// <summary>用容量上限构造，上限传 -1 表示不限。</summary>
        public ObjectPoolBucket(int maximumCapacity = -1)
        {
            MaximumCapacity = maximumCapacity;
        }

        /// <summary>容量上限，-1 表示不限。</summary>
        public int MaximumCapacity { get; }

        /// <summary>当前闲置对象数量。</summary>
        public int IdleCount => _idle.Count;

        /// <summary>累计取出次数。</summary>
        public int TakenCount { get; private set; }

        /// <summary>累计归还成功次数。</summary>
        public int ReturnedCount { get; private set; }

        /// <summary>取一个闲置对象，桶空时返回 null。</summary>
        public object Take()
        {
            // 计数先加：桶空时由系统层用工厂补造一个交付给调用方，
            // 只有每一次「取出」都计入，TakenCount - ReturnedCount 才是准确的在外未还数。
            TakenCount++;
            if (_idle.Count == 0)
            {
                return null;
            }

            var instance = _idle.Pop();
            _idleSet.Remove(instance);
            return instance;
        }

        /// <summary>归还一个对象。桶满时不收，返回 false，由调用方自己丢弃。</summary>
        public bool Return(object instance)
        {
            if (instance == null)
            {
                return false;
            }

            // 同一个实例已经收在闲置区时不再收：重复归还是真实存在的 bug，
            // 收两次会让两个调用方先后拿到同一个对象。
            if (_idleSet.Contains(instance))
            {
                return false;
            }

            if (MaximumCapacity != -1 && _idle.Count >= MaximumCapacity)
            {
                return false;
            }

            _idle.Push(instance);
            _idleSet.Add(instance);
            ReturnedCount++;
            return true;
        }

        /// <summary>清空桶里的闲置对象，计数保留。</summary>
        public void Clear()
        {
            _idle.Clear();
            _idleSet.Clear();
        }

        /// <summary>预热时直接把实例放进闲置区，不计入归还计数（这些实例从未被取走）。</summary>
        internal void AddIdle(object instance)
        {
            _idle.Push(instance);
            _idleSet.Add(instance);
        }

        // 判「这个实例是不是已经在桶里」必须按引用比，不能按 Equals：
        // 业务类型重写了 Equals 之后，两个内容相同的不同实例会被当成同一个，
        // 于是第二个实例悄悄归不进池子。
        private sealed class InstanceReferenceComparer : IEqualityComparer<object>
        {
            public static readonly InstanceReferenceComparer Shared = new InstanceReferenceComparer();

            public new bool Equals(object left, object right) => ReferenceEquals(left, right);

            public int GetHashCode(object instance) => RuntimeHelpers.GetHashCode(instance);
        }
    }
}
