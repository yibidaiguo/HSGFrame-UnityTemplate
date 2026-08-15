using System;
using System.Collections.Generic;

namespace HSGFrame.ObjectPool
{
    /// <summary>对象池：按键管理多个对象桶，键可以是类型名，也可以是调用方自定的字符串。</summary>
    public sealed class ObjectPoolSystem
    {
        private readonly Dictionary<string, ObjectPoolBucket> _buckets = new Dictionary<string, ObjectPoolBucket>();
        private readonly Dictionary<string, Func<object>> _factories = new Dictionary<string, Func<object>>();

        /// <summary>已登记的桶数量。</summary>
        public int BucketCount => _buckets.Count;

        /// <summary>为某个键登记一个桶并可预热若干实例，重复登记时沿用已有的桶。</summary>
        /// <param name="key">桶的键。</param>
        /// <param name="factory">造实例的工厂，预热与取不到时用它。</param>
        /// <param name="maximumCapacity">容量上限，-1 表示不限。</param>
        /// <param name="warmUpCount">预热实例数。</param>
        public void Register(string key, Func<object> factory, int maximumCapacity = -1, int warmUpCount = 0)
        {
            if (_buckets.ContainsKey(key))
            {
                // 重复登记沿用已有的桶，不覆盖工厂与容量上限。
                return;
            }

            var bucket = new ObjectPoolBucket(maximumCapacity);
            _buckets.Add(key, bucket);
            _factories.Add(key, factory);

            // 预热受容量上限约束，超出的部分不再多造。
            var warmUpLimit = maximumCapacity == -1 ? warmUpCount : Math.Min(warmUpCount, maximumCapacity);
            for (var index = 0; index < warmUpLimit; index++)
            {
                bucket.AddIdle(factory());
            }
        }

        /// <summary>按类型登记一个桶，键取类型的全名。</summary>
        public void Register<T>(int maximumCapacity = -1, int warmUpCount = 0) where T : new()
        {
            Register(typeof(T).FullName, () => new T(), maximumCapacity, warmUpCount);
        }

        /// <summary>取一个对象：桶里有就复用，没有就用工厂造一个。键没登记过时抛 ObjectPoolException。</summary>
        public object Take(string key)
        {
            var bucket = GetBucketOrThrow(key);
            var instance = bucket.Take();
            if (instance == null)
            {
                instance = _factories[key]();
            }

            return instance;
        }

        /// <summary>按类型取一个对象。</summary>
        public T Take<T>() where T : class
        {
            return (T)Take(typeof(T).FullName);
        }

        /// <summary>归还一个对象，桶满或键没登记时返回 false。</summary>
        public bool Return(string key, object instance)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                return false;
            }

            return bucket.Return(instance);
        }

        /// <summary>按类型归还。</summary>
        public bool Return<T>(T instance) where T : class
        {
            return Return(typeof(T).FullName, instance);
        }

        /// <summary>取某个桶的统计快照，键没登记时返回 null。</summary>
        public ObjectPoolBucket FindBucket(string key)
        {
            return _buckets.TryGetValue(key, out var bucket) ? bucket : null;
        }

        /// <summary>清空某个桶。</summary>
        public bool ClearBucket(string key)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                return false;
            }

            bucket.Clear();
            return true;
        }

        /// <summary>清空全部桶。</summary>
        public void ClearAll()
        {
            _buckets.Clear();
            _factories.Clear();
        }

        private ObjectPoolBucket GetBucketOrThrow(string key)
        {
            if (_buckets.TryGetValue(key, out var bucket))
            {
                return bucket;
            }

            throw new ObjectPoolException(
                location: "ObjectPoolSystem.Take",
                cause: $"键“{key}”没有登记过桶",
                fix: "先调用 Register 登记这个键，再取对象",
                reference: "参见 ObjectPoolSystem.Register 的说明");
        }
    }

    /// <summary>对象池用法错误时抛出，消息按四要素书写。</summary>
    public sealed class ObjectPoolException : Exception
    {
        /// <summary>用现成的消息构造。</summary>
        public ObjectPoolException(string message) : base(message)
        {
        }

        /// <summary>按位置、原因、修复、参考四要素拼装消息。</summary>
        public ObjectPoolException(string location, string cause, string fix, string reference)
            : base($"位置：{location}；原因：{cause}；修复：{fix}；参考：{reference}")
        {
        }
    }
}
