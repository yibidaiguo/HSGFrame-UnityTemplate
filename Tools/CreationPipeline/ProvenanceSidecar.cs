using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>溯源边车的不可变模型：对应「溯源」schema 的 13 个字段，从 JSON 读出、按 schema 键序写回。</summary>
    public sealed class ProvenanceSidecar
    {
        /// <summary>写盘用序列化选项：中文键原样输出、不缩进。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 构造一份溯源边车。
        /// </summary>
        /// <param name="assetRequestIdentifier">关联的资产请求 id。</param>
        /// <param name="variantIndex">变体序号。</param>
        /// <param name="productionMethod">产出方式：生成 / 人工产出 / 加工。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="recipeName">配方名，可为空。</param>
        /// <param name="randomSeed">随机种，可为空。</param>
        /// <param name="promptLines">提示词行列表。</param>
        /// <param name="styleAnchors">风格锚点：字段名到原始 JSON 文本的映射。</param>
        /// <param name="generatedAt">生成时间。</param>
        /// <param name="fileHash">文件哈希。</param>
        /// <param name="inspectionResults">机检结果：字段名到原始 JSON 文本的映射。</param>
        /// <param name="isChosen">是否当选。</param>
        /// <param name="contractVersion">契约版本。</param>
        public ProvenanceSidecar(
            string assetRequestIdentifier,
            int variantIndex,
            string productionMethod,
            string driverName,
            string recipeName,
            string randomSeed,
            IReadOnlyList<string> promptLines,
            IReadOnlyDictionary<string, string> styleAnchors,
            string generatedAt,
            string fileHash,
            IReadOnlyDictionary<string, string> inspectionResults,
            bool isChosen,
            string contractVersion)
        {
            AssetRequestIdentifier = assetRequestIdentifier;
            VariantIndex = variantIndex;
            ProductionMethod = productionMethod;
            DriverName = driverName;
            RecipeName = recipeName ?? "";
            RandomSeed = randomSeed ?? "";
            PromptLines = promptLines ?? Array.Empty<string>();
            StyleAnchors = styleAnchors ?? new Dictionary<string, string>();
            GeneratedAt = generatedAt;
            FileHash = fileHash;
            InspectionResults = inspectionResults ?? new Dictionary<string, string>();
            IsChosen = isChosen;
            ContractVersion = contractVersion;
        }

        /// <summary>关联的资产请求 id。</summary>
        public string AssetRequestIdentifier { get; }

        /// <summary>变体序号。</summary>
        public int VariantIndex { get; }

        /// <summary>产出方式：生成 / 人工产出 / 加工。</summary>
        public string ProductionMethod { get; }

        /// <summary>driver 名。</summary>
        public string DriverName { get; }

        /// <summary>配方名，可为空。</summary>
        public string RecipeName { get; }

        /// <summary>随机种，可为空。</summary>
        public string RandomSeed { get; }

        /// <summary>提示词行列表。</summary>
        public IReadOnlyList<string> PromptLines { get; }

        /// <summary>风格锚点：字段名到原始 JSON 文本的映射。</summary>
        public IReadOnlyDictionary<string, string> StyleAnchors { get; }

        /// <summary>生成时间。</summary>
        public string GeneratedAt { get; }

        /// <summary>文件哈希。</summary>
        public string FileHash { get; }

        /// <summary>机检结果：字段名到原始 JSON 文本的映射。</summary>
        public IReadOnlyDictionary<string, string> InspectionResults { get; }

        /// <summary>是否当选。</summary>
        public bool IsChosen { get; }

        /// <summary>契约版本。</summary>
        public string ContractVersion { get; }

        /// <summary>
        /// 从 JSON 文件读一份溯源边车；字段缺失或类型不对时填默认值，任何异常都不抛出。
        /// </summary>
        /// <param name="filePath">边车 JSON 文件路径。</param>
        public static ProvenanceSidecar Read(string filePath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Empty();
                }

                return new ProvenanceSidecar(
                    GetString(root, "资产请求id"),
                    GetInt32(root, "变体序号"),
                    GetString(root, "产出方式"),
                    GetString(root, "driver"),
                    GetString(root, "配方"),
                    GetString(root, "随机种"),
                    GetStringArray(root, "提示词"),
                    GetObjectAsRawText(root, "风格锚点"),
                    GetString(root, "生成时间"),
                    GetString(root, "文件哈希"),
                    GetObjectAsRawText(root, "机检结果"),
                    GetBoolean(root, "当选"),
                    GetString(root, "契约版本"));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return Empty();
            }
        }

        /// <summary>
        /// 把这份边车按 schema 的中文键与字段顺序写回 JSON 文件；目录不存在时先创建。
        /// </summary>
        /// <param name="filePath">目标 JSON 文件路径。</param>
        public void WriteTo(string filePath)
        {
            var root = new JsonObject
            {
                ["资产请求id"] = AssetRequestIdentifier,
                ["变体序号"] = VariantIndex,
                ["产出方式"] = ProductionMethod,
                ["driver"] = DriverName,
                ["配方"] = RecipeName,
                ["随机种"] = RandomSeed,
                ["提示词"] = new JsonArray(PromptLines.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                ["风格锚点"] = ToObjectNode(StyleAnchors),
                ["生成时间"] = GeneratedAt,
                ["文件哈希"] = FileHash,
                ["机检结果"] = ToObjectNode(InspectionResults),
                ["当选"] = IsChosen,
                ["契约版本"] = ContractVersion
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(root, WriteOptions));
        }

        /// <summary>
        /// 计算文件内容的 SHA256，返回小写十六进制；文件不存在返回空串。
        /// </summary>
        /// <param name="filePath">要计算哈希的文件路径。</param>
        public static string ComputeFileHash(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "";
            }

            var bytes = SHA256.HashData(File.ReadAllBytes(filePath));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 为人工产出的变体产一份边车：产出方式「人工产出」、driver「人」、配方与随机种为空、
        /// 文件哈希按文件内容计算、生成时间取当前 UTC、契约版本 1.0.0、当选为 true。
        /// </summary>
        /// <param name="assetRequestIdentifier">关联的资产请求 id。</param>
        /// <param name="variantIndex">变体序号。</param>
        /// <param name="filePath">人工产出的变体文件路径。</param>
        public static ProvenanceSidecar ForManualProduction(string assetRequestIdentifier, int variantIndex, string filePath)
        {
            return new ProvenanceSidecar(
                assetRequestIdentifier,
                variantIndex,
                "人工产出",
                "人",
                "",
                "",
                Array.Empty<string>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ComputeFileHash(filePath),
                new Dictionary<string, string>(),
                true,
                "1.0.0");
        }

        /// <summary>全字段默认值的一份空边车。</summary>
        private static ProvenanceSidecar Empty()
        {
            return new ProvenanceSidecar("", 0, "", "", "", "", Array.Empty<string>(), new Dictionary<string, string>(), "", "", new Dictionary<string, string>(), false, "");
        }

        /// <summary>读字符串字段；缺失、null 或类型不对给空串。</summary>
        private static string GetString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读整数字段；缺失、null 或类型不对给 0。</summary>
        private static int GetInt32(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt32();
            }

            return 0;
        }

        /// <summary>读布尔字段；缺失、null 或类型不对给 false。</summary>
        private static bool GetBoolean(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element))
            {
                if (element.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>读字符串数组字段；缺失、null 或类型不对给空列表，非字符串元素给空串。</summary>
        private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                values.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : "");
            }

            return values;
        }

        /// <summary>读对象字段成「字段名 → 原始 JSON 文本」映射；缺失、null 或类型不对给空字典。</summary>
        private static IReadOnlyDictionary<string, string> GetObjectAsRawText(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>();
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                values[property.Name] = property.Value.GetRawText();
            }

            return values;
        }

        /// <summary>把「字段名 → 原始 JSON 文本」映射还原成 JSON 对象节点；文本解析失败时退化成字符串节点。</summary>
        private static JsonObject ToObjectNode(IReadOnlyDictionary<string, string> values)
        {
            var node = new JsonObject();
            foreach (var pair in values)
            {
                node[pair.Key] = JsonNode.Parse(pair.Value) ?? JsonValue.Create(pair.Value);
            }

            return node;
        }
    }
}
