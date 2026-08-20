using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>冲突列表读写与裁决语义的测试：追加、三选一、防覆盖、就地裁决与坏数据容错。</summary>
    public class ConflictListTests
    {
        /// <summary>冲突列表文件不存在时返回空列表且失败原因为空串（空不是错）。</summary>
        [Fact]
        public void MissingFileIsEmptyListWithEmptyReason()
        {
            using var workspace = new PoolTestWorkspace();
            var list = ConflictList.Load(workspace.Root);

            Assert.Empty(list.Entries);
            Assert.Equal("", list.LoadFailureReason);
        }

        /// <summary>追加两条冲突，id 依次是 CF-0001、CF-0002。</summary>
        [Fact]
        public void AppendAssignsSequentialIdentifiers()
        {
            using var workspace = new PoolTestWorkspace();
            var first = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            var second = ConflictList.Append(workspace.Root, "DR-0001", "REQ-0001", "入库");

            Assert.Equal("CF-0001", first.Identifier);
            Assert.Equal("CF-0002", second.Identifier);
        }

        /// <summary>裁决「改旧的」后状态变已裁决，系统动作两条且第二条含新需求 id。</summary>
        [Fact]
        public void ResolveToModifyOldMarksResolvedWithActions()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");

            var result = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "改旧的", "2026-08-19T10:00:00+08:00");

            Assert.True(result.IsResolved);
            Assert.Equal(ConflictEntry.ResolvedState, result.Entry.State);
            Assert.Equal(2, result.SystemActions.Count);
            Assert.Contains("REQ-0042", result.SystemActions[1]);
        }

        /// <summary>裁决「强制推送」是合法裁决（IsResolved 为 true），但 PendingCount 仍把它算进未销账。</summary>
        [Fact]
        public void ForcePushResolvesButStillCountsAsPending()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");

            var result = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "强制推送", "2026-08-19T10:00:00+08:00");

            Assert.True(result.IsResolved);
            Assert.Equal("强制推送", result.Entry.Choice);
            // 强制推送是挂账不是销账：状态必须留在「未决」，否则「已裁决不许覆盖」
            // 会把后面的补选一并堵死，这条冲突就永远销不了账。
            Assert.Equal(ConflictEntry.PendingState, result.Entry.State);
            Assert.Equal(1, ConflictList.Load(workspace.Root).PendingCount());
        }

        /// <summary>强制推送挂账之后，补选「改旧的」能销账——这是总方案 §三 说的「事后补选」。</summary>
        [Fact]
        public void ForcePushCanBeClosedByLaterChoice()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "强制推送", "2026-08-19T10:00:00+08:00");

            var closing = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划乙", "改旧的", "2026-08-19T12:00:00+08:00");

            Assert.True(closing.IsResolved);
            Assert.Equal(ConflictEntry.ResolvedState, closing.Entry.State);
            Assert.Equal("改旧的", closing.Entry.Choice);
            Assert.Equal("策划乙", closing.Entry.ResolverName);
            Assert.Equal(0, ConflictList.Load(workspace.Root).PendingCount());
        }

        /// <summary>同一条重复强制推送失败：账已经挂上了，再挂只会把「挂了多久」查没。</summary>
        [Fact]
        public void RepeatedForcePushFails()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "强制推送", "2026-08-19T10:00:00+08:00");

            var again = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划乙", "强制推送", "2026-08-19T12:00:00+08:00");

            Assert.False(again.IsResolved);
            Assert.Contains("改新的", again.Reason);
            Assert.Contains("改旧的", again.Reason);
        }

        /// <summary>重复裁决同一条失败，原因含原来选的是什么。</summary>
        [Fact]
        public void ReResolveFailsAndMentionsPreviousChoice()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "改旧的", "2026-08-19T10:00:00+08:00");

            var again = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划乙", "改新的", "2026-08-19T11:00:00+08:00");

            Assert.False(again.IsResolved);
            Assert.Contains("改旧的", again.Reason);
        }

        /// <summary>非法 choice 失败，原因列出三个合法值。</summary>
        [Fact]
        public void IllegalChoiceFailsAndListsAllowedValues()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");

            var result = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "随便改", "2026-08-19T10:00:00+08:00");

            Assert.False(result.IsResolved);
            Assert.Contains("改新的", result.Reason);
            Assert.Contains("改旧的", result.Reason);
            Assert.Contains("强制推送", result.Reason);
        }

        /// <summary>非法发现阶段追加时抛 InvalidOperationException。</summary>
        [Fact]
        public void AppendWithIllegalStageThrows()
        {
            using var workspace = new PoolTestWorkspace();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "未知阶段"));

            Assert.Contains("入库", exception.Message);
            Assert.Contains("影响评估", exception.Message);
        }

        /// <summary>裁决只改那一条的「状态」与「裁决」两个键，另一条冲突逐字未变。</summary>
        [Fact]
        public void ResolveLeavesOtherEntriesUntouched()
        {
            using var workspace = new PoolTestWorkspace();
            var first = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Append(workspace.Root, "DR-0001", "REQ-0001", "入库");
            var beforeText = File.ReadAllText(PoolPaths.ConflictListFile(workspace.Root));

            ConflictList.Resolve(workspace.Root, first.Identifier, "策划甲", "改旧的", "2026-08-19T10:00:00+08:00");

            var afterText = File.ReadAllText(PoolPaths.ConflictListFile(workspace.Root));
            var beforeArray = JsonNode.Parse(beforeText) as JsonArray;
            var afterArray = JsonNode.Parse(afterText) as JsonArray;
            Assert.NotNull(beforeArray);
            Assert.NotNull(afterArray);
            Assert.Equal(beforeArray[1].ToJsonString(), afterArray[1].ToJsonString());
            Assert.Equal("已裁决", (string)afterArray[0]["状态"]);
            Assert.NotNull(afterArray[0]["裁决"]);
        }

        /// <summary>列表里混一条坏数据，其余照常读出，原因累加进 LoadFailureReason。</summary>
        [Fact]
        public void BadEntryIsSkippedAndOthersSurvive()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = PoolPaths.ConflictListFile(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              { "id": 123 },
              {
                "id": "CF-0003",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """, new UTF8Encoding(false));

            var list = ConflictList.Load(workspace.Root);

            Assert.Equal(2, list.Entries.Count);
            Assert.Contains("CF-0001", list.Entries.Select(entry => entry.Identifier));
            Assert.Contains("CF-0003", list.Entries.Select(entry => entry.Identifier));
            Assert.NotEqual("", list.LoadFailureReason);
        }
    }
}
