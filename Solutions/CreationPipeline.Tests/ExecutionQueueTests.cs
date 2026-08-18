using System;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>ExecutionQueue 的装载、入队、出队与落盘行为测试。</summary>
    public class ExecutionQueueTests
    {
        /// <summary>固定时刻：2026-08-18 10:00 +08:00，测试里一律用它，不许用 Now。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>队列文件不存在时 Load 出空队列，不抛异常，原因非空。</summary>
        [Fact]
        public void MissingQueueFileLoadsEmpty()
        {
            using var workspace = new PoolTestWorkspace();

            var queue = ExecutionQueue.Load(workspace.Root);

            Assert.Empty(queue.Entries);
            Assert.NotEqual("", queue.LoadFailureReason);
        }

        /// <summary>Enqueue 两条不同需求后 Entries 的顺序就是入队顺序。</summary>
        [Fact]
        public void EnqueuePreservesOrder()
        {
            var queue = new ExecutionQueue(null, "");

            Assert.True(queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认"));
            Assert.True(queue.Enqueue("REQ-0002", FixedMoment, "确认人已确认"));

            Assert.Equal(2, queue.Entries.Count);
            Assert.Equal("REQ-0001", queue.Entries[0].RequirementIdentifier);
            Assert.Equal("REQ-0002", queue.Entries[1].RequirementIdentifier);
        }

        /// <summary>同一个需求入队两次，第二次返回 false 且条数仍是 1（幂等）。</summary>
        [Fact]
        public void EnqueueDuplicateRequirementIsIdempotent()
        {
            var queue = new ExecutionQueue(null, "");

            Assert.True(queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认"));
            Assert.False(queue.Enqueue("REQ-0001", FixedMoment, "又确认了一次"));

            Assert.Single(queue.Entries);
        }

        /// <summary>TryDequeue 取的是队首（先进先出），取完 Entries 少一条。</summary>
        [Fact]
        public void TryDequeueTakesHeadFirst()
        {
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");
            queue.Enqueue("REQ-0002", FixedMoment, "确认人已确认");

            var ok = queue.TryDequeue(out var entry);

            Assert.True(ok);
            Assert.Equal("REQ-0001", entry.RequirementIdentifier);
            Assert.Single(queue.Entries);
            Assert.Equal("REQ-0002", queue.Entries[0].RequirementIdentifier);
        }

        /// <summary>空队列 TryDequeue 返回 false，entry 为 null。</summary>
        [Fact]
        public void TryDequeueOnEmptyReturnsFalse()
        {
            var queue = new ExecutionQueue(null, "");

            var ok = queue.TryDequeue(out var entry);

            Assert.False(ok);
            Assert.Null(entry);
        }

        /// <summary>Save 之后 Load 回来内容一致：需求 id 与理由都对得上。</summary>
        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            using var workspace = new PoolTestWorkspace();
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");
            queue.Enqueue("REQ-0042", FixedMoment, "双线终审通过");

            queue.Save(workspace.Root);

            var reloaded = ExecutionQueue.Load(workspace.Root);
            Assert.Equal("", reloaded.LoadFailureReason);
            Assert.Equal(2, reloaded.Entries.Count);
            Assert.Equal("REQ-0001", reloaded.Entries[0].RequirementIdentifier);
            Assert.Equal("确认人已确认", reloaded.Entries[0].Reason);
            Assert.Equal("REQ-0042", reloaded.Entries[1].RequirementIdentifier);
            Assert.Equal("双线终审通过", reloaded.Entries[1].Reason);
        }

        /// <summary>存盘的 JSON 里中文没被转义，文件文本里能直接看到「理由」两个字。</summary>
        [Fact]
        public void SavedJsonKeepsChineseUnescaped()
        {
            using var workspace = new PoolTestWorkspace();
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");

            queue.Save(workspace.Root);

            var text = File.ReadAllText(PoolPaths.QueueFile(workspace.Root));
            Assert.Contains("理由", text);
        }
    }
}
