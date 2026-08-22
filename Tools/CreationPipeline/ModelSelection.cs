using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 「模型」这一格的哨兵值 <c>自动</c> 的唯一落点：把「配置里写的」与「本次调用指定的」
    /// 解析成**这一次真正要发给下游的模型名**，并给出一句「凭什么是它」的账。
    ///
    /// 三条纪律：
    /// 1. **清单来自上次能力探测，不来自代码。**本文件里没有、也永远不许有任何一个具体模型名——
    ///    写一个进来就等于替下游决定了「你应该有哪个模型」，而下游随时会多出一个我们没探到的。
    /// 2. **任何下游 driver 的名字都不许出现在本文件里**（决策 17）：driver 名一律走参数，
    ///    「哪个字段是模型字段」由 driver 自述的「选项来源」声明说了算。
    /// 3. **挑不出来时的正确行为是「一个 model 参数都不发」+ 说清为什么**，不是回落到某个模型。
    ///    这与出图尺寸删掉写死缺省是同一条道理：各家模型的档位不一样，替它猜只会撞上「参数非法」。
    /// </summary>
    public static class ModelSelection
    {
        /// <summary>
        /// 「模型」这一格的哨兵值：填它表示**不钉死模型**，由每次调用现挑。
        /// 它是配置层的值，绝不许当成模型名发给下游。
        /// </summary>
        public const string AutoSentinel = "自动";

        /// <summary>判断一个配置值是不是哨兵「自动」（前后空白不算）。</summary>
        /// <param name="value">配置值。</param>
        public static bool IsAuto(string value)
        {
            return string.Equals((value ?? "").Trim(), AutoSentinel, StringComparison.Ordinal);
        }

        /// <summary>
        /// 解析这次调用该用哪个模型。
        /// **返回空串表示一个 model 参数都不发**，由下游按它自己的默认来。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名；只用来定位探测产出与写人话，不参与任何判断分支。</param>
        /// <param name="configuredValue">本机配置里那一格现在的值：哨兵、具体模型名或空串。</param>
        /// <param name="overrideValue">本次调用指定的模型；空串表示没指定。</param>
        /// <param name="note">给人看的账：这次用了谁、依据是什么；没什么可说时为空串。</param>
        public static string Resolve(
            string repositoryRoot,
            string driverName,
            string configuredValue,
            string overrideValue,
            out string note)
        {
            var configured = (configuredValue ?? "").Trim();
            var chosen = (overrideValue ?? "").Trim();

            // 一、本次调用指定了，就用它——「用户在对话里主动挑一个」与「助手按需求挑一个」
            // 走的都是这一条路，它盖过本机配置的一切。
            if (chosen.Length > 0)
            {
                note = configured.Length > 0 && !string.Equals(configured, chosen, StringComparison.Ordinal)
                    ? $"本次调用指定了模型「{chosen}」，盖过本机配置的「{configured}」"
                    : $"本次调用指定了模型「{chosen}」";
                return chosen;
            }

            // 二、配的是个具体值（或什么都没配）——原样交出去，没什么可说的。
            if (!IsAuto(configured))
            {
                note = "";
                return configured;
            }

            // 三、配的是「自动」：问上次探测回来的清单，不问代码。
            var probePath = ProvisionPaths.ProbeResultFile(repositoryRoot, driverName);
            if (!System.IO.File.Exists(probePath))
            {
                note = $"模型配的是「{AutoSentinel}」，但还没探过 {driverName}：这次一个 model 参数都不发，由下游按它自己的默认来。要让「{AutoSentinel}」有得挑，先跑一次 bridge.probe --Driver {driverName}";
                return "";
            }

            CapabilityProbeResult probeResult;
            try
            {
                probeResult = CapabilityProbeResult.LoadFromFile(probePath);
            }
            catch (InvalidOperationException exception)
            {
                note = $"模型配的是「{AutoSentinel}」，但 {driverName} 的探测产出读不了（{exception.Message}）：这次一个 model 参数都不发";
                return "";
            }

            var names = probeResult.Models
                .Select(item => item.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            names.Sort(StringComparer.Ordinal);

            if (names.Count == 0)
            {
                note = $"模型配的是「{AutoSentinel}」，但 {driverName} 上次探测回来的模型清单是空的：这次一个 model 参数都不发。地址对不对、这个账号开通了什么，都可能是原因";
                return "";
            }

            // 挑序数序第一项。这不是「最好的那个」，是**可复算、可解释的那个**——
            // 按名字里的字样（turbo / pro / 大版本号之类）排优先级就是把模型名写死进代码，
            // 那正是这一档要消灭的东西。要别的就在面板上选一项，或这次调用带 --Model。
            var provenance = DescribeProvenance(probeResult);
            note = $"模型配的是「{AutoSentinel}」：从 {driverName} 上次探测的 {names.Count} 项里挑了「{names[0]}」（清单序数序第一项{provenance}）。要钉死别的，在面板上选一项，或这次调用带 --Model";
            return names[0];
        }

        /// <summary>
        /// 「自动」这一档现在会挑谁——只读，不改任何东西，给面板与 bridge.catalog 用。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="note">这一句就是给人看的账。</param>
        /// <returns>会挑中的模型名；空串表示这次什么都不发。</returns>
        public static string PreviewAuto(string repositoryRoot, string driverName, out string note)
        {
            return Resolve(repositoryRoot, driverName, AutoSentinel, "", out note);
        }

        /// <summary>把「探于哪个地址、什么时候探的」拼成一句可以直接塞进账里的补语；没盖章时是空串。</summary>
        /// <param name="probeResult">探测产出。</param>
        private static string DescribeProvenance(CapabilityProbeResult probeResult)
        {
            var parts = new List<string>();
            if (probeResult.ProbedEndpoint.Length > 0)
            {
                parts.Add($"探于 {probeResult.ProbedEndpoint}");
            }

            if (probeResult.ProbedAtText.Length > 0)
            {
                parts.Add($"探测时间 {probeResult.ProbedAtText}");
            }

            return parts.Count == 0 ? "" : "，" + string.Join("，", parts);
        }
    }
}
