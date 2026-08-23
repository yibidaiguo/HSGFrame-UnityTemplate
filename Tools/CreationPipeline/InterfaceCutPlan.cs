using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 这一趟拆图照不照清单切。
    /// </summary>
    /// <param name="Spec">选中的那份界面规格；没有或有歧义时为 null。</param>
    /// <param name="Requests">真要出图的元素清单；空表示这趟按看图猜元素走。</param>
    /// <param name="Blocker">要拦住这一趟时的回话；不拦为空串。</param>
    /// <param name="Notes">这一趟怎么判的，一句一条，进执行流水。</param>
    public sealed record InterfaceCutPlan(
        InterfaceSpec Spec,
        IReadOnlyList<UiLayerRequest> Requests,
        string Blocker,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// 定这一趟拆图照哪份界面规格切：会话 → 需求 → 界面规格 → 资产清单，这条链走一遍。
    ///
    /// **照清单切与看图猜的区别不在准不准，在谁说了算**：清单是策划审过的功能契约，
    /// 猜出来的是视觉模型看图看出来的。一屏猜出上百个、跟需求对不上、通用件认不出来——
    /// 三样都是从这一点上错的（子文档 08 §六）。
    /// </summary>
    public static class InterfaceCutPlanner
    {
        /// <summary>
        /// 定这一趟怎么切。
        ///
        /// 三种结果，各有各的态度：
        /// - **一份规格**：给它的「真要出图」清单，照清单切；
        /// - **没有规格**：给空清单，退回看图猜元素那条老路（并在流水里说清是哪条路，不许含糊）；
        /// - **好几份规格**：给一句拦住的话。一条需求动了两屏是常事，而这张图只可能是其中一屏——
        ///   猜错了就是照着商店的清单去切背包，白花一趟钱还得从头再来。
        ///   这时问一句最便宜：视觉模型那一刀还没下去。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录，用来读需求的「专项」。</param>
        /// <param name="conversationIdentifier">会话标识；空表示认不出在做哪条需求。</param>
        /// <param name="hint">人这次说的话；有歧义时拿它认是哪一屏。空串表示没线索。</param>
        public static InterfaceCutPlan Resolve(
            string repositoryRoot, string poolRoot, string conversationIdentifier, string hint)
        {
            var notes = new List<string>();
            var empty = Array.Empty<UiLayerRequest>() as IReadOnlyList<UiLayerRequest>;

            var requirementIdentifier = AssistantServeTurn.ReadConversationRequirement(
                repositoryRoot, conversationIdentifier);
            if (requirementIdentifier.Length == 0)
            {
                notes.Add("这条会话没留需求底，按看图猜元素拆");
                return new InterfaceCutPlan(null, empty, "", notes);
            }

            // **按模块找，不按需求找。** 界面是模块的属性——这条需求可能只改了背包的一个按钮，
            // 而要切的是整屏背包，它多半是更早那条需求出的。按需求找会把「这一屏有规格」
            // 误判成「没规格」，然后退回看图猜，白白丢掉一份已经审过的清单。
            var moduleName = ModulePlanRefresher.ReadEpic(poolRoot, requirementIdentifier);
            IReadOnlyList<InterfaceSpec> found;
            IReadOnlyList<string> skipped;
            if (moduleName.Length > 0)
            {
                found = InterfaceSpec.FindByModule(repositoryRoot, moduleName, out skipped);
            }
            else
            {
                // 没挂专项时退回按需求找：聊出来的临时需求可能还没归模块。
                notes.Add(requirementIdentifier + " 没挂专项，退回按需求找界面规格");
                found = InterfaceSpec.FindByRequirement(repositoryRoot, requirementIdentifier, out skipped);
            }

            foreach (var reason in skipped)
            {
                notes.Add("界面规格读不动：" + reason);
            }

            if (found.Count == 0)
            {
                notes.Add((moduleName.Length > 0 ? moduleName : requirementIdentifier)
                    + " 还没出过功能图，按看图猜元素拆");
                return new InterfaceCutPlan(null, empty, "", notes);
            }

            if (found.Count > 1)
            {
                // 先看人这次有没有把话说清。**问出来的问题要答得上才算问**——
                // 上一轮拦住时报的就是 id 与标题，人照着回一句「背包那屏」或「UI-0002」，
                // 这里认得出来才不至于变成一个死循环。
                var picked = MatchByHint(found, hint);
                if (picked == null)
                {
                    var names = new List<string>();
                    foreach (var candidate in found)
                    {
                        names.Add(candidate.Identifier + "「" + candidate.Title + "」");
                    }

                    var owner = moduleName.Length > 0 ? moduleName : requirementIdentifier;
                    notes.Add(owner + " 名下有 " + found.Count + " 份界面规格，停下来问是哪一屏");
                    return new InterfaceCutPlan(
                        null,
                        empty,
                        owner + " 名下有好几屏：" + string.Join("、", names)
                            + "。\n这张图是哪一屏？说一句我就照那份的清单切——"
                            + "猜错了就是照着这一屏的清单去切另一屏，一趟钱白花还得重来。",
                        notes);
                }

                notes.Add((moduleName.Length > 0 ? moduleName : requirementIdentifier)
                    + " 名下有 " + found.Count + " 份，按人说的认出 " + picked.Identifier);
                found = new[] { picked };
            }

            var spec = found[0];
            var requests = RequestsFor(repositoryRoot, spec, out var elementCount);

            if (requests.Count == 0)
            {
                notes.Add(spec.Identifier + " 这一屏没有要出图的元素");
                return new InterfaceCutPlan(
                    null,
                    empty,
                    spec.Identifier + "「" + spec.Title + "」这一屏的元素全是不用出图的那几类"
                        + "（文案、容器、装饰，或者能直接复用通用件）。这张图不用拆。",
                    notes);
            }

            notes.Add("照 " + spec.Identifier + "「" + spec.Title + "」的清单切："
                + "元素 " + elementCount + " 个，真要出 " + requests.Count + " 个");
            return new InterfaceCutPlan(spec, requests, "", notes);
        }

        /// <summary>
        /// 一份界面规格里**真要出图**的那些元素。
        ///
        /// 挡掉的是：Label（文案由 UI Toolkit 出）、Container（底图是别的元素）、
        /// Decoration（属于底图的一部分）、以及能直接复用 Shared/ 里已有的通用件。
        /// 让模型去图上找它们，每一个都是一次白花的调用——
        /// 一屏从「画面上能框出来的一百多个」收敛到「真正要出的十几二十个」，靠的就是这一步。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="spec">界面规格。</param>
        /// <param name="elementCount">这一屏一共几个元素（含不出图的），报数用。</param>
        public static IReadOnlyList<UiLayerRequest> RequestsFor(
            string repositoryRoot, InterfaceSpec spec, out int elementCount)
        {
            elementCount = 0;
            var requests = new List<UiLayerRequest>();
            if (spec == null)
            {
                return requests;
            }

            var catalog = UiElementTemplateCatalog.Load(repositoryRoot, spec.PanelName);
            var manifest = InterfaceAssetManifest.Build(repositoryRoot, spec, catalog);
            elementCount = manifest.Count;

            var byIdentifier = new Dictionary<string, InterfaceElement>(StringComparer.Ordinal);
            foreach (var element in spec.Elements)
            {
                byIdentifier[element.Identifier] = element;
            }

            foreach (var entry in manifest)
            {
                if (!string.Equals(entry.Action, InterfaceAssetManifest.ActionGenerate, StringComparison.Ordinal))
                {
                    continue;
                }

                byIdentifier.TryGetValue(entry.ElementIdentifier, out var element);
                requests.Add(new UiLayerRequest(
                    entry.ElementIdentifier,
                    entry.ElementType,
                    element?.DisplayName ?? "",
                    entry.Width,
                    entry.Height));
            }

            return requests;
        }

        /// <summary>
        /// 从几份候选里按人说的话认出一份。
        ///
        /// 比的是**人这句话与「界面 id / 标题 / 面板名」的最长公共子串**，取分最高的那份。
        /// 不用「整串包含」是因为人不会照抄标题：拦住时报的是「UI-0002「商店主界面」」，
        /// 人回的是「商店那屏」——要求包含整串等于问了一个答不上的问题。
        ///
        /// **打平就当没认出**：「背包主界面和商店主界面都要」对两份的公共子串一样长，
        /// 这时挑哪一屏都是猜，而猜错要花一整趟钱。再问一次比赌一次便宜。
        /// </summary>
        /// <param name="candidates">候选。</param>
        /// <param name="hint">人说的话。</param>
        public static InterfaceSpec MatchByHint(IReadOnlyList<InterfaceSpec> candidates, string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return null;
            }

            InterfaceSpec matched = null;
            var best = 0;
            var tied = false;

            foreach (var candidate in candidates ?? Array.Empty<InterfaceSpec>())
            {
                var score = Math.Max(
                    LongestCommonRun(hint, candidate.Identifier),
                    Math.Max(LongestCommonRun(hint, candidate.Title), LongestCommonRun(hint, candidate.PanelName)));

                if (score > best)
                {
                    best = score;
                    matched = candidate;
                    tied = false;
                }
                else if (score == best)
                {
                    tied = true;
                }
            }

            // 两个字才算数：一个字的重合（「界」「图」）到处都是，据此挑一屏跟掷骰子没区别。
            return best >= MinimumHintRun && !tied ? matched : null;
        }

        /// <summary>认一屏至少要几个字的重合。</summary>
        private const int MinimumHintRun = 2;

        /// <summary>两串的最长公共子串有多长；任一为空给 0。</summary>
        /// <param name="left">左串。</param>
        /// <param name="right">右串。</param>
        private static int LongestCommonRun(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            var a = left.Trim();
            var b = right.Trim();
            var best = 0;

            // 串都是一句话与一个标题的量级，朴素两重循环够用，不值得为它引一张 DP 表。
            for (var start = 0; start < a.Length; start++)
            {
                var run = 0;
                for (var length = 1; start + length <= a.Length; length++)
                {
                    if (b.IndexOf(a.Substring(start, length), StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        break;
                    }

                    run = length;
                }

                if (run > best)
                {
                    best = run;
                }
            }

            return best;
        }
    }
}
