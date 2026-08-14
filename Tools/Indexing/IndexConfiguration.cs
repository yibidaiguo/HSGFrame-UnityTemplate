using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.Indexing
{
    /// <summary>从 JSON 读取的多类索引定义集合。</summary>
    public sealed class IndexConfiguration
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>索引定义列表。</summary>
        public IReadOnlyList<IndexDefinition> Definitions { get; set; } = Array.Empty<IndexDefinition>();

        /// <summary>从 JSON 文件读取索引配置。</summary>
        /// <param name="path">配置文件路径。</param>
        public static IndexConfiguration LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<IndexConfiguration>(json, JsonOptions);
        }
    }
}
