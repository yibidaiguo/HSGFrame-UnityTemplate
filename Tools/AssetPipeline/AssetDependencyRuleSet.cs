using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条依赖方向规则在 JSON 里的写法，键名与「导入规则.json」一样用中文。</summary>
    public sealed class AssetDependencyRuleDefinition
    {
        /// <summary>引用方目录前缀，相对 Assets 根；空串表示任意目录。</summary>
        [JsonPropertyName("引用方前缀")]
        public string FromPathPrefix { get; set; }

        /// <summary>被禁止引用的目录前缀，相对 Assets 根。</summary>
        [JsonPropertyName("禁止引用前缀")]
        public string ForbiddenPathPrefix { get; set; }

        /// <summary>这条规则为什么存在，会原样进违规报告。</summary>
        [JsonPropertyName("理由")]
        public string Reason { get; set; }
    }

    /// <summary>「dependency-direction-rules.json」的整份内容。</summary>
    public sealed class AssetDependencyRuleSet
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>规则清单。</summary>
        [JsonPropertyName("依赖方向规则")]
        public IReadOnlyList<AssetDependencyRuleDefinition> Rules { get; set; } = Array.Empty<AssetDependencyRuleDefinition>();

        /// <summary>从「dependency-direction-rules.json」读回规则；文件不存在时返回空集合。</summary>
        /// <param name="path">规则文件路径。</param>
        public static IReadOnlyList<AssetDependencyRule> LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<AssetDependencyRule>();
            }

            var ruleSet = JsonSerializer.Deserialize<AssetDependencyRuleSet>(File.ReadAllText(path), JsonOptions);
            if (ruleSet?.Rules == null)
            {
                return Array.Empty<AssetDependencyRule>();
            }

            var rules = new List<AssetDependencyRule>();
            foreach (var definition in ruleSet.Rules)
            {
                rules.Add(new AssetDependencyRule(
                    definition.FromPathPrefix,
                    definition.ForbiddenPathPrefix,
                    definition.Reason));
            }

            return rules;
        }
    }
}
