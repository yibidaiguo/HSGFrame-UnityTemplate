using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.Indexing
{
    /// <summary>一份索引文件的模型：源信息、源哈希、生成时间与条目列表。</summary>
    public sealed class IndexDocument
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>索引名。</summary>
        public string IndexName { get; set; }

        /// <summary>扫描根目录，相对仓库根。</summary>
        public string SourceRoot { get; set; }

        /// <summary>全部命中文件按相对路径升序拼串后算出的 SHA256，供新鲜度校验比对。</summary>
        public string SourceHash { get; set; }

        /// <summary>生成时刻，ISO 8601 UTC 字符串。</summary>
        public string GeneratedAtUtc { get; set; }

        /// <summary>索引条目列表。</summary>
        public IReadOnlyList<IndexEntry> Entries { get; set; } = Array.Empty<IndexEntry>();

        /// <summary>本次运行复用旧条目的数量，只做统计，不写入索引文件。</summary>
        [JsonIgnore]
        public int ReusedEntryCount { get; set; }

        /// <summary>把索引写到指定路径（自动建目录）。</summary>
        /// <param name="path">输出文件路径。</param>
        public void SaveToFile(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }

        /// <summary>从指定路径读回一份索引。</summary>
        /// <param name="path">索引文件路径。</param>
        public static IndexDocument LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<IndexDocument>(json, JsonOptions);
        }
    }
}
