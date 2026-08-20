using System;
using System.Collections.Generic;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>审查包五件套组装的测试：标题顺序、高危标记、缺省文本与摘要里逐条列 Reason。</summary>
    public class ReviewPackageBuilderTests
    {
        /// <summary>五个二级标题按顺序出现。</summary>
        [Fact]
        public void FiveSectionHeadersAppearInOrder()
        {
            var markdown = BuildSample(automatic: true, reasons: Array.Empty<string>());

            var headers = new[]
            {
                "## 一、变更地图",
                "## 二、方案对照",
                "## 三、预审报告",
                "## 四、验收报告",
                "## 五、提交清单"
            };

            var previousIndex = -1;
            foreach (var header in headers)
            {
                var index = markdown.IndexOf(header, StringComparison.Ordinal);
                Assert.True(index > previousIndex, $"标题「{header}」应在靠后的位置出现");
                previousIndex = index;
            }
        }

        /// <summary>高危范围的组标题带「（高危）」。</summary>
        [Fact]
        public void HighRiskScopeGroupHeaderHasMark()
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "Packages/com.hsgframe.core/Runtime/A.cs", "Tools/Gates/gate.ps1" },
                12,
                "无偏差",
                "预审通过",
                "验收通过",
                new List<string> { "feat: 改动 A" },
                null);
            var risk = new RiskGradeResult("高", new[] { "框架", "检查器" }, "涉及高危范围：框架");
            var decision = new ReleaseDecision(false, "高", risk.Scopes, new[] { "基线底线：本次改动涉及高危范围「框架」，永不自动放行" });

            var markdown = ReviewPackageBuilder.Build(input, risk, decision);

            Assert.Contains("框架（高危）：", markdown);
            Assert.Contains("检查器（高危）：", markdown);
        }

        /// <summary>四段文本为空时出现「（未提供）」，提交清单为空时也写「（未提供）」。</summary>
        [Fact]
        public void MissingTextsShowPlaceholder()
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                "",
                "",
                "",
                new List<string>(),
                null);
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");
            var decision = new ReleaseDecision(true, "低", risk.Scopes, Array.Empty<string>());

            var markdown = ReviewPackageBuilder.Build(input, risk, decision);

            Assert.Contains("（未提供）", markdown);
            Assert.Equal(4, CountOccurrences(markdown, "（未提供）"));
        }

        /// <summary>不放行时摘要里逐条列出了 Reason。</summary>
        [Fact]
        public void ManualReviewListsReasonsInSummary()
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                "无偏差",
                "预审通过",
                "验收通过",
                new List<string> { "feat: 改动 A" },
                null);
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");
            var reasons = new[]
            {
                "门禁未全绿，不能自动放行",
                "预审发现未达标：阻断 0 条、建议 4 条（阈值 3）"
            };
            var decision = new ReleaseDecision(false, "低", risk.Scopes, reasons);

            var markdown = ReviewPackageBuilder.Build(input, risk, decision);

            Assert.Contains("放行结论：人审", markdown);
            Assert.Contains(reasons[0], markdown);
            Assert.Contains(reasons[1], markdown);
        }

        /// <summary>自动放行时摘要写「自动放行」，且不列任何 Reason。</summary>
        [Fact]
        public void AutomaticReleaseShowsConclusion()
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                "无偏差",
                "预审通过",
                "验收通过",
                new List<string> { "feat: 改动 A" },
                null);
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");
            var decision = new ReleaseDecision(true, "低", risk.Scopes, Array.Empty<string>());

            var markdown = ReviewPackageBuilder.Build(input, risk, decision);

            Assert.Contains("放行结论：自动放行", markdown);
        }

        /// <summary>一级标题带需求 id。</summary>
        [Fact]
        public void TitleCarriesRequirementIdentifier()
        {
            var markdown = BuildSample(automatic: true, reasons: Array.Empty<string>());

            Assert.Contains("# 审查包：REQ-0001", markdown);
        }

        private static string BuildSample(bool automatic, IReadOnlyList<string> reasons)
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                "无偏差",
                "预审通过",
                "验收通过",
                new List<string> { "feat: 改动 A" },
                null);
            var risk = new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");
            var decision = new ReleaseDecision(automatic, "低", risk.Scopes, reasons);
            return ReviewPackageBuilder.Build(input, risk, decision);
        }

        private static int CountOccurrences(string text, string fragment)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += fragment.Length;
            }

            return count;
        }
    }
}
