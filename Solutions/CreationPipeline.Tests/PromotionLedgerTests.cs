using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 晋升提案账本测试：目录不存在是空账本、坏文件与空账本分开、入库幂等只挡未关闭、
    /// 终态不挡新提案、状态机三条合法转换与非法转换拒绝。
    /// </summary>
    public class PromotionLedgerTests
    {
        /// <summary>造一条去检查器的提案。</summary>
        private static PromotionProposal Proposal(string category)
        {
            return new PromotionProposal(
                category,
                3,
                "可代码化",
                "检查器",
                new[] { "签到" },
                new[] { "这里没判 null" });
        }

        /// <summary>目录不存在 → 空账本、LoadFailureReason 是空串（空账本是正常状态）。</summary>
        [Fact]
        public void MissingDirectoryLoadsEmptyLedger()
        {
            using var workspace = new PoolTestWorkspace();

            var ledger = PromotionLedger.Load(workspace.Root);

            Assert.Empty(ledger.Records);
            Assert.Equal("", ledger.LoadFailureReason);
        }

        /// <summary>一个坏 JSON 文件 → LoadFailureReason 非空（与空账本能分开，锁定决策 42）。</summary>
        [Fact]
        public void BrokenFileProducesLoadFailureReason()
        {
            using var workspace = new PoolTestWorkspace();
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据，
            // 裸中文写在这里会被当成「标识符含中文」判红。
            Directory.CreateDirectory(PoolPaths.PromotionProposalDirectory(workspace.Root));
            File.WriteAllText(
                Path.Combine(PoolPaths.PromotionProposalDirectory(workspace.Root), "PR-0001.json"),
                "not-json",
                new UTF8Encoding(false));

            var ledger = PromotionLedger.Load(workspace.Root);

            Assert.Empty(ledger.Records);
            Assert.Contains("PR-0001.json", ledger.LoadFailureReason);
        }

        /// <summary>追加两条不同类别 → PR-0001 / PR-0002，状态都是待批。</summary>
        [Fact]
        public void AppendCreatesSequentialIdentifiers()
        {
            using var workspace = new PoolTestWorkspace();

            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);
            var second = PromotionLedger.Append(workspace.Root, Proposal("命名歧义"), "2026-08-20T10:00:01+09:00", out _);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal("PR-0001", first.Identifier);
            Assert.Equal("PR-0002", second.Identifier);
            Assert.Equal(PromotionRecord.PendingState, first.State);
            Assert.Equal(PromotionRecord.PendingState, second.State);

            var ledger = PromotionLedger.Load(workspace.Root);
            Assert.Equal(2, ledger.Records.Count);
            Assert.Equal("PR-0001", ledger.Records[0].Identifier);
            Assert.Equal("PR-0002", ledger.Records[1].Identifier);
            Assert.Equal(2, ledger.OpenCount());
        }

        /// <summary>同类别第二次追加（第一条还是待批）→ 不入库，reason 提到被哪条挡住。</summary>
        [Fact]
        public void AppendRejectsOpenDuplicateCategory()
        {
            using var workspace = new PoolTestWorkspace();

            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);
            var second = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:01+09:00", out var reason);

            Assert.NotNull(first);
            Assert.Null(second);
            Assert.Contains("PR-0001", reason);
            Assert.Contains("未关闭", reason);
        }

        /// <summary>同类别的第一条改成 已拒绝 之后再追加 → 成功入库（终态不挡新提案）。</summary>
        [Fact]
        public void AppendSucceedsAfterTerminalRejection()
        {
            using var workspace = new PoolTestWorkspace();

            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);
            var rejected = PromotionLedger.UpdateState(
                workspace.Root, first.Identifier, PromotionRecord.RejectedState, "张三", "2026-08-20T11:00:00+09:00", "", out _);
            Assert.True(rejected);

            var second = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T12:00:00+09:00", out var reason);

            Assert.NotNull(second);
            Assert.Equal("PR-0002", second.Identifier);
            Assert.Equal("", reason);
        }

        /// <summary>晋升去向是 无 的提案 → 不入库，reason 说清没有落点。</summary>
        [Fact]
        public void AppendRejectsUnroutableChannel()
        {
            using var workspace = new PoolTestWorkspace();
            var proposal = new PromotionProposal(
                "随手一写", 3, "不可规则化", "无", new[] { "签到" }, new[] { "这句没法规则化" });

            var record = PromotionLedger.Append(workspace.Root, proposal, "2026-08-20T10:00:00+09:00", out var reason);

            Assert.Null(record);
            Assert.Contains("落点", reason);
        }

        /// <summary>待批 → 已落地 → 失败，reason 提到必须先批准。</summary>
        [Fact]
        public void UpdateStateRejectsSkippingApproval()
        {
            using var workspace = new PoolTestWorkspace();
            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);

            var ok = PromotionLedger.UpdateState(
                workspace.Root, first.Identifier, PromotionRecord.LandedState, "", "", "Proposals/Checkers/空引用未防.md", out var reason);

            Assert.False(ok);
            Assert.Contains("先批准", reason);
        }

        /// <summary>已拒绝 之后再改成 已批准 → 失败，reason 提到当时的状态（终态不许覆盖）。</summary>
        [Fact]
        public void UpdateStateRejectsTerminalOverwrite()
        {
            using var workspace = new PoolTestWorkspace();
            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);
            PromotionLedger.UpdateState(
                workspace.Root, first.Identifier, PromotionRecord.RejectedState, "张三", "2026-08-20T11:00:00+09:00", "", out _);

            var ok = PromotionLedger.UpdateState(
                workspace.Root, first.Identifier, PromotionRecord.ApprovedState, "李四", "2026-08-20T12:00:00+09:00", "", out var reason);

            Assert.False(ok);
            Assert.Contains(PromotionRecord.RejectedState, reason);
            Assert.Contains("终态", reason);
        }

        /// <summary>批准时裁决人为空 → 失败。</summary>
        [Fact]
        public void UpdateStateRequiresDeciderNameForApproval()
        {
            using var workspace = new PoolTestWorkspace();
            var first = PromotionLedger.Append(workspace.Root, Proposal("空引用未防"), "2026-08-20T10:00:00+09:00", out _);

            var ok = PromotionLedger.UpdateState(
                workspace.Root, first.Identifier, PromotionRecord.ApprovedState, "", "2026-08-20T11:00:00+09:00", "", out var reason);

            Assert.False(ok);
            Assert.Contains("裁决人", reason);
        }
    }
}
