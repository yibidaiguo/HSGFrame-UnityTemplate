using System;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>EngineDispatchRule 按引擎模式决定能不能自动取下一条的行为测试。</summary>
    public class EngineDispatchRuleTests
    {
        /// <summary>固定时刻：2026-08-18 10:00 +08:00。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>造一个队列里已有两条任务的执行队列。</summary>
        private static ExecutionQueue QueueWithTwoEntries()
        {
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");
            queue.Enqueue("REQ-0002", FixedMoment, "确认人已确认");
            return queue;
        }

        /// <summary>造一份指定模式的引擎配置，其余键取默认值。</summary>
        private static EngineSettings Settings(EngineMode mode)
        {
            return new EngineSettings(mode, 60, 2, 500000, 60, "");
        }

        /// <summary>值守模式 + 队列有两条 → 返回 false，reason 含「值守」，队列条数仍是 2（一条都没被取走）。</summary>
        [Fact]
        public void StandbyNeverTakesFromQueue()
        {
            var queue = QueueWithTwoEntries();

            var ok = EngineDispatchRule.TryTakeNext(Settings(EngineMode.Standby), queue, out var entry, out var reason);

            Assert.False(ok);
            Assert.Null(entry);
            Assert.Contains("值守", reason);
            Assert.Equal(2, queue.Entries.Count);
        }

        /// <summary>轮询模式 + 队列有两条 → 返回 true，取到队首，队列剩 1 条。</summary>
        [Fact]
        public void PollingTakesHead()
        {
            var queue = QueueWithTwoEntries();

            var ok = EngineDispatchRule.TryTakeNext(Settings(EngineMode.Polling), queue, out var entry, out var reason);

            Assert.True(ok);
            Assert.Equal("REQ-0001", entry.RequirementIdentifier);
            Assert.Single(queue.Entries);
            Assert.Contains("轮询", reason);
        }

        /// <summary>唤醒模式 + 队列有两条 → 返回 true，取到队首，队列剩 1 条。</summary>
        [Fact]
        public void WakeupTakesHead()
        {
            var queue = QueueWithTwoEntries();

            var ok = EngineDispatchRule.TryTakeNext(Settings(EngineMode.Wakeup), queue, out var entry, out var reason);

            Assert.True(ok);
            Assert.Equal("REQ-0001", entry.RequirementIdentifier);
            Assert.Single(queue.Entries);
            Assert.Contains("唤醒", reason);
        }

        /// <summary>任意非值守模式 + 空队列 → 返回 false，reason 含「队列为空」。</summary>
        [Fact]
        public void NonStandbyOnEmptyQueueReturnsFalse()
        {
            var queue = new ExecutionQueue(null, "");

            var ok = EngineDispatchRule.TryTakeNext(Settings(EngineMode.Polling), queue, out var entry, out var reason);

            Assert.False(ok);
            Assert.Null(entry);
            Assert.Contains("队列为空", reason);
        }

        /// <summary>引擎.json 不存在时 EngineSettings.Load 出来的 Mode 是 Standby，LoadFailureReason 非空。</summary>
        [Fact]
        public void MissingSettingsFileDefaultsToStandby()
        {
            using var workspace = new PoolTestWorkspace();

            var settings = EngineSettings.Load(workspace.Root);

            Assert.Equal(EngineMode.Standby, settings.Mode);
            Assert.NotEqual("", settings.LoadFailureReason);
        }
    }
}
