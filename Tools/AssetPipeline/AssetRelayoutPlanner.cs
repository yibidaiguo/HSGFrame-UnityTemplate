using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条搬迁提议：从哪搬到哪，以及是靠哪个关键词认出来的。</summary>
    public sealed class AssetRelayoutMove
    {
        /// <summary>构造一条搬迁提议。</summary>
        /// <param name="fromPath">现在在哪，相对 Assets 根。</param>
        /// <param name="toPath">该搬到哪，相对 Assets 根。</param>
        /// <param name="matchedKeyword">靠哪个关键词认出来的。</param>
        public AssetRelayoutMove(string fromPath, string toPath, string matchedKeyword)
        {
            FromPath = fromPath ?? "";
            ToPath = toPath ?? "";
            MatchedKeyword = matchedKeyword ?? "";
        }

        /// <summary>现在在哪，相对 Assets 根。</summary>
        public string FromPath { get; }

        /// <summary>该搬到哪，相对 Assets 根。</summary>
        public string ToPath { get; }

        /// <summary>靠哪个关键词认出来的。</summary>
        public string MatchedKeyword { get; }
    }

    /// <summary>一份搬迁计划：认出来的搬法，与认不出来、要人定的那些。</summary>
    public sealed class AssetRelayoutPlan
    {
        /// <summary>构造一份搬迁计划。</summary>
        /// <param name="moves">认出来的搬法。</param>
        /// <param name="undecided">认不出来、要人定的资产路径。</param>
        /// <param name="failureReason">整份计划算不出来的原因；正常为空串。</param>
        public AssetRelayoutPlan(
            IReadOnlyList<AssetRelayoutMove> moves,
            IReadOnlyList<string> undecided,
            string failureReason)
        {
            Moves = moves ?? Array.Empty<AssetRelayoutMove>();
            Undecided = undecided ?? Array.Empty<string>();
            FailureReason = failureReason ?? "";
        }

        /// <summary>认出来的搬法。</summary>
        public IReadOnlyList<AssetRelayoutMove> Moves { get; }

        /// <summary>认不出来、要人定的资产路径。</summary>
        public IReadOnlyList<string> Undecided { get; }

        /// <summary>整份计划算不出来的原因；正常为空串。</summary>
        public string FailureReason { get; }
    }

    /// <summary>
    /// 按文件名给平铺的资产算搬迁计划：<c>Art/Model/M_BigRock.fbx</c> → <c>Art/Model/Rock/Boulder/M_BigRock.fbx</c>。
    ///
    /// **认不出来就不猜。** 名字里没有能对上的关键词时，这条资产进「要人定」，一个字都不动。
    /// 猜错一个资产的归属，代价是以后没人找得到它，而那种错**不会报错**——
    /// 它只是安静地待在一个不该待的夹子里。所以宁可留着让人看一眼。
    ///
    /// 这个类只算不搬：搬是命令层的事，而且默认干跑（决策 92——调东西不该靠反复真改）。
    /// </summary>
    public static class AssetRelayoutPlanner
    {
        /// <summary>资产名的前缀表，算模块名之前先剥掉。</summary>
        private static readonly string[] NamePrefixes = { "M_", "T_", "Mat_", "A_", "S_", "AN_", "AC_", "P_", "SA_", "F_" };

        /// <summary>
        /// 给一个 Assets 根算搬迁计划。
        /// </summary>
        /// <param name="assetsRootDirectory">Unity 工程的 Assets 目录。</param>
        /// <param name="ruleSet">分层词表，用来判「哪些还没落到位」。</param>
        /// <param name="keywordFilePath">关键词表文件。</param>
        public static AssetRelayoutPlan Plan(
            string assetsRootDirectory,
            AssetLayoutRuleSet ruleSet,
            string keywordFilePath)
        {
            if (ruleSet == null || ruleSet.LoadFailureReason.Length > 0)
            {
                return new AssetRelayoutPlan(null, null, ruleSet?.LoadFailureReason ?? "没有分层词表");
            }

            if (string.IsNullOrWhiteSpace(assetsRootDirectory) || !Directory.Exists(assetsRootDirectory))
            {
                return new AssetRelayoutPlan(null, null, $"Assets 根目录不存在：{assetsRootDirectory}");
            }

            List<KeywordRule> keywordRules;
            try
            {
                keywordRules = LoadKeywordRules(keywordFilePath);
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                return new AssetRelayoutPlan(null, null, $"关键词表读不动：{exception.Message}");
            }

            // 违规清单直接问检查器要——**判「哪些不合规」只能有一处定义**，
            // 这里再写一遍迟早与门禁漂成两套，那时「门禁说红、搬迁说没事」谁也说不清谁对。
            var violations = AssetLayoutChecker.Check(assetsRootDirectory, ruleSet);

            var moves = new List<AssetRelayoutMove>();
            var undecided = new List<string>();

            foreach (var violation in violations)
            {
                var relative = violation.AssetPath;
                if (!relative.StartsWith(ruleSet.AssetRoot + "/", StringComparison.Ordinal))
                {
                    undecided.Add(relative);
                    continue;
                }

                var withinArt = relative.Substring(ruleSet.AssetRoot.Length + 1);
                var segments = withinArt.Split('/');
                if (segments.Length < 2)
                {
                    undecided.Add(relative);
                    continue;
                }

                var typeName = segments[0];
                var fileName = segments[segments.Length - 1];
                var rule = ruleSet.Find(typeName);
                if (rule == null)
                {
                    // 类型都不认识，搬去哪无从谈起。
                    undecided.Add(relative);
                    continue;
                }

                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (rule.AllowedExtensions.Count > 0 && !rule.AllowedExtensions.Contains(extension, StringComparer.Ordinal))
                {
                    // 放错了树。**该搬去哪棵树要人定**：一个 .fbx 躺在 Animation/ 下，
                    // 它可能该进 Model/，也可能该被提成 clip 之后删掉——那是两件完全不同的事。
                    undecided.Add(relative);
                    continue;
                }

                var matched = Match(keywordRules, fileName);
                if (matched == null || !rule.Categories.Contains(matched.Category, StringComparer.Ordinal))
                {
                    undecided.Add(relative);
                    continue;
                }

                var target = string.Join(
                    "/",
                    ruleSet.AssetRoot, typeName, matched.Category, matched.Module, fileName);
                if (string.Equals(target, relative, StringComparison.Ordinal))
                {
                    continue;
                }

                moves.Add(new AssetRelayoutMove(relative, target, matched.Keyword));
            }

            return new AssetRelayoutPlan(
                moves.OrderBy(item => item.FromPath, StringComparer.Ordinal).ToList(),
                undecided.OrderBy(item => item, StringComparer.Ordinal).ToList(),
                "");
        }

        /// <summary>
        /// 按关键词认一个文件名。**长的关键词先试**——不然 "grass" 会抢在 "grasspatch" 前面命中，
        /// 而那两条常常指向不同的模块夹。
        /// </summary>
        private static KeywordRule Match(List<KeywordRule> rules, string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            foreach (var prefix in NamePrefixes)
            {
                if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    stem = stem.Substring(prefix.Length);
                    break;
                }
            }

            var lowered = stem.ToLowerInvariant();
            foreach (var rule in rules)
            {
                if (lowered.Contains(rule.Keyword, StringComparison.Ordinal))
                {
                    return rule;
                }
            }

            return null;
        }

        /// <summary>读关键词表，按关键词长度从长到短排好。</summary>
        private static List<KeywordRule> LoadKeywordRules(string keywordFilePath)
        {
            if (string.IsNullOrWhiteSpace(keywordFilePath) || !File.Exists(keywordFilePath))
            {
                throw new FileNotFoundException($"关键词表不存在：{keywordFilePath}");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(keywordFilePath));
            if (!document.RootElement.TryGetProperty("规则", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("关键词表里没有「规则」数组");
            }

            var rules = new List<KeywordRule>();
            foreach (var element in array.EnumerateArray())
            {
                var keyword = ReadString(element, "关键词").ToLowerInvariant();
                var category = ReadString(element, "门类");
                var module = ReadString(element, "模块");
                if (keyword.Length == 0 || category.Length == 0 || module.Length == 0)
                {
                    continue;
                }

                rules.Add(new KeywordRule(keyword, category, module));
            }

            return rules.OrderByDescending(item => item.Keyword.Length).ToList();
        }

        private static string ReadString(JsonElement owner, string key)
        {
            return owner.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        /// <summary>一条关键词规则。</summary>
        private sealed class KeywordRule
        {
            public KeywordRule(string keyword, string category, string module)
            {
                Keyword = keyword;
                Category = category;
                Module = module;
            }

            public string Keyword { get; }

            public string Category { get; }

            public string Module { get; }
        }
    }
}
