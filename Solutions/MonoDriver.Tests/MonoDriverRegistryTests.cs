using System;
using HSGFrame.MonoDriver;
using Xunit;

namespace HSGFrame.MonoDriver.Tests
{
    /// <summary>帧回调登记表的登记、派发、快照与异常语义测试。</summary>
    public class MonoDriverRegistryTests
    {
        [Fact]
        public void AddUpdateListenerIsInvokedOnTickUpdate()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            registry.AddUpdateListener(() => count++);

            registry.TickUpdate();

            Assert.Equal(1, count);
        }

        [Fact]
        public void AddLateUpdateListenerIsInvokedOnTickLateUpdate()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            registry.AddLateUpdateListener(() => count++);

            registry.TickLateUpdate();

            Assert.Equal(1, count);
        }

        [Fact]
        public void AddFixedUpdateListenerIsInvokedOnTickFixedUpdate()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            registry.AddFixedUpdateListener(_ => count++);

            registry.TickFixedUpdate(0.02f);

            Assert.Equal(1, count);
        }

        [Fact]
        public void ListenerCountsReflectRegisteredListeners()
        {
            var registry = new MonoDriverRegistry();
            registry.AddUpdateListener(() => { });
            registry.AddUpdateListener(() => { });
            registry.AddLateUpdateListener(() => { });
            registry.AddFixedUpdateListener(_ => { });

            Assert.Equal(2, registry.UpdateListenerCount);
            Assert.Equal(1, registry.LateUpdateListenerCount);
            Assert.Equal(1, registry.FixedUpdateListenerCount);
        }

        [Fact]
        public void DisposedHandleIsNotInvoked()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            var handle = registry.AddUpdateListener(() => count++);

            handle.Dispose();
            registry.TickUpdate();

            Assert.Equal(0, count);
            Assert.Equal(0, registry.UpdateListenerCount);
        }

        [Fact]
        public void DisposingHandleTwiceIsSafe()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            var handle = registry.AddUpdateListener(() => count++);

            handle.Dispose();
            handle.Dispose();
            registry.TickUpdate();

            Assert.Equal(0, count);
        }

        [Fact]
        public void SameDelegateRegisteredTwiceCountsAsTwo()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            Action listener = () => count++;
            var first = registry.AddUpdateListener(listener);
            var second = registry.AddUpdateListener(listener);

            Assert.Equal(2, registry.UpdateListenerCount);
            registry.TickUpdate();
            Assert.Equal(2, count);

            first.Dispose();
            Assert.Equal(1, registry.UpdateListenerCount);
            registry.TickUpdate();
            Assert.Equal(3, count);

            second.Dispose();
            Assert.Equal(0, registry.UpdateListenerCount);
        }

        [Fact]
        public void CallbackCanRemoveItselfWithoutException()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            IDisposable handle = null;
            handle = registry.AddUpdateListener(() =>
            {
                count++;
                handle.Dispose();
            });

            registry.TickUpdate();
            Assert.Equal(1, count);

            registry.TickUpdate();
            Assert.Equal(1, count);
        }

        [Fact]
        public void CallbackRegisteredDuringDispatchRunsOnNextTickOnly()
        {
            var registry = new MonoDriverRegistry();
            var firstCount = 0;
            var secondCount = 0;
            registry.AddUpdateListener(() =>
            {
                firstCount++;
                registry.AddUpdateListener(() => secondCount++);
            });

            registry.TickUpdate();
            Assert.Equal(1, firstCount);
            Assert.Equal(0, secondCount);

            registry.TickUpdate();
            Assert.Equal(2, firstCount);
            Assert.Equal(1, secondCount);
        }

        [Fact]
        public void ThrowingCallbackDoesNotPreventOthersAndThrowsAggregate()
        {
            var registry = new MonoDriverRegistry();
            var count = 0;
            registry.AddUpdateListener(() => throw new InvalidOperationException("第一个回调崩了"));
            registry.AddUpdateListener(() => count++);

            var exception = Assert.Throws<AggregateException>(() => registry.TickUpdate());

            Assert.Single(exception.InnerExceptions);
            Assert.IsType<InvalidOperationException>(exception.InnerExceptions[0]);
            Assert.Equal(1, count);
        }

        [Fact]
        public void TickFixedUpdatePassesDeltaToCallback()
        {
            var registry = new MonoDriverRegistry();
            var received = 0f;
            registry.AddFixedUpdateListener(delta => received = delta);

            registry.TickFixedUpdate(0.016f);

            Assert.Equal(0.016f, received, 3);
        }

        [Fact]
        public void ClearAllResetsAllCounters()
        {
            var registry = new MonoDriverRegistry();
            registry.AddUpdateListener(() => { });
            registry.AddLateUpdateListener(() => { });
            registry.AddFixedUpdateListener(_ => { });

            registry.ClearAll();

            Assert.Equal(0, registry.UpdateListenerCount);
            Assert.Equal(0, registry.LateUpdateListenerCount);
            Assert.Equal(0, registry.FixedUpdateListenerCount);
        }

        [Fact]
        public void TickWithNoListenersDoesNotThrow()
        {
            var registry = new MonoDriverRegistry();

            registry.TickUpdate();
            registry.TickLateUpdate();
            registry.TickFixedUpdate(0.02f);
        }

        [Fact]
        public void NullListenerThrowsArgumentNullException()
        {
            var registry = new MonoDriverRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.AddUpdateListener(null));
            Assert.Throws<ArgumentNullException>(() => registry.AddLateUpdateListener(null));
            Assert.Throws<ArgumentNullException>(() => registry.AddFixedUpdateListener(null));
        }
    }
}
