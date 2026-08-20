using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条晋升提案：同类打回意见攒够阈值后，建议沉淀到检查器或预审规则。</summary>
    public sealed class PromotionProposal
    {
        /// <summary>
        /// 构造一条晋升提案。
        /// </summary>
        /// <param name="category">问题类别。</param>
        /// <param name="count">同类条数。</param>
        /// <param name="rulability">该类里出现最多的可规则化性（平票取更严）。</param>
        /// <param name="targetChannel">晋升去向：检查器 / 预审规则 / 无。</param>
        /// <param name="moduleNames">涉及模块，序数序。</param>
        /// <param name="quotations">原文引用，按 id 序数序取前三条。</param>
        public PromotionProposal(
            string category,
            int count,
            string rulability,
            string targetChannel,
            IReadOnlyList<string> moduleNames,
            IReadOnlyList<string> quotations)
        {
            Category = category ?? "";
            Count = count;
            Rulability = rulability ?? "";
            TargetChannel = targetChannel ?? "";
            ModuleNames = moduleNames ?? Array.Empty<string>();
            Quotations = quotations ?? Array.Empty<string>();
        }

        /// <summary>问题类别。</summary>
        public string Category { get; }

        /// <summary>同类条数。</summary>
        public int Count { get; }

        /// <summary>该类里出现最多的可规则化性（平票取更严）。</summary>
        public string Rulability { get; }

        /// <summary>晋升去向：检查器 / 预审规则 / 无。</summary>
        public string TargetChannel { get; }

        /// <summary>涉及模块，序数序。</summary>
        public IReadOnlyList<string> ModuleNames { get; }

        /// <summary>原文引用，按 id 序数序取前三条。</summary>
        public IReadOnlyList<string> Quotations { get; }
    }

    /// <summary>
    /// 晋升提案构建器：把意见库按问题类别分组，组内条数达到阈值才出提案。
    /// 提案是待办不是违规——空库返回空列表是正常状态。
    /// </summary>
    public static class PromotionProposalBuilder
    {
        /// <summary>可规则化性从最严到最松的次序，平票时取更严的那个。</summary>
        private static readonly string[] RulabilitySeverityOrder =
        {
            "可代码化", "可提示词化", "不可规则化"
        };

        /// <summary>
        /// 从意见库构建晋升提案：按问题类别分组，组内条数大于等于阈值才出提案；
        /// 结果按条数降序、同数按问题类别序数序；阈值小于 1 当作 1；空库返回空列表。
        /// </summary>
        /// <param name="book">意见库。</param>
        /// <param name="threshold">同类条数阈值，小于 1 当作 1。</param>
        public static IReadOnlyList<PromotionProposal> Build(ReviewOpinionBook book, int threshold)
        {
            if (book == null || book.Opinions.Count == 0)
            {
                return Array.Empty<PromotionProposal>();
            }

            var effectiveThreshold = threshold < 1 ? 1 : threshold;
            var proposals = new List<PromotionProposal>();
            foreach (var group in book.Opinions.GroupBy(opinion => opinion.Category))
            {
                var opinions = group.ToList();
                if (opinions.Count < effectiveThreshold)
                {
                    continue;
                }

                var rulability = MajorityRulability(opinions);
                var moduleNames = opinions
                    .Select(opinion => opinion.ModuleName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                var quotations = opinions
                    .OrderBy(opinion => opinion.Identifier, StringComparer.Ordinal)
                    .Take(3)
                    .Select(opinion => opinion.Quotation)
                    .ToList();

                proposals.Add(new PromotionProposal(
                    group.Key,
                    opinions.Count,
                    rulability,
                    MapChannel(rulability),
                    moduleNames,
                    quotations));
            }

            proposals.Sort((left, right) =>
            {
                var byCount = right.Count.CompareTo(left.Count);
                return byCount != 0 ? byCount : string.CompareOrdinal(left.Category, right.Category);
            });
            return proposals;
        }

        /// <summary>取组内出现最多的可规则化性；平票时取更严的那个（可代码化 &gt; 可提示词化 &gt; 不可规则化）。</summary>
        private static string MajorityRulability(IReadOnlyList<ReviewOpinion> opinions)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var opinion in opinions)
            {
                if (counts.TryGetValue(opinion.Rulability, out var count))
                {
                    counts[opinion.Rulability] = count + 1;
                }
                else
                {
                    counts[opinion.Rulability] = 1;
                }
            }

            var best = RulabilitySeverityOrder[RulabilitySeverityOrder.Length - 1];
            var bestCount = -1;
            foreach (var candidate in RulabilitySeverityOrder)
            {
                if (!counts.TryGetValue(candidate, out var count))
                {
                    continue;
                }

                if (count > bestCount)
                {
                    best = candidate;
                    bestCount = count;
                }
            }

            return best;
        }

        /// <summary>可规则化性映射到晋升去向：可代码化 → 检查器；可提示词化 → 预审规则；其余 → 无。</summary>
        private static string MapChannel(string rulability)
        {
            if (string.Equals(rulability, "可代码化", StringComparison.Ordinal))
            {
                return "检查器";
            }

            if (string.Equals(rulability, "可提示词化", StringComparison.Ordinal))
            {
                return "预审规则";
            }

            return "无";
        }
    }
}
