using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次风险分级的结果：风险级、本次改动涉及的范围（序数序）与一句中文理由。
    /// </summary>
    public sealed class RiskGradeResult
    {
        /// <summary>
        /// 构造一次风险分级的结果。
        /// </summary>
        /// <param name="grade">风险级：低 / 常规 / 高。</param>
        /// <param name="scopes">本次改动涉及的范围，序数序。</param>
        /// <param name="reason">一句中文，说清为什么是这一级。</param>
        public RiskGradeResult(string grade, IReadOnlyList<string> scopes, string reason)
        {
            Grade = grade ?? "";
            Scopes = scopes ?? Array.Empty<string>();
            Reason = reason ?? "";
        }

        /// <summary>风险级：低 / 常规 / 高。</summary>
        public string Grade { get; }

        /// <summary>本次改动涉及的范围，序数序。</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>一句中文，说清为什么是这一级。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 风险分级器：按改动范围、改动行数与预审发现数给风险级。判定顺序钉死，第一个命中的赢。
    /// </summary>
    public static class RiskGrader
    {
        /// <summary>高危范围缺省值，与放行策略基线「高危范围」一致；参数传 null 时用它兜底。</summary>
        private static readonly string[] DefaultHighRiskScopes = { "框架", "引擎", "检查器", "构建", "Specifications" };

        /// <summary>
        /// 按改动范围与规模给风险级。
        /// </summary>
        /// <param name="changedPaths">本次改动的仓库相对路径列表。</param>
        /// <param name="changedLineCount">改动行数。</param>
        /// <param name="blockingFindingCount">阻断级发现数。</param>
        /// <param name="suggestionFindingCount">建议级发现数。</param>
        /// <param name="highRiskScopes">高危范围清单；传 null 用「框架 / 引擎 / 检查器 / 构建 / 规范」兜底。</param>
        public static RiskGradeResult Grade(
            IReadOnlyList<string> changedPaths,
            int changedLineCount,
            int blockingFindingCount,
            int suggestionFindingCount,
            IReadOnlyList<string> highRiskScopes)
        {
            if (changedPaths == null || changedPaths.Count == 0)
            {
                return new RiskGradeResult("低", Array.Empty<string>(), "零改动");
            }

            var scopes = ChangeScopeClassifier.ClassifyAll(changedPaths);
            var highRisk = highRiskScopes ?? DefaultHighRiskScopes;

            var hitScopes = scopes.Where(scope => highRisk.Contains(scope, StringComparer.Ordinal)).ToList();
            if (hitScopes.Count > 0)
            {
                return new RiskGradeResult("高", scopes, $"涉及高危范围：{string.Join("、", hitScopes)}");
            }

            if (changedLineCount > 400)
            {
                return new RiskGradeResult("高", scopes, $"改动行数 {changedLineCount} 超过 400 行");
            }

            if (blockingFindingCount > 0)
            {
                return new RiskGradeResult("高", scopes, $"阻断级发现 {blockingFindingCount} 条");
            }

            var onlyBusinessOrOther = scopes.All(scope => scope == "业务" || scope == "其他");
            if (onlyBusinessOrOther && changedLineCount <= 80 && blockingFindingCount == 0 && suggestionFindingCount == 0)
            {
                return new RiskGradeResult("低", scopes, "小改动且只涉业务或其它范围、零发现");
            }

            return new RiskGradeResult("常规", scopes, "未命中低风险判据");
        }
    }
}
