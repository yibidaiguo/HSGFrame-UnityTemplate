using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>卡片路由命中的步骤：四步路由里最终落位。</summary>
    public enum RoutingStep
    {
        /// <summary>第②步·专项内已认领：需求所属专项的该职责有认领人，只推认领人。</summary>
        ClaimedInEpic,

        /// <summary>第③步·按默认职责全员先到先得：无专项认领时给出该职责全部成员的候选集。</summary>
        DutyPool,

        /// <summary>伪职责「提出人」命中提出人本人。</summary>
        Submitter,

        /// <summary>第④步·管理员兜底：职责无人时改推管理员全员。</summary>
        AdministratorFallback,

        /// <summary>无人可推：职责与管理员都没有成员。</summary>
        NoRecipient
    }

    /// <summary>一次卡片路由的结果：卡片类型、实际查人职责、收件人 open_id 列表、命中步骤与一句中文理由。</summary>
    public sealed class CardRoutingResult
    {
        /// <summary>
        /// 构造一次路由结果。
        /// </summary>
        /// <param name="cardType">卡片类型。</param>
        /// <param name="duty">实际用于查人的职责；伪职责「提出人」命中本人时写「提出人」，兜底时写「管理员」。</param>
        /// <param name="recipients">收件人 open_id 列表。</param>
        /// <param name="step">命中的路由步骤。</param>
        /// <param name="reason">一句中文理由，说清为什么落到这些人头上。</param>
        public CardRoutingResult(string cardType, string duty, IReadOnlyList<string> recipients, RoutingStep step, string reason)
        {
            CardType = cardType;
            Duty = duty;
            Recipients = recipients ?? Array.Empty<string>();
            Step = step;
            Reason = reason ?? "";
        }

        /// <summary>卡片类型。</summary>
        public string CardType { get; }

        /// <summary>实际用于查人的职责；伪职责「提出人」命中本人时写「提出人」，兜底时写「管理员」。</summary>
        public string Duty { get; }

        /// <summary>收件人 open_id 列表。</summary>
        public IReadOnlyList<string> Recipients { get; }

        /// <summary>命中的路由步骤。</summary>
        public RoutingStep Step { get; }

        /// <summary>一句中文理由，说清为什么落到这些人头上。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 卡片路由四步：① 卡片类型→职责；② 专项认领优先；③ 默认职责全员先到先得；④ 管理员兜底。
    /// 「提出人」是伪职责，在查人前特判为提出人本人，找不到时退化成「策划」继续走第②步。
    /// </summary>
    public static class CardRouter
    {
        /// <summary>伪职责「提出人」的写法。</summary>
        private const string SubmitterDuty = "提出人";

        /// <summary>管理员兜底职责的写法。</summary>
        private const string AdministratorDuty = "管理员";

        /// <summary>
        /// 按四步顺序路由一张卡片到收件人 open_id 列表。
        /// </summary>
        /// <param name="cardType">卡片类型。</param>
        /// <param name="epicIdentifier">需求所属专项 id，可空；传空串表示无专项。</param>
        /// <param name="submitterName">需求提交人姓名，伪职责「提出人」特判用；可空。</param>
        /// <param name="routeTable">卡片类型→职责的路由表。</param>
        /// <param name="members">成员目录。</param>
        /// <param name="claims">专项认领表。</param>
        public static CardRoutingResult Route(
            string cardType,
            string epicIdentifier,
            string submitterName,
            CardRouteTable routeTable,
            MemberDirectory members,
            EpicClaimBook claims)
        {
            var duty = routeTable.DutyOf(cardType);

            // 第①步：卡片类型未配置 → 直接落第④步管理员兜底。
            if (string.IsNullOrEmpty(duty))
            {
                return FallbackToAdministrators(
                    cardType,
                    AdministratorDuty,
                    members,
                    $"卡片类型「{cardType}」未在路由表里配置，落管理员兜底",
                    unconfigured: true);
            }

            string prefix;
            if (string.Equals(duty, SubmitterDuty, StringComparison.Ordinal))
            {
                // 伪职责特判：提出人在成员表 → 收件人就是本人；找不到 → 退化「策划」继续走第②步。
                if (!string.IsNullOrEmpty(submitterName))
                {
                    var submitter = members.FindByName(submitterName);
                    if (submitter != null)
                    {
                        return new CardRoutingResult(
                            cardType,
                            SubmitterDuty,
                            new[] { submitter.OpenIdentifier },
                            RoutingStep.Submitter,
                            $"卡片「{cardType}」对应伪职责「提出人」，命中提出人本人「{submitterName}」");
                    }
                }

                duty = "策划";
                prefix = $"卡片「{cardType}」对应伪职责「提出人」，提出人不在成员表，落策划";
            }
            else
            {
                prefix = $"卡片「{cardType}」对应职责「{duty}」";
            }

            // 第②步：专项存在且该职责有认领人 → 只推认领人。
            if (!string.IsNullOrEmpty(epicIdentifier))
            {
                var claimers = claims.ClaimersOf(epicIdentifier, duty);
                if (claimers.Count > 0)
                {
                    return new CardRoutingResult(
                        cardType,
                        duty,
                        claimers,
                        RoutingStep.ClaimedInEpic,
                        $"{prefix}，第②步命中专项「{epicIdentifier}」认领，只推认领人");
                }
            }

            // 第③步：无专项认领 → 按默认职责全员先到先得。
            var byDuty = members.ByDuty(duty);
            if (byDuty.Count > 0)
            {
                return new CardRoutingResult(
                    cardType,
                    duty,
                    OpenIdentifiersOf(byDuty),
                    RoutingStep.DutyPool,
                    $"{prefix}，第③步无专项认领，按默认职责全员先到先得");
            }

            // 第④步：职责无人 → 管理员兜底。
            return FallbackToAdministrators(
                cardType,
                duty,
                members,
                $"{prefix}，第④步职责「{duty}」无人，落管理员兜底",
                unconfigured: false);
        }

        /// <summary>第④步兜底：查管理员，无人可推时返回 NoRecipient。</summary>
        private static CardRoutingResult FallbackToAdministrators(
            string cardType,
            string duty,
            MemberDirectory members,
            string reasonPrefix,
            bool unconfigured)
        {
            var administrators = members.ByDuty(AdministratorDuty);
            if (administrators.Count > 0)
            {
                return new CardRoutingResult(
                    cardType,
                    AdministratorDuty,
                    OpenIdentifiersOf(administrators),
                    RoutingStep.AdministratorFallback,
                    reasonPrefix);
            }

            var reason = unconfigured
                ? $"{reasonPrefix}，但管理员也没有成员，成员表可能没配"
                : $"{reasonPrefix}；职责「{duty}」与「管理员」都没有成员，成员表可能没配";
            return new CardRoutingResult(cardType, duty, Array.Empty<string>(), RoutingStep.NoRecipient, reason);
        }

        /// <summary>把成员列表转成 open_id 列表。</summary>
        private static IReadOnlyList<string> OpenIdentifiersOf(IReadOnlyList<PoolMember> members)
        {
            var identifiers = new List<string>(members.Count);
            foreach (var member in members)
            {
                identifiers.Add(member.OpenIdentifier);
            }

            return identifiers;
        }
    }
}
