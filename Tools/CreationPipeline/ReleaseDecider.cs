using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次放行判定的结果：是否自动放行、风险级、涉及范围与全部未满足的判据理由。
    /// </summary>
    public sealed class ReleaseDecision
    {
        /// <summary>
        /// 构造一次放行判定的结果。
        /// </summary>
        /// <param name="isAutomatic">是否自动放行。</param>
        /// <param name="grade">风险级：低 / 常规 / 高。</param>
        /// <param name="scopes">本次改动涉及的范围。</param>
        /// <param name="reasons">每条不满足的判据各一句中文；全满足时为空列表。</param>
        public ReleaseDecision(bool isAutomatic, string grade, IReadOnlyList<string> scopes, IReadOnlyList<string> reasons)
        {
            IsAutomatic = isAutomatic;
            Grade = grade ?? "";
            Scopes = scopes ?? Array.Empty<string>();
            Reasons = reasons ?? Array.Empty<string>();
        }

        /// <summary>是否自动放行。</summary>
        public bool IsAutomatic { get; }

        /// <summary>风险级：低 / 常规 / 高。</summary>
        public string Grade { get; }

        /// <summary>本次改动涉及的范围。</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>每条不满足的判据各一句中文；全满足时为空列表。</summary>
        public IReadOnlyList<string> Reasons { get; }
    }

    /// <summary>
    /// 自动放行判定器：四条判据全满足才自动放行，缺一条就记一句 Reason 且不放行。
    /// 基线底线（高危范围永不自动放行）先判且不可被策略数据推翻。
    /// </summary>
    public static class ReleaseDecider
    {
        /// <summary>
        /// 判定是否自动放行。四条判据都要判完再返回，不撞上第一条不满足就提前返回。
        /// </summary>
        /// <param name="catalog">放行策略目录。</param>
        /// <param name="risk">风险分级结果。</param>
        /// <param name="allGatesGreen">门禁是否全绿。</param>
        /// <param name="blockingFindingCount">阻断级发现数。</param>
        /// <param name="suggestionFindingCount">建议级发现数。</param>
        public static ReleaseDecision Decide(
            ReleasePolicyCatalog catalog,
            RiskGradeResult risk,
            bool allGatesGreen,
            int blockingFindingCount,
            int suggestionFindingCount)
        {
            var reasons = new List<string>();
            var isAutomatic = true;

            var highRisk = catalog?.HighRiskScopes ?? Array.Empty<string>();
            var riskScopes = risk?.Scopes ?? Array.Empty<string>();

            // 判据一：基线底线。高危范围永不自动放行，这条不可被策略数据推翻。
            var baselineHitScopes = riskScopes.Where(scope => highRisk.Contains(scope, StringComparer.Ordinal)).ToList();
            if (baselineHitScopes.Count > 0)
            {
                reasons.Add($"基线底线：本次改动涉及高危范围「{string.Join("、", baselineHitScopes)}」，永不自动放行");
                isAutomatic = false;
            }

            // 判据二：命中策略。对每一个范围都查，全部返回「自动放行」才算命中。
            var manualScopes = new List<string>();
            if (catalog != null && risk != null)
            {
                foreach (var scope in riskScopes)
                {
                    if (catalog.Decide(risk.Grade, scope) != "自动放行")
                    {
                        manualScopes.Add(scope);
                    }
                }
            }

            if (manualScopes.Count > 0)
            {
                reasons.Add($"放行策略未命中：{risk?.Grade}.{string.Join("、", manualScopes)} 仍需人审");
                isAutomatic = false;
            }

            // 判据三：门禁全绿。
            if (!allGatesGreen)
            {
                reasons.Add("门禁未全绿，不能自动放行");
                isAutomatic = false;
            }

            // 判据四：预审发现数达标。
            var threshold = catalog?.SuggestionThreshold ?? 0;
            if (blockingFindingCount > 0 || suggestionFindingCount > threshold)
            {
                reasons.Add($"预审发现未达标：阻断 {blockingFindingCount} 条、建议 {suggestionFindingCount} 条（阈值 {threshold}）");
                isAutomatic = false;
            }

            return new ReleaseDecision(isAutomatic, risk?.Grade ?? "", riskScopes, reasons);
        }
    }
}
