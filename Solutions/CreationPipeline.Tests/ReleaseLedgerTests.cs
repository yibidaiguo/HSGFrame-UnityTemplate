using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>放行流水读写与抽查销账的测试：追加取号、只追加、就地改抽查状态与坏数据容错。</summary>
    public class ReleaseLedgerTests
    {
        /// <summary>流水文件不存在时返回空流水且失败原因为空串（空流水是正常状态，不是错）。</summary>
        [Fact]
        public void MissingFileIsEmptyLedgerWithEmptyReason()
        {
            using var workspace = new PoolTestWorkspace();
            var ledger = ReleaseLedger.Load(workspace.Root);

            Assert.Empty(ledger.Entries);
            Assert.Equal("", ledger.LoadFailureReason);
        }

        /// <summary>文件是坏 JSON 时 Entries 空且 LoadFailureReason 非空——与「空流水」必须能分开。</summary>
        [Fact]
        public void BadJsonYieldsEmptyEntriesWithNonEmptyReason()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = PoolPaths.ReleaseLedgerFile(workspace.Root);
            File.WriteAllText(filePath, "not a json", new UTF8Encoding(false));

            var ledger = ReleaseLedger.Load(workspace.Root);

            Assert.Empty(ledger.Entries);
            Assert.NotEqual("", ledger.LoadFailureReason);
        }

        /// <summary>追加三条流水，id 依次是 RL-0001、RL-0002、RL-0003，抽查状态全是未抽查。</summary>
        [Fact]
        public void AppendAssignsSequentialIdentifiers()
        {
            using var workspace = new PoolTestWorkspace();
            var first = ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "a1b2c3d4");
            var second = ReleaseLedger.Append(workspace.Root, "REQ-0001", "低", new[] { "业务", "其他" }, "2026-08-20T11:00:00+09:00", "");
            var third = ReleaseLedger.Append(workspace.Root, "REQ-0043", "常规", new[] { "其他" }, "2026-08-20T12:00:00+09:00", "");

            Assert.Equal("RL-0001", first.Identifier);
            Assert.Equal("RL-0002", second.Identifier);
            Assert.Equal("RL-0003", third.Identifier);
            Assert.Equal("未抽查", first.SpotCheckState);
            Assert.Equal("未抽查", second.SpotCheckState);
            Assert.Equal("未抽查", third.SpotCheckState);
            Assert.Equal("", first.SpotCheckConclusion);
            Assert.Equal("", first.RevertCommit);
        }

        /// <summary>追加后重新 Load，三条都读得回来，且范围数组原样保留。</summary>
        [Fact]
        public void AppendThenReloadKeepsAllFields()
        {
            using var workspace = new PoolTestWorkspace();
            ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务", "其他" }, "2026-08-20T10:00:00+09:00", "a1b2c3d4");

            var ledger = ReleaseLedger.Load(workspace.Root);

            var entry = Assert.Single(ledger.Entries);
            Assert.Equal("RL-0001", entry.Identifier);
            Assert.Equal("REQ-0042", entry.RequirementIdentifier);
            Assert.Equal("低", entry.Grade);
            Assert.Equal(new[] { "业务", "其他" }, entry.Scopes);
            Assert.Equal("2026-08-20T10:00:00+09:00", entry.ReleasedMoment);
            Assert.Equal("a1b2c3d4", entry.MergeCommit);
        }

        /// <summary>记「合格」成功；再记一次失败，reason 提到已经抽查过。</summary>
        [Fact]
        public void RecordSpotCheckOnceThenAgainFails()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "");

            var firstOk = ReleaseLedger.RecordSpotCheck(workspace.Root, entry.Identifier, "合格", "抽查无误", "", out var firstReason);
            Assert.True(firstOk);
            Assert.Equal("", firstReason);

            var again = ReleaseLedger.RecordSpotCheck(workspace.Root, entry.Identifier, "发现问题", "这里有问题", "", out var againReason);
            Assert.False(again);
            Assert.Contains("已经抽查过", againReason);
        }

        /// <summary>传「未抽查」当结论状态失败——抽查是销账动作，不许把账再挂回去。</summary>
        [Fact]
        public void RecordSpotCheckWithUncheckedStateFails()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "");

            var ok = ReleaseLedger.RecordSpotCheck(workspace.Root, entry.Identifier, "未抽查", "", "", out var reason);

            Assert.False(ok);
            Assert.Contains("未抽查", reason);
        }

        /// <summary>传找不到的 id 失败。</summary>
        [Fact]
        public void RecordSpotCheckWithUnknownIdentifierFails()
        {
            using var workspace = new PoolTestWorkspace();
            ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "");

            var ok = ReleaseLedger.RecordSpotCheck(workspace.Root, "RL-9999", "合格", "", "", out var reason);

            Assert.False(ok);
            Assert.Contains("RL-9999", reason);
        }

        /// <summary>RecordSpotCheck 只改那一条的三个键，另一条流水逐字未变。</summary>
        [Fact]
        public void RecordSpotCheckLeavesOtherEntriesUntouched()
        {
            using var workspace = new PoolTestWorkspace();
            var first = ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "a1b2c3d4");
            ReleaseLedger.Append(workspace.Root, "REQ-0001", "低", new[] { "其他" }, "2026-08-20T11:00:00+09:00", "");
            var beforeText = File.ReadAllText(PoolPaths.ReleaseLedgerFile(workspace.Root));

            ReleaseLedger.RecordSpotCheck(workspace.Root, first.Identifier, "发现问题", "范围判断错了", "d4c3b2a1", out _);

            var afterText = File.ReadAllText(PoolPaths.ReleaseLedgerFile(workspace.Root));
            var beforeRoot = JsonNode.Parse(beforeText) as JsonObject;
            var afterRoot = JsonNode.Parse(afterText) as JsonObject;
            Assert.NotNull(beforeRoot);
            Assert.NotNull(afterRoot);
            var beforeArray = beforeRoot["条目"] as JsonArray;
            var afterArray = afterRoot["条目"] as JsonArray;
            Assert.NotNull(beforeArray);
            Assert.NotNull(afterArray);
            Assert.Equal(beforeArray[1].ToJsonString(), afterArray[1].ToJsonString());
            Assert.Equal("发现问题", (string)afterArray[0]["抽查状态"]);
            Assert.Equal("范围判断错了", (string)afterArray[0]["抽查结论"]);
            Assert.Equal("d4c3b2a1", (string)afterArray[0]["回滚提交"]);
        }

        /// <summary>UncheckedCount 与 ProblemCount 各一条断言：追加三条后未抽查 3、问题 0，记两条后对应变化。</summary>
        [Fact]
        public void CountsTrackSpotCheckState()
        {
            using var workspace = new PoolTestWorkspace();
            var first = ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", "");
            ReleaseLedger.Append(workspace.Root, "REQ-0001", "低", new[] { "其他" }, "2026-08-20T11:00:00+09:00", "");
            ReleaseLedger.Append(workspace.Root, "REQ-0043", "常规", new[] { "其他" }, "2026-08-20T12:00:00+09:00", "");

            var before = ReleaseLedger.Load(workspace.Root);
            Assert.Equal(3, before.UncheckedCount());
            Assert.Equal(0, before.ProblemCount());

            ReleaseLedger.RecordSpotCheck(workspace.Root, first.Identifier, "发现问题", "有问题", "", out _);

            var after = ReleaseLedger.Load(workspace.Root);
            Assert.Equal(2, after.UncheckedCount());
            Assert.Equal(1, after.ProblemCount());
        }

        /// <summary>账本读不动时 Append 拒绝追加，抛 InvalidOperationException 并带上原因。</summary>
        [Fact]
        public void AppendRejectsWhenLedgerUnreadable()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = PoolPaths.ReleaseLedgerFile(workspace.Root);
            File.WriteAllText(filePath, "not a json", new UTF8Encoding(false));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ReleaseLedger.Append(workspace.Root, "REQ-0042", "低", new[] { "业务" }, "2026-08-20T10:00:00+09:00", ""));

            Assert.NotEqual("", exception.Message);
        }
    }
}
