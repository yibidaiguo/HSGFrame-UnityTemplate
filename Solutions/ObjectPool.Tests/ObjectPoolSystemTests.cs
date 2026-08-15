using System;
using System.Collections.Generic;
using HSGFrame.ObjectPool;
using Xunit;

namespace HSGFrame.ObjectPool.Tests
{
    /// <summary>对象池系统与对象桶的取还、计数与容量测试。</summary>
    public class ObjectPoolSystemTests
    {
        /// <summary>测试用的工厂产物类型。</summary>
        private sealed class TestInstance
        {
        }

        [Fact]
        public void TakeAfterRegisterReturnsFactoryMadeInstance()
        {
            var system = new ObjectPoolSystem();
            var created = new List<object>();
            system.Register("实体", () =>
            {
                var instance = new TestInstance();
                created.Add(instance);
                return instance;
            });

            var taken = system.Take("实体");

            Assert.IsType<TestInstance>(taken);
            Assert.Contains(taken, created);
        }

        [Fact]
        public void ReturnThenTakeReturnsSameInstance()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance());
            var instance = system.Take("实体");

            var returned = system.Return("实体", instance);

            Assert.True(returned);
            Assert.Same(instance, system.Take("实体"));
        }

        [Fact]
        public void ReturnReturnsFalseWhenBucketIsFull()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance(), maximumCapacity: 1);
            var first = system.Take("实体");
            var second = system.Take("实体");

            Assert.True(system.Return("实体", first));
            Assert.False(system.Return("实体", second));
        }

        [Fact]
        public void TakeUsesFactoryWhenBucketIsEmpty()
        {
            var system = new ObjectPoolSystem();
            var createdCount = 0;
            system.Register("实体", () =>
            {
                createdCount++;
                return new TestInstance();
            });

            system.Take("实体");
            system.Take("实体");
            system.Take("实体");

            Assert.Equal(3, createdCount);
        }

        [Fact]
        public void WarmUpPrefillsBucket()
        {
            var system = new ObjectPoolSystem();
            var createdCount = 0;
            system.Register("实体", () =>
            {
                createdCount++;
                return new TestInstance();
            }, warmUpCount: 3);

            Assert.Equal(3, createdCount);
            Assert.Equal(3, system.FindBucket("实体").IdleCount);
        }

        [Fact]
        public void TakenAndReturnedCountsTrackInFlightInstances()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance());
            var first = system.Take("实体");
            var second = system.Take("实体");
            var third = system.Take("实体");

            system.Return("实体", first);
            system.Return("实体", second);

            var bucket = system.FindBucket("实体");
            Assert.Equal(3, bucket.TakenCount);
            Assert.Equal(2, bucket.ReturnedCount);
        }

        [Fact]
        public void ReturningSameInstanceTwiceCountsOnce()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance());
            var instance = system.Take("实体");

            Assert.True(system.Return("实体", instance));
            Assert.False(system.Return("实体", instance));

            var bucket = system.FindBucket("实体");
            Assert.Equal(1, bucket.ReturnedCount);
            Assert.Equal(1, bucket.IdleCount);
        }

        [Fact]
        public void ReturnNullReturnsFalse()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance());

            Assert.False(system.Return("实体", null));
        }

        [Fact]
        public void TakeUnregisteredKeyThrowsObjectPoolException()
        {
            var system = new ObjectPoolSystem();

            var exception = Assert.Throws<ObjectPoolException>(() => system.Take("未登记"));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void ReturnUnregisteredKeyReturnsFalse()
        {
            var system = new ObjectPoolSystem();

            Assert.False(system.Return("未登记", new TestInstance()));
        }

        [Fact]
        public void RegisterByTypeThenTakeAndReturnByType()
        {
            var system = new ObjectPoolSystem();
            system.Register<TestInstance>();

            var instance = system.Take<TestInstance>();

            Assert.IsType<TestInstance>(instance);
            Assert.True(system.Return(instance));
            Assert.Same(instance, system.Take<TestInstance>());
        }

        [Fact]
        public void ClearBucketResetsIdleCountButKeepsCounters()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance(), warmUpCount: 2);
            var instance = system.Take("实体");
            system.Return("实体", instance);

            var cleared = system.ClearBucket("实体");
            var bucket = system.FindBucket("实体");

            Assert.True(cleared);
            Assert.Equal(0, bucket.IdleCount);
            Assert.Equal(1, bucket.TakenCount);
            Assert.Equal(1, bucket.ReturnedCount);
        }

        [Fact]
        public void ClearAllResetsBucketCount()
        {
            var system = new ObjectPoolSystem();
            system.Register("甲", () => new TestInstance());
            system.Register("乙", () => new TestInstance());
            Assert.Equal(2, system.BucketCount);

            system.ClearAll();

            Assert.Equal(0, system.BucketCount);
        }

        [Fact]
        public void UnlimitedCapacityAcceptsEveryReturn()
        {
            var system = new ObjectPoolSystem();
            system.Register("实体", () => new TestInstance(), maximumCapacity: -1);
            var first = system.Take("实体");
            var second = system.Take("实体");
            var third = system.Take("实体");

            Assert.True(system.Return("实体", first));
            Assert.True(system.Return("实体", second));
            Assert.True(system.Return("实体", third));
        }

        [Fact]
        public void FindBucketReturnsNullForUnregisteredKey()
        {
            var system = new ObjectPoolSystem();

            Assert.Null(system.FindBucket("未登记"));
        }
    }
}
