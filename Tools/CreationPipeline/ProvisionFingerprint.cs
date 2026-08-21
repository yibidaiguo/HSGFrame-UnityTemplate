using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 供给指纹：schema 哈希、设计池汇总哈希与生成时间，供给产物与下游对账的依据。
    /// 指纹文件缺失视为「未供给」，对账放行；文件在而哈希对不上才出问题。
    /// </summary>
    public sealed class ProvisionFingerprint
    {
        /// <summary>
        /// 构造一份指纹。
        /// </summary>
        /// <param name="schemaHash">合并 schema 的规范化哈希。</param>
        /// <param name="designDigestHash">设计池汇总的汇总哈希。</param>
        /// <param name="generatedAt">生成时间，UTC 的 ISO 8601 文本。</param>
        /// <param name="driverName">面向的 driver 名称。</param>
        /// <param name="contractRange">生成时校验过的契约版本区间。</param>
        public ProvisionFingerprint(
            string schemaHash,
            string designDigestHash,
            string generatedAt,
            string driverName,
            string contractRange)
        {
            SchemaHash = schemaHash ?? "";
            DesignDigestHash = designDigestHash ?? "";
            GeneratedAt = generatedAt ?? "";
            DriverName = driverName ?? "";
            ContractRange = contractRange ?? "";
        }

        /// <summary>合并 schema 的规范化哈希。</summary>
        public string SchemaHash { get; }

        /// <summary>设计池汇总的汇总哈希。</summary>
        public string DesignDigestHash { get; }

        /// <summary>生成时间，UTC 的 ISO 8601 文本。</summary>
        public string GeneratedAt { get; }

        /// <summary>面向的 driver 名称。</summary>
        public string DriverName { get; }

        /// <summary>生成时校验过的契约版本区间。</summary>
        public string ContractRange { get; }

        /// <summary>
        /// 把 schema 规范化成一段确定性文本再取 SHA256，返回小写十六进制。
        /// 规范化规则写死：版本 / 实体 / id 模式 / 排序后的字段行 / 排序后的分类型必填行 /
        /// 初始状态 / 排序后的转换行，行与行之间用 \n 连接。
        /// </summary>
        /// <param name="schema">要哈希的合并 schema。</param>
        public static string ComputeSchemaHash(PoolSchema schema)
        {
            var lines = new List<string>
            {
                $"版本={schema.SchemaVersion}",
                $"实体={schema.EntityName}",
                $"id模式={schema.IdentifierPattern}"
            };

            var fields = new List<PoolSchemaField>(schema.Fields);
            fields.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            foreach (var field in fields)
            {
                lines.Add(
                    $"字段|{field.Name}|{field.FieldType}|{field.IsRequired}|{field.ElementType}|"
                    + field.MinimumCount.ToString(CultureInfo.InvariantCulture)
                    + $"|{field.Ownership}|{field.IsNullable}|{field.IsEditableAfterLock}|"
                    + string.Join(",", field.EnumValues));
            }

            var requiredKeys = new List<string>(schema.RequiredByType.Keys);
            requiredKeys.Sort(StringComparer.Ordinal);
            foreach (var key in requiredKeys)
            {
                lines.Add($"分类型必填|{key}|{string.Join(",", schema.RequiredByType[key])}");
            }

            var initialState = schema.StateMachine != null ? schema.StateMachine.InitialState : "";
            lines.Add($"初始状态={initialState}");

            if (schema.StateMachine != null)
            {
                var transitions = new List<PoolStateTransition>(schema.StateMachine.Transitions);
                transitions.Sort(static (left, right) =>
                {
                    var byFrom = string.CompareOrdinal(left.From, right.From);
                    if (byFrom != 0)
                    {
                        return byFrom;
                    }

                    var byTo = string.CompareOrdinal(left.To, right.To);
                    if (byTo != 0)
                    {
                        return byTo;
                    }

                    return string.CompareOrdinal(left.Actor, right.Actor);
                });

                foreach (var transition in transitions)
                {
                    lines.Add($"转换|{transition.From}|{transition.To}|{transition.Actor}");
                }
            }

            return HashHexLower(string.Join("\n", lines));
        }

        /// <summary>
        /// 计算设计池汇总的汇总哈希：列 &lt;池根&gt;/Designs/Digest/ 下的 *.md（不递归），
        /// 按文件名序数序排序，每文件一行「文件名|内容 SHA256」，\n 连接后再取一次 SHA256。
        /// 目录不存在或一个文件都没有时，对空字符串取 SHA256。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string ComputeDesignDigestHash(string poolRoot)
        {
            var lines = new List<string>();
            var directory = PoolPaths.DesignSummaryDirectory(poolRoot);
            if (Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, "*.md").ToList();
                files.Sort(StringComparer.Ordinal);
                foreach (var file in files)
                {
                    lines.Add($"{Path.GetFileName(file)}|{HashHexLower(File.ReadAllText(file))}");
                }
            }

            return HashHexLower(string.Join("\n", lines));
        }

        /// <summary>
        /// 造一份当前时刻的指纹：生成时间取 UTC 的 ISO 8601 秒级文本。
        /// </summary>
        /// <param name="driverName">面向的 driver 名称。</param>
        /// <param name="contractRange">生成时校验过的契约版本区间。</param>
        /// <param name="schemaHash">合并 schema 的规范化哈希。</param>
        /// <param name="designDigestHash">设计池汇总的汇总哈希。</param>
        public static ProvisionFingerprint Create(
            string driverName,
            string contractRange,
            string schemaHash,
            string designDigestHash)
        {
            return new ProvisionFingerprint(
                schemaHash,
                designDigestHash,
                DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                driverName,
                contractRange);
        }

        /// <summary>
        /// 把指纹写成 JSON 文件，键为 驱动 / 契约版本 / schema哈希 / 设计池汇总哈希 / 生成时间；
        /// 目标目录不存在时先创建。
        /// </summary>
        /// <param name="filePath">指纹文件的输出路径。</param>
        public void WriteTo(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new Dictionary<string, string>
            {
                ["驱动"] = DriverName,
                ["契约版本"] = ContractRange,
                ["schema哈希"] = SchemaHash,
                ["设计池汇总哈希"] = DesignDigestHash,
                ["生成时间"] = GeneratedAt
            };

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(false));
        }

        /// <summary>
        /// 读回指纹文件；文件不存在返回 null。
        /// </summary>
        /// <param name="filePath">指纹文件的路径。</param>
        public static ProvisionFingerprint Read(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            return new ProvisionFingerprint(
                ReadStringOrEmpty(root, "schema哈希"),
                ReadStringOrEmpty(root, "设计池汇总哈希"),
                ReadStringOrEmpty(root, "生成时间"),
                ReadStringOrEmpty(root, "驱动"),
                ReadStringOrEmpty(root, "契约版本"));
        }

        /// <summary>
        /// 把指纹文件与当前算出的两个哈希对账。指纹文件不存在返回空列表（未供给，不算问题）；
        /// 任一哈希对不上就各出一条 PoolFinding。
        /// </summary>
        /// <param name="filePath">指纹文件的路径。</param>
        /// <param name="expectedSchemaHash">当前算出的 schema 哈希。</param>
        /// <param name="expectedDesignDigestHash">当前算出的设计池汇总哈希。</param>
        public static IReadOnlyList<PoolFinding> Reconcile(
            string filePath,
            string expectedSchemaHash,
            string expectedDesignDigestHash)
        {
            if (!File.Exists(filePath))
            {
                return Array.Empty<PoolFinding>();
            }

            var fingerprint = Read(filePath);
            var findings = new List<PoolFinding>();
            if (!string.Equals(fingerprint.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"schema 哈希与指纹不一致：指纹里是「{fingerprint.SchemaHash}」，当前算出来是「{expectedSchemaHash}」",
                    "重跑 bridge.provision 生成新产物，并重新导入下游助手",
                    ""));
            }

            if (!string.Equals(fingerprint.DesignDigestHash, expectedDesignDigestHash, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"设计池汇总哈希与指纹不一致：指纹里是「{fingerprint.DesignDigestHash}」，当前算出来是「{expectedDesignDigestHash}」",
                    "重跑 bridge.provision 生成新产物，并重新导入下游助手",
                    ""));
            }

            return findings;
        }

        /// <summary>写指纹文件用的序列化选项：缩进 + 中文不转义。</summary>
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

        /// <summary>对一段文本取 UTF-8 字节的 SHA256，返回小写十六进制。</summary>
        private static string HashHexLower(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
