using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次选片出站规划的结果：组好的信封（可能为 null）、装配出的卡片（可能为 null）与发现的全部问题。</summary>
    public sealed class SelectionOutboundResult
    {
        /// <summary>
        /// 构造一次出站规划结果。
        /// </summary>
        /// <param name="envelope">组好的出站意图信封；卡片装配失败时为 null。</param>
        /// <param name="card">装配出的选片卡片，失败时为 null。</param>
        /// <param name="findings">规划过程中发现的全部问题。</param>
        public SelectionOutboundResult(OutboundEnvelope envelope, SelectionCard card, IReadOnlyList<PoolFinding> findings)
        {
            Envelope = envelope;
            Card = card;
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>组好的出站意图信封；卡片装配失败时为 null。</summary>
        public OutboundEnvelope Envelope { get; }

        /// <summary>装配出的选片卡片，失败时为 null。</summary>
        public SelectionCard Card { get; }

        /// <summary>规划过程中发现的全部问题。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 选片出站规划：先装配选片卡片，再按「选片」卡片类型路由收件人，最后包成出站意图信封。
    /// 选片的单位是资产、不回写任何下游需求字段，所以走独立规划器，不并进需求级的出站规划。
    /// </summary>
    public static class SelectionOutboundPlanner
    {
        /// <summary>选片卡片的类型名，与默认路由表里「选片 → 美术」的键一致。</summary>
        private const string SelectionCardType = "选片";

        /// <summary>
        /// 规划一次选片出站。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录，路由表与成员表从这里加载。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="epicIdentifier">需求所属专项 id，可空；空串按「无专项」走路由第③步。</param>
        /// <param name="round">选片轮次，从 1 起。</param>
        /// <param name="moment">事件发生时刻。</param>
        public static SelectionOutboundResult Plan(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            string assetIdentifier,
            string epicIdentifier,
            int round,
            DateTimeOffset moment)
        {
            var buildResult = SelectionCardBuilder.Build(repositoryRoot, requirementIdentifier, assetIdentifier, round);
            if (buildResult.Card == null)
            {
                return new SelectionOutboundResult(null, null, buildResult.Findings);
            }

            var routeTable = CardRouteTable.Load(poolRoot);
            var members = MemberDirectory.Load(poolRoot);
            var claims = EpicClaimBook.Load(poolRoot);
            var routing = CardRouter.Route(
                SelectionCardType,
                epicIdentifier ?? "",
                "",
                routeTable,
                members,
                claims);

            var summary = $"资产 {assetIdentifier} 第 {buildResult.Card.Round} 轮选片："
                + $"合格变体 {buildResult.Card.QualifiedVariants.Count} 张、弃置 {buildResult.Card.RejectedCount} 张";
            var envelope = new OutboundEnvelope(
                requirementIdentifier,
                SelectionCardType,
                moment,
                new Dictionary<string, string>(),
                routing,
                summary);

            return new SelectionOutboundResult(envelope, buildResult.Card, buildResult.Findings);
        }
    }
}
