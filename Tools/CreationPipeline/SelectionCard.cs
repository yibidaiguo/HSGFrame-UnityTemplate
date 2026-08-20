using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一张选片卡片：需求与资产定位、轮次、合格变体清单、弃置数与给人按的按钮。
    /// 卡片本身只描述「选什么、有哪些可选项」，不负责扫描与路由。
    /// </summary>
    public sealed class SelectionCard
    {
        /// <summary>
        /// 构造一张选片卡片；按钮按「合格变体序号 → 换一批 → 我自己来」的固定顺序生成，
        /// 提示语按轮次规则生成（第 3 轮起给接管提示）。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="round">选片轮次，从 1 起。</param>
        /// <param name="qualifiedVariants">合格变体的文件名列表，序数序。</param>
        /// <param name="rejectedCount">弃置目录里的文件数。</param>
        public SelectionCard(
            string requirementIdentifier,
            string assetIdentifier,
            int round,
            IReadOnlyList<string> qualifiedVariants,
            int rejectedCount)
        {
            RequirementIdentifier = requirementIdentifier ?? "";
            AssetIdentifier = assetIdentifier ?? "";
            Round = round;
            QualifiedVariants = qualifiedVariants ?? Array.Empty<string>();
            RejectedCount = rejectedCount;
            Buttons = BuildButtons(QualifiedVariants);
            Hint = Round >= 3 ? "已 3 轮，考虑接管或调锚点" : "";
        }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        public string AssetIdentifier { get; }

        /// <summary>选片轮次，从 1 起。</summary>
        public int Round { get; }

        /// <summary>合格变体的文件名列表，序数序。</summary>
        public IReadOnlyList<string> QualifiedVariants { get; }

        /// <summary>弃置目录里的文件数。</summary>
        public int RejectedCount { get; }

        /// <summary>给人按的按钮：先是「1」…「N」（N = 合格变体数），再是「换一批」，最后是「我自己来」。</summary>
        public IReadOnlyList<string> Buttons { get; }

        /// <summary>提示语；第 3 轮起给「已 3 轮，考虑接管或调锚点」，之前为空串。</summary>
        public string Hint { get; }

        /// <summary>按钉死的顺序生成按钮：合格变体序号 → 换一批 → 我自己来。</summary>
        private static IReadOnlyList<string> BuildButtons(IReadOnlyList<string> qualifiedVariants)
        {
            var buttons = new List<string>(qualifiedVariants.Count + 2);
            for (var index = 1; index <= qualifiedVariants.Count; index++)
            {
                buttons.Add(index.ToString());
            }

            buttons.Add("换一批");
            buttons.Add("我自己来");
            return buttons;
        }
    }
}
