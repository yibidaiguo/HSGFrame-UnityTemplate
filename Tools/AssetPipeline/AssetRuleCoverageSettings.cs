using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>「rule-coverage.json」的整份内容：查哪些树、放行哪些目录。</summary>
    public sealed class AssetRuleCoverageSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>要检查的扫描根，相对 Assets 根，正斜杠。</summary>
        [JsonPropertyName("扫描根")]
        public IReadOnlyList<string> ScanRoots { get; set; } = Array.Empty<string>();

        /// <summary>放行哪些目录，相对 Assets 根，正斜杠；按路径段对齐匹配，目录下的子目录一并放行。</summary>
        [JsonPropertyName("豁免目录")]
        public IReadOnlyList<string> ExemptDirectories { get; set; } = Array.Empty<string>();

        /// <summary>从「rule-coverage.json」读回覆盖范围配置；文件不存在或内容为空时返回两项都为空的实例，不抛异常。</summary>
        /// <param name="path">配置文件路径。</param>
        public static AssetRuleCoverageSettings LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new AssetRuleCoverageSettings();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AssetRuleCoverageSettings();
            }

            var settings = JsonSerializer.Deserialize<AssetRuleCoverageSettings>(json, JsonOptions);
            return settings ?? new AssetRuleCoverageSettings();
        }
    }
}
