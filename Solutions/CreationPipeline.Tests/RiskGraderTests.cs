using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>风险分级判定顺序的测试：高危范围、行数、阻断数、低风险判据与空改动。</summary>
    public class RiskGraderTests
    {
        private static readonly string[] HighRiskScopes = { "框架", "引擎", "检查器", "构建", "Specifications" };

        /// <summary>改框架包：高危范围命中，风险级「高」，范围含「框架」。</summary>
        [Fact]
        public void PackageChangeIsHighRiskFrameworkScope()
        {
            var result = RiskGrader.Grade(
                new[] { "Packages/com.hsgframe.core/Runtime/A.cs" },
                10,
                0,
                0,
                HighRiskScopes);

            Assert.Equal("高", result.Grade);
            Assert.Contains("框架", result.Scopes);
            Assert.Contains("框架", result.Reason);
        }

        /// <summary>改 500 行业务代码：行数触发，风险级「高」。</summary>
        [Fact]
        public void LargeChangeIsHighRiskByLineCount()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                500,
                0,
                0,
                HighRiskScopes);

            Assert.Equal("高", result.Grade);
            Assert.Contains("500", result.Reason);
        }

        /// <summary>阻断发现 1 条、其余全绿的小业务改动：风险级「高」。</summary>
        [Fact]
        public void BlockingFindingMakesHighRisk()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                1,
                0,
                HighRiskScopes);

            Assert.Equal("高", result.Grade);
            Assert.Contains("阻断", result.Reason);
        }

        /// <summary>改签到模块 30 行、零发现：低风险。</summary>
        [Fact]
        public void SmallBusinessChangeZeroFindingsIsLow()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                0,
                0,
                HighRiskScopes);

            Assert.Equal("低", result.Grade);
            Assert.Equal(new[] { "业务" }, result.Scopes);
        }

        /// <summary>同上但建议发现 1 条：常规。</summary>
        [Fact]
        public void SmallBusinessChangeWithSuggestionIsRegular()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                0,
                1,
                HighRiskScopes);

            Assert.Equal("常规", result.Grade);
        }

        /// <summary>空改动列表：低风险，理由说「零改动」。</summary>
        [Fact]
        public void EmptyPathsIsLow()
        {
            var result = RiskGrader.Grade(Array.Empty<string>(), 0, 0, 0, HighRiskScopes);

            Assert.Equal("低", result.Grade);
            Assert.Contains("零改动", result.Reason);
        }

        /// <summary>highRiskScopes 传 null 用缺省五范围兜底：改引擎代码仍判高。</summary>
        [Fact]
        public void NullHighRiskScopesFallsBackToDefaults()
        {
            var result = RiskGrader.Grade(
                new[] { "Tools/CreationPipeline/A.cs" },
                10,
                0,
                0,
                null);

            Assert.Equal("高", result.Grade);
            Assert.Contains("引擎", result.Scopes);
        }

        /// <summary>范围只有业务或其它、但行数 81：判常规，不因行数贴近阈值误判低。</summary>
        [Fact]
        public void BusinessChangeOverLineBoundaryIsRegular()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                81,
                0,
                0,
                HighRiskScopes);

            Assert.Equal("常规", result.Grade);
        }

        /// <summary>范围去重且序数序排序：同一范围多条路径只出现一次。</summary>
        [Fact]
        public void ScopesAreDistinctAndOrdinalSorted()
        {
            var result = RiskGrader.Grade(
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs", "UnityProject/Assets/Game/Scripts/Modules/签到/B.cs" },
                20,
                0,
                0,
                HighRiskScopes);

            Assert.Equal(new[] { "业务" }, result.Scopes);
            Assert.Equal("低", result.Grade);
        }
    }
}
