using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一个目录的资产导入规则，从「导入规则.json」读取。</summary>
    public sealed class AssetImportRule
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>目录用途，例如「贴图」或「待归档」。</summary>
        [JsonPropertyName("目录用途")]
        public string DirectoryPurpose { get; set; }

        /// <summary>文件名前缀，例如「T_」；待归档目录为空字符串。</summary>
        [JsonPropertyName("文件名前缀")]
        public string FileNamePrefix { get; set; }

        /// <summary>允许的扩展名集合，例如 [".png", ".tga"]。</summary>
        [JsonPropertyName("允许扩展名")]
        public IReadOnlyList<string> AllowedExtensions { get; set; } = Array.Empty<string>();

        /// <summary>命名风格，当前仅实现「PascalCase」。</summary>
        [JsonPropertyName("命名风格")]
        public string NamingStyle { get; set; }

        /// <summary>单个文件的最大字节数，超过即不合规。</summary>
        [JsonPropertyName("最大文件字节")]
        public long MaximumFileBytes { get; set; }

        /// <summary>这个目录的贴图进哪张图集，例如「SA_Inventory」；只给 UI 贴图目录写，其余留空。</summary>
        [JsonPropertyName("图集")]
        public string Atlas { get; set; }

        /// <summary>从「导入规则.json」读回一条导入规则。</summary>
        /// <param name="path">规则文件路径。</param>
        public static AssetImportRule LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AssetImportRule>(json, JsonOptions);
        }
    }
}
