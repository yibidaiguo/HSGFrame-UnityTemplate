using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>自动放行判定四条判据的测试：全满足才放行、缺一即记一条 Reason、基线底线不可被数据推翻。</summary>
    public class ReleaseDeciderTests
    {
        /// <summary>低风险业务、全门禁绿、零发现：自动放行，Reasons 空。</summary>
        [Fact]
        public void LowRiskBusinessAllGreenIsAutomatic()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: true, blockingFindingCount: 0, suggestionFindingCount: 0);

            Assert.True(decision.IsAutomatic);
            Assert.Empty(decision.Reasons);
        }

        /// <summary>同上但门禁非全绿：不放行，Reason 只有一条且点名门禁。</summary>
        [Fact]
        public void GateRedBlocksAutomaticRelease()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: false, blockingFindingCount: 0, suggestionFindingCount: 0);

            Assert.False(decision.IsAutomatic);
            var reason = Assert.Single(decision.Reasons);
            Assert.Contains("门禁", reason);
        }

        /// <summary>范围含引擎且策略数据被改成「高.引擎=自动放行」：仍不放行，Reason 说清是基线底线拦的。</summary>
        [Fact]
        public void BaselineBottomLineCannotBeOverriddenByData()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("高", new[] { "引擎" }, "涉及高危范围：引擎");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: true, blockingFindingCount: 0, suggestionFindingCount: 0);

            Assert.False(decision.IsAutomatic);
            var reason = Assert.Single(decision.Reasons);
            Assert.Contains("基线底线", reason);
        }

        /// <summary>建议数超过阈值：不放行。</summary>
        [Fact]
        public void SuggestionCountOverThresholdBlocks()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: true, blockingFindingCount: 0, suggestionFindingCount: 4);

            Assert.False(decision.IsAutomatic);
            Assert.Contains(decision.Reasons, reason => reason.Contains("建议"));
        }

        /// <summary>同时踩三条判据：Reasons 有三条（证明判完全部才返回，不撞上第一条就 return）。</summary>
        [Fact]
        public void ThreeViolationsReportAllReasons()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("高", new[] { "引擎" }, "涉及高危范围：引擎");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: false, blockingFindingCount: 0, suggestionFindingCount: 4);

            Assert.False(decision.IsAutomatic);
            Assert.Equal(3, decision.Reasons.Count);
            Assert.Contains(decision.Reasons, reason => reason.Contains("基线底线"));
            Assert.Contains(decision.Reasons, reason => reason.Contains("门禁"));
            Assert.Contains(decision.Reasons, reason => reason.Contains("建议"));
        }

        /// <summary>策略未命中（范围在策略表里是「人审」）：不放行，Reason 点名是哪个范围。</summary>
        [Fact]
        public void PolicyMissNamesTheScope()
        {
            var catalog = CatalogWithBaselinePolicies();
            var risk = new RiskGradeResult("低", new[] { "其他" }, "小改动且只涉业务或其它范围、零发现");

            var decision = ReleaseDecider.Decide(catalog, risk, allGatesGreen: true, blockingFindingCount: 0, suggestionFindingCount: 0);

            Assert.False(decision.IsAutomatic);
            var reason = Assert.Single(decision.Reasons);
            Assert.Contains("其他", reason);
        }

        private static ReleasePolicyCatalog CatalogWithBaselinePolicies()
        {
            return new ReleasePolicyCatalog(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["低.业务"] = "自动放行",
                    ["低.其他"] = "人审",
                    ["常规.业务"] = "人审",
                    ["高.引擎"] = "自动放行"
                },
                new List<string> { "低.业务" },
                suggestionThreshold: 3,
                new List<string> { "框架", "引擎" },
                new List<PoolFinding>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }
}
