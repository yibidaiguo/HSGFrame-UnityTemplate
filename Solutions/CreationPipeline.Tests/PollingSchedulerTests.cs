using System;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>PollingScheduler 按模式与间隔决定一轮该不该取活的行为测试。</summary>
    public class PollingSchedulerTests
    {
        /// <summary>固定时刻：2026-08-20 10:00 +08:00。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>造一份指定模式的引擎配置，其余键取默认值；loadFailureReason 可指定。</summary>
        private static EngineSettings Settings(EngineMode mode, string loadFailureReason = "")
        {
            return new EngineSettings(mode, 60, 2, 500000, 60, loadFailureReason);
        }

        /// <summary>造一个队列里已有一条任务的执行队列。</summary>
        private static ExecutionQueue QueueWithOneEntry()
        {
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");
            return queue;
        }

        /// <summary>值守模式 + 队列里有活 + 唤醒信号 → ShouldTake 仍是 false：值守压倒一切。</summary>
        [Fact]
        public void StandbyOverridesWakeUpSignal()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Standby), queue, FixedMoment, FixedMoment.AddSeconds(-100), isWakeUp: true);

            Assert.False(decision.ShouldTake);
            Assert.Null(decision.Entry);
            Assert.Contains("值守", decision.Reason);
            Assert.Equal(0, decision.NextTickSeconds);
        }

        /// <summary>配置加载失败 → false，Reason 含那个失败原因。</summary>
        [Fact]
        public void LoadFailureReturnsFalseWithReason()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Polling, "引擎配置文件不存在：/nope/engine.json"), queue, FixedMoment, FixedMoment.AddSeconds(-100), isWakeUp: false);

            Assert.False(decision.ShouldTake);
            Assert.Contains("引擎配置文件不存在", decision.Reason);
            Assert.Contains("按值守处理", decision.Reason);
            Assert.Equal(0, decision.NextTickSeconds);
        }

        /// <summary>轮询模式、距上轮不足间隔 → false，NextTickSeconds 是剩余秒数。</summary>
        [Fact]
        public void PollingBelowIntervalWaitsRemainingSeconds()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Polling), queue, FixedMoment, FixedMoment.AddSeconds(-30), isWakeUp: false);

            Assert.False(decision.ShouldTake);
            Assert.Null(decision.Entry);
            Assert.Equal(30, decision.NextTickSeconds);
            Assert.Contains("还差 30 秒", decision.Reason);
        }

        /// <summary>轮询模式、超过间隔、队列有活 → true 且 Entry 非 null。</summary>
        [Fact]
        public void PollingPastIntervalTakesHead()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Polling), queue, FixedMoment, FixedMoment.AddSeconds(-61), isWakeUp: false);

            Assert.True(decision.ShouldTake);
            Assert.NotNull(decision.Entry);
            Assert.Equal("REQ-0001", decision.Entry.RequirementIdentifier);
            Assert.Equal(60, decision.NextTickSeconds);
        }

        /// <summary>轮询模式、超过间隔、队列空 → false，Reason 来自 TryTakeNext。</summary>
        [Fact]
        public void PollingPastIntervalOnEmptyQueueReturnsFalse()
        {
            var queue = new ExecutionQueue(null, "");

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Polling), queue, FixedMoment, FixedMoment.AddSeconds(-61), isWakeUp: false);

            Assert.False(decision.ShouldTake);
            Assert.Null(decision.Entry);
            Assert.Contains("队列为空", decision.Reason);
            Assert.Equal(60, decision.NextTickSeconds);
        }

        /// <summary>唤醒模式 + isWakeUp true + 距上轮不足间隔 → 跳过间隔检查，取到活。</summary>
        [Fact]
        public void WakeUpSkipsIntervalCheckAndTakes()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Wakeup), queue, FixedMoment, FixedMoment.AddSeconds(-5), isWakeUp: true);

            Assert.True(decision.ShouldTake);
            Assert.NotNull(decision.Entry);
            Assert.Contains("唤醒提前触发", decision.Reason);
        }

        /// <summary>非唤醒轮询超出间隔时 Reason 不含「唤醒提前触发」，且每条 Reason 都非空。</summary>
        [Fact]
        public void PollingWithoutWakeUpHasNonEmptyReason()
        {
            var queue = QueueWithOneEntry();

            var decision = PollingScheduler.Tick(
                Settings(EngineMode.Polling), queue, FixedMoment, FixedMoment.AddSeconds(-61), isWakeUp: false);

            Assert.NotEqual("", decision.Reason);
            Assert.DoesNotContain("唤醒提前触发", decision.Reason);
        }
    }
}
