using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>.baseline.json 的读写与哈希比对：记录每张表上次同步时 Excel 与镜像的哈希对。</summary>
    public sealed class BaselineStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly Dictionary<string, TableBaseline> _tables = new Dictionary<string, TableBaseline>();

        /// <summary>从基线文件载入；文件不存在时返回一张空表。</summary>
        public static BaselineStore Load(string baselinePath)
        {
            var store = new BaselineStore();
            if (!File.Exists(baselinePath))
            {
                return store;
            }

            var json = File.ReadAllText(baselinePath);
            var document = JsonSerializer.Deserialize<BaselineDocument>(json, SerializerOptions);
            if (document?.Tables == null)
            {
                return store;
            }

            foreach (var pair in document.Tables)
            {
                store._tables[pair.Key] = pair.Value;
            }

            return store;
        }

        /// <summary>把当前基线表写回文件。</summary>
        public void Save(string baselinePath)
        {
            var document = new BaselineDocument { Tables = _tables };
            var json = JsonSerializer.Serialize(document, SerializerOptions);
            File.WriteAllText(baselinePath, json);
        }

        /// <summary>计算文件内容的小写十六进制 SHA-256。</summary>
        public static string ComputeFileHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>用 Excel 与镜像的当前文件哈希更新某张表的基线记录。</summary>
        public void Update(string tableName, string workbookPath, string mirrorPath)
        {
            _tables[tableName] = new TableBaseline
            {
                WorkbookHash = ComputeFileHash(workbookPath),
                MirrorHash = ComputeFileHash(mirrorPath)
            };
        }

        /// <summary>判断某张表的 Excel 是否仍与基线一致；不一致时通过 reason 说明原因。</summary>
        public bool IsWorkbookInSync(string tableName, string workbookPath, out string reason)
        {
            reason = string.Empty;

            if (!_tables.TryGetValue(tableName, out var baseline))
            {
                reason = $"表「{tableName}」在基线里没有记录，请先跑 config.sync";
                return false;
            }

            var currentHash = ComputeFileHash(workbookPath);
            if (string.Equals(currentHash, baseline.WorkbookHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var expectedPrefix = Prefix(baseline.WorkbookHash);
            var actualPrefix = Prefix(currentHash);
            reason = $"表「{tableName}」的 Excel 与基线不一致：期望哈希 {expectedPrefix}，实际哈希 {actualPrefix}，请先跑 config.sync";
            return false;
        }

        private static string Prefix(string hash)
        {
            return hash.Length >= 12 ? hash.Substring(0, 12) : hash;
        }

        private sealed class BaselineDocument
        {
            [JsonPropertyName("tables")]
            public Dictionary<string, TableBaseline> Tables { get; set; }
        }

        private sealed class TableBaseline
        {
            [JsonPropertyName("workbookHash")]
            public string WorkbookHash { get; set; }

            [JsonPropertyName("mirrorHash")]
            public string MirrorHash { get; set; }
        }
    }
}
