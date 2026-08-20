using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>同步水位读写测试：空水位=全量拉、只许前进、幂等重放不算前进、各 driver 互不干扰、显式回退。</summary>
    public class SyncWatermarkTests
    {
        /// <summary>文件不存在 → 空水位、原因空串（空水位=全量拉），Find 返回零水位条目而不是 null。</summary>
        [Fact]
        public void MissingFileLoadsEmptyWatermarkWithEmptyReason()
        {
            using var workspace = new PoolTestWorkspace();

            var watermark = SyncWatermark.Load(workspace.Root);

            Assert.Empty(watermark.Entries);
            Assert.Equal("", watermark.LoadFailureReason);
            var entry = watermark.Find("feishu");
            Assert.Equal("", entry.Moment);
            Assert.Equal("", entry.RecordIdentifier);
        }

        /// <summary>顶层不是对象 → 空水位且原因非空，不许静默当成空。</summary>
        [Fact]
        public void NonObjectTopLevelFailsWithReason()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = PipelinePaths.SyncWatermarkFile(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, "[1,2,3]", new UTF8Encoding(false));

            var watermark = SyncWatermark.Load(workspace.Root);

            Assert.Empty(watermark.Entries);
            Assert.NotEqual("", watermark.LoadFailureReason);
        }

        /// <summary>首次 Advance 写成功；再 Advance 一个更晚的时间成功且 Advanced 为 true。</summary>
        [Fact]
        public void FirstAdvanceWritesAndLaterAdvanceMovesForward()
        {
            using var workspace = new PoolTestWorkspace();

            var first = SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T10:00:00Z", "rec-7788");
            Assert.True(first.Succeeded);
            Assert.True(first.Advanced);

            var second = SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T11:00:00Z", "rec-7789");
            Assert.True(second.Succeeded);
            Assert.True(second.Advanced);

            var entry = SyncWatermark.Load(workspace.Root).Find("feishu");
            Assert.Equal("2026-08-20T11:00:00Z", entry.Moment);
            Assert.Equal("rec-7789", entry.RecordIdentifier);
        }

        /// <summary>Advance 一个更早的时间 → 失败，原因含「只许前进」并指路 Rewind。</summary>
        [Fact]
        public void AdvanceToEarlierTimeFailsWithOnlyForwardMessage()
        {
            using var workspace = new PoolTestWorkspace();
            SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T11:00:00Z", "rec-7789");

            var result = SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T10:00:00Z", "rec-7788");

            Assert.False(result.Succeeded);
            Assert.Contains("只许前进", result.FailureReason);
            Assert.Contains("Rewind", result.FailureReason);
        }

        /// <summary>Advance 相同时间 → 成功但 Advanced 为 false（幂等重放不算错也不算前进）。</summary>
        [Fact]
        public void AdvanceToSameTimeSucceedsButDoesNotAdvance()
        {
            using var workspace = new PoolTestWorkspace();
            SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T10:00:00Z", "rec-7788");

            var result = SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T10:00:00Z", "rec-7788");

            Assert.True(result.Succeeded);
            Assert.False(result.Advanced);
        }

        /// <summary>两个 driver 各记各的：给 B 记水位，A 的条目逐字未动。</summary>
        [Fact]
        public void DriversAreIndependent()
        {
            using var workspace = new PoolTestWorkspace();
            SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T10:00:00Z", "rec-7788");
            var filePath = PipelinePaths.SyncWatermarkFile(workspace.Root);
            var beforeRoot = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;

            SyncWatermark.Advance(workspace.Root, "jira", "2026-08-20T09:00:00Z", "rec-jira-1");

            var afterRoot = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
            Assert.NotNull(beforeRoot);
            Assert.NotNull(afterRoot);
            Assert.Equal(beforeRoot["feishu"].ToJsonString(), afterRoot["feishu"].ToJsonString());
            Assert.Equal("rec-jira-1", SyncWatermark.Load(workspace.Root).Find("jira").RecordIdentifier);
        }

        /// <summary>Rewind 到更早的时间 → 成功（显式重拉的正门，不做前进检查）。</summary>
        [Fact]
        public void RewindToEarlierTimeSucceeds()
        {
            using var workspace = new PoolTestWorkspace();
            SyncWatermark.Advance(workspace.Root, "feishu", "2026-08-20T11:00:00Z", "rec-7789");

            var result = SyncWatermark.Rewind(workspace.Root, "feishu", "2026-08-20T09:00:00Z", "rec-7700");

            Assert.True(result.Succeeded);
            var entry = SyncWatermark.Load(workspace.Root).Find("feishu");
            Assert.Equal("2026-08-20T09:00:00Z", entry.Moment);
            Assert.Equal("rec-7700", entry.RecordIdentifier);
        }
    }
}
