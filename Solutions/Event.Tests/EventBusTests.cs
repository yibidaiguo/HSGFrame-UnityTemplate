using System;
using HSGFrame.Event;
using Xunit;

namespace HSGFrame.Event.Tests
{
    /// <summary>事件总线的订阅、派发、退订与异常隔离测试。</summary>
    public class EventBusTests
    {
        [Fact]
        public void NoParameterSubscribeAndPublish()
        {
            var bus = new EventBus();
            var receivedCount = 0;
            bus.Subscribe("开门", () => receivedCount++);

            bus.Publish("开门");

            Assert.Equal(1, receivedCount);
        }

        [Fact]
        public void PayloadSubscribeAndPublish()
        {
            var bus = new EventBus();
            var received = 0;
            bus.Subscribe<int>("得分", score => received += score);

            bus.Publish<int>("得分", 42);

            Assert.Equal(42, received);
        }

        [Fact]
        public void MultipleSubscribersAllReceive()
        {
            var bus = new EventBus();
            var first = 0;
            var second = 0;
            bus.Subscribe("事件", () => first++);
            bus.Subscribe("事件", () => second++);

            bus.Publish("事件");

            Assert.Equal(1, first);
            Assert.Equal(1, second);
        }

        [Fact]
        public void DisposedSubscriptionStopsReceiving()
        {
            var bus = new EventBus();
            var receivedCount = 0;
            var subscription = bus.Subscribe("事件", () => receivedCount++);
            subscription.Dispose();

            bus.Publish("事件");

            Assert.Equal(0, receivedCount);
        }

        [Fact]
        public void DisposeTwiceIsSafe()
        {
            var bus = new EventBus();
            var subscription = bus.Subscribe("事件", () => { });
            subscription.Dispose();
            subscription.Dispose();

            bus.Publish("事件");
        }

        [Fact]
        public void SameHandlerSubscribedTwiceCountsAsTwo()
        {
            var bus = new EventBus();
            var receivedCount = 0;
            Action handler = () => receivedCount++;
            bus.Subscribe("事件", handler);
            bus.Subscribe("事件", handler);

            bus.Publish("事件");

            Assert.Equal(2, receivedCount);
        }

        [Fact]
        public void SubscriberCountIsCorrect()
        {
            var bus = new EventBus();
            var first = bus.Subscribe("事件", () => { });
            var second = bus.Subscribe("事件", () => { });
            Assert.Equal(2, bus.SubscriberCount("事件"));

            first.Dispose();
            Assert.Equal(1, bus.SubscriberCount("事件"));

            Assert.Equal(0, bus.SubscriberCount("不存在"));
        }

        [Fact]
        public void UnsubscribeInsideCallbackDoesNotThrow()
        {
            var bus = new EventBus();
            var receivedCount = 0;
            IDisposable subscription = null;
            subscription = bus.Subscribe("事件", () =>
            {
                receivedCount++;
                subscription.Dispose();
            });

            bus.Publish("事件");
            bus.Publish("事件");

            Assert.Equal(1, receivedCount);
        }

        [Fact]
        public void SubscribeInsideCallbackDoesNotNotifyThisRound()
        {
            var bus = new EventBus();
            var firstCount = 0;
            var secondCount = 0;
            bus.Subscribe("事件", () =>
            {
                firstCount++;
                bus.Subscribe("事件", () => secondCount++);
            });

            bus.Publish("事件");
            Assert.Equal(1, firstCount);
            Assert.Equal(0, secondCount);

            bus.Publish("事件");
            Assert.Equal(2, firstCount);
            Assert.True(secondCount >= 1);
        }

        [Fact]
        public void ThrowingSubscriberDoesNotStopOthersAndThrowsAggregate()
        {
            var bus = new EventBus();
            var firstReceived = false;
            var secondReceived = false;
            bus.Subscribe("事件", () =>
            {
                firstReceived = true;
                throw new InvalidOperationException("爆炸");
            });
            bus.Subscribe("事件", () => secondReceived = true);

            var exception = Assert.Throws<AggregateException>(() => { bus.Publish("事件"); });

            Assert.True(firstReceived);
            Assert.True(secondReceived);
            Assert.Single(exception.InnerExceptions);
            Assert.IsType<InvalidOperationException>(exception.InnerExceptions[0]);
        }

        [Fact]
        public void MismatchedPayloadTypeIsSkipped()
        {
            var bus = new EventBus();
            var intReceived = 0;
            var stringReceived = 0;
            bus.Subscribe<int>("事件", _ => intReceived++);
            bus.Subscribe<string>("事件", _ => stringReceived++);

            bus.Publish<int>("事件", 1);
            Assert.Equal(1, intReceived);
            Assert.Equal(0, stringReceived);

            bus.Publish<string>("事件", "负载");
            Assert.Equal(1, intReceived);
            Assert.Equal(1, stringReceived);
        }

        [Fact]
        public void ClearEventRemovesAllSubscribers()
        {
            var bus = new EventBus();
            var receivedCount = 0;
            bus.Subscribe("事件", () => receivedCount++);
            bus.Subscribe("事件", () => receivedCount++);

            var cleared = bus.ClearEvent("事件");

            Assert.True(cleared);
            bus.Publish("事件");
            Assert.Equal(0, receivedCount);
        }

        [Fact]
        public void ClearAllResetsEventNameCount()
        {
            var bus = new EventBus();
            bus.Subscribe("甲", () => { });
            bus.Subscribe("乙", () => { });
            Assert.Equal(2, bus.EventNameCount);

            bus.ClearAll();

            Assert.Equal(0, bus.EventNameCount);
        }

        [Fact]
        public void EmptyEventNameThrowsArgumentException()
        {
            var bus = new EventBus();

            var exception = Assert.Throws<ArgumentException>(() => { bus.Subscribe("", () => { }); });

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void PublishWithNoSubscribersDoesNotThrow()
        {
            var bus = new EventBus();

            bus.Publish("无人订阅");
        }
    }
}
