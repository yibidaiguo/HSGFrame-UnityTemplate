using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>晋升提案构建器测试：分组、阈值、平票取更严、引用条数上限与排序。</summary>
    public class PromotionProposalBuilderTests
    {
        /// <summary>造一条意见。</summary>
        private static ReviewOpinion Opinion(string identifier, string category, string rulability, string moduleName, string quotation)
        {
            return new ReviewOpinion(identifier, category, moduleName, rulability, quotation, "2026-08-20T10:00:00+09:00");
        }

        /// <summary>造一份含给定意见的库。</summary>
        private static ReviewOpinionBook Book(params ReviewOpinion[] opinions)
        {
            return new ReviewOpinionBook(opinions, "");
        }

        /// <summary>空库 → 空列表，不是问题。</summary>
        [Fact]
        public void EmptyBookBuildsEmptyList()
        {
            var proposals = PromotionProposalBuilder.Build(new ReviewOpinionBook(Array.Empty<ReviewOpinion>(), ""), 3);

            Assert.Empty(proposals);
        }

        /// <summary>同类 3 条、阈值 3 → 出一条提案，Count 是 3。</summary>
        [Fact]
        public void GroupAtThresholdProducesProposal()
        {
            var book = Book(
                Opinion("OP-0001", "空引用未防", "可代码化", "签到", "a"),
                Opinion("OP-0002", "空引用未防", "可代码化", "签到", "b"),
                Opinion("OP-0003", "空引用未防", "可代码化", "签到", "c"));

            var proposals = PromotionProposalBuilder.Build(book, 3);

            var proposal = Assert.Single(proposals);
            Assert.Equal("空引用未防", proposal.Category);
            Assert.Equal(3, proposal.Count);
            Assert.Equal("可代码化", proposal.Rulability);
            Assert.Equal("检查器", proposal.TargetChannel);
        }

        /// <summary>同类 2 条、阈值 3 → 零提案。</summary>
        [Fact]
        public void GroupBelowThresholdProducesNothing()
        {
            var book = Book(
                Opinion("OP-0001", "空引用未防", "可代码化", "签到", "a"),
                Opinion("OP-0002", "空引用未防", "可代码化", "签到", "b"));

            var proposals = PromotionProposalBuilder.Build(book, 3);

            Assert.Empty(proposals);
        }

        /// <summary>组内可代码化与可提示词化各 2 条（平票）→ TargetChannel 是检查器（取更严）。</summary>
        [Fact]
        public void TieRulabilityPicksStricterChannel()
        {
            var book = Book(
                Opinion("OP-0001", "空引用未防", "可代码化", "签到", "a"),
                Opinion("OP-0002", "空引用未防", "可代码化", "签到", "b"),
                Opinion("OP-0003", "空引用未防", "可提示词化", "签到", "c"),
                Opinion("OP-0004", "空引用未防", "可提示词化", "签到", "d"));

            var proposals = PromotionProposalBuilder.Build(book, 1);

            var proposal = Assert.Single(proposals);
            Assert.Equal("检查器", proposal.TargetChannel);
            Assert.Equal("可代码化", proposal.Rulability);
        }

        /// <summary>Quotations 最多 3 条，按 id 序数序取前三。</summary>
        [Fact]
        public void QuotationsCappedAtThree()
        {
            var book = Book(
                Opinion("OP-0001", "空引用未防", "可代码化", "签到", "第一条"),
                Opinion("OP-0002", "空引用未防", "可代码化", "签到", "第二条"),
                Opinion("OP-0003", "空引用未防", "可代码化", "签到", "第三条"),
                Opinion("OP-0004", "空引用未防", "可代码化", "签到", "第四条"),
                Opinion("OP-0005", "空引用未防", "可代码化", "签到", "第五条"));

            var proposals = PromotionProposalBuilder.Build(book, 1);

            var proposal = Assert.Single(proposals);
            Assert.Equal(3, proposal.Quotations.Count);
            Assert.Equal(new[] { "第一条", "第二条", "第三条" }, proposal.Quotations);
        }

        /// <summary>两组不同条数 → 按 Count 降序排。</summary>
        [Fact]
        public void ProposalsSortedByCountDescending()
        {
            var book = Book(
                Opinion("OP-0001", "甲类", "可代码化", "签到", "a"),
                Opinion("OP-0002", "甲类", "可代码化", "签到", "b"),
                Opinion("OP-0003", "甲类", "可代码化", "签到", "c"),
                Opinion("OP-0004", "甲类", "可代码化", "签到", "d"),
                Opinion("OP-0005", "甲类", "可代码化", "签到", "e"),
                Opinion("OP-0006", "乙类", "可提示词化", "任务", "f"),
                Opinion("OP-0007", "乙类", "可提示词化", "任务", "g"));

            var proposals = PromotionProposalBuilder.Build(book, 1);

            Assert.Equal(2, proposals.Count);
            Assert.Equal("甲类", proposals[0].Category);
            Assert.Equal(5, proposals[0].Count);
            Assert.Equal("乙类", proposals[1].Category);
            Assert.Equal(2, proposals[1].Count);
        }
    }
}
