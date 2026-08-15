using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一个打包分组在 JSON 里的写法。</summary>
    public sealed class AssetBundleGroupDefinition
    {
        /// <summary>分组名，会原样进违规报告。</summary>
        [JsonPropertyName("分组名")]
        public string GroupName { get; set; }

        /// <summary>本组覆盖的目录前缀，相对 Assets 根，用正斜杠。</summary>
        [JsonPropertyName("路径前缀")]
        public string PathPrefix { get; set; }

        /// <summary>为 true 时是共享组：被多个分组引用的资产就该放在这里。</summary>
        [JsonPropertyName("是共享组")]
        public bool IsShared { get; set; }
    }

    /// <summary>「打包分组规则.json」的整份内容。</summary>
    public sealed class AssetBundleGroupRuleSet
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>打包分组清单。</summary>
        [JsonPropertyName("打包分组")]
        public IReadOnlyList<AssetBundleGroupDefinition> Groups { get; set; } = Array.Empty<AssetBundleGroupDefinition>();

        /// <summary>为 true 时把不落在任何分组里的资产报成违规；默认 true。</summary>
        [JsonPropertyName("未分组资产是否报错")]
        public bool ReportUngroupedAssets { get; set; } = true;

        /// <summary>从「打包分组规则.json」读回分组规则；文件不存在或内容为空时返回一组不报未分组资产的空规则，不抛异常。</summary>
        /// <param name="path">规则文件路径。</param>
        public static AssetBundleGroupRuleSet LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new AssetBundleGroupRuleSet
                {
                    Groups = Array.Empty<AssetBundleGroupDefinition>(),
                    ReportUngroupedAssets = false,
                };
            }

            var ruleSet = JsonSerializer.Deserialize<AssetBundleGroupRuleSet>(File.ReadAllText(path), JsonOptions);
            if (ruleSet?.Groups == null)
            {
                return new AssetBundleGroupRuleSet
                {
                    Groups = Array.Empty<AssetBundleGroupDefinition>(),
                    ReportUngroupedAssets = false,
                };
            }

            return ruleSet;
        }

        /// <summary>返回匹配给定资产路径的打包分组，取路径前缀最长的那一个；一个都不匹配返回 null。</summary>
        /// <param name="assetRelativePath">资产路径，相对 Assets 根。</param>
        public AssetBundleGroupDefinition FindGroup(string assetRelativePath)
        {
            if (string.IsNullOrEmpty(assetRelativePath))
            {
                return null;
            }

            // 最长前缀优先：_Project/Art/Shared/ 与 _Project/Art/ 同时存在时，
            // 共享目录里的资产必须归共享组。按声明顺序匹配会让嵌套的共享目录被外层
            // 业务组吃掉，整条共享检查就失效了，所以这里比的是前缀长度而不是声明顺序。
            AssetBundleGroupDefinition bestMatch = null;
            foreach (var group in Groups)
            {
                if (!MatchesPrefix(assetRelativePath, group.PathPrefix))
                {
                    continue;
                }

                if (bestMatch == null || group.PathPrefix.Length > bestMatch.PathPrefix.Length)
                {
                    bestMatch = group;
                }
            }

            return bestMatch;
        }

        // 空前缀视为「匹配所有路径」；非空前缀按忽略大小写比较开头。
        private static bool MatchesPrefix(string path, string prefix)
        {
            return string.IsNullOrEmpty(prefix)
                || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
