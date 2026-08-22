using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>资产请求的不可变模型：对应「资产请求」schema 的 15 个字段，从 JSON 读出、按 schema 键序写回。</summary>
    public sealed class AssetRequest
    {
        /// <summary>
        /// 无主资产的收容所：还没挂到任何需求上的图与模型都落在 <c>_Tasks/REQ-0000/</c> 下面。
        ///
        /// 为什么用一个哨兵号而不是让「需求id」可空：整条路径都是按
        /// <c>_Tasks/&lt;需求id&gt;/30-outputs/&lt;资产id&gt;/</c> 拼的，字段一空，落点、选片、溯源
        /// 全得各自想一套「没有需求时怎么办」。给一个合法的号，上下游一个字都不用改，
        /// 而且它符合 id 模式，schema 与校验器照旧管得住。
        /// 事后要把某张图认领给一条真需求，是把目录挪过去的事，不是改模型的事。
        /// </summary>
        public const string UnownedRequirementIdentifier = "REQ-0000";

        /// <summary>无主资产的工作项号，跟着 <see cref="UnownedRequirementIdentifier"/> 走。</summary>
        public const string UnownedWorkItemIdentifier = "WI-0000-00";

        /// <summary>写盘用序列化选项：中文键原样输出、不缩进，保证规格等对象字段的原始文本可往返。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 构造一份资产请求。
        /// </summary>
        /// <param name="identifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="requirementIdentifier">所属需求 id，如「REQ-0042」。</param>
        /// <param name="workItemIdentifier">工作项 id，如「WI-0042-03」。</param>
        /// <param name="domain">域，取自 schema 枚举。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="specification">规格：字段名到原始 JSON 文本的映射。</param>
        /// <param name="destination">落点目录。</param>
        /// <param name="namingText">命名文本。</param>
        /// <param name="description">描述。</param>
        /// <param name="styleAnchors">风格锚点：字段名到原始 JSON 文本的映射。</param>
        /// <param name="variantCount">变体数。</param>
        /// <param name="budgetCallLimit">预算调用上限。</param>
        /// <param name="dependencies">依赖的资产 id 列表。</param>
        /// <param name="isManual">是否人工产出。</param>
        /// <param name="contractVersion">契约版本。</param>
        public AssetRequest(
            string identifier,
            string requirementIdentifier,
            string workItemIdentifier,
            string domain,
            string assetType,
            IReadOnlyDictionary<string, string> specification,
            string destination,
            string namingText,
            string description,
            IReadOnlyDictionary<string, string> styleAnchors,
            int variantCount,
            int budgetCallLimit,
            IReadOnlyList<string> dependencies,
            bool isManual,
            string contractVersion)
        {
            Identifier = identifier;
            RequirementIdentifier = requirementIdentifier;
            WorkItemIdentifier = workItemIdentifier;
            Domain = domain;
            AssetType = assetType;
            Specification = specification ?? new Dictionary<string, string>();
            Destination = destination;
            NamingText = namingText;
            Description = description;
            StyleAnchors = styleAnchors ?? new Dictionary<string, string>();
            VariantCount = variantCount;
            BudgetCallLimit = budgetCallLimit;
            Dependencies = dependencies ?? Array.Empty<string>();
            IsManual = isManual;
            ContractVersion = contractVersion;
        }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        public string Identifier { get; }

        /// <summary>所属需求 id，如「REQ-0042」。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>工作项 id，如「WI-0042-03」。</summary>
        public string WorkItemIdentifier { get; }

        /// <summary>域，取自 schema 枚举。</summary>
        public string Domain { get; }

        /// <summary>资产类型，如「图标」。</summary>
        public string AssetType { get; }

        /// <summary>规格：字段名到原始 JSON 文本的映射，如「尺寸」→「[256,256]」。</summary>
        public IReadOnlyDictionary<string, string> Specification { get; }

        /// <summary>落点目录。</summary>
        public string Destination { get; }

        /// <summary>命名文本。</summary>
        public string NamingText { get; }

        /// <summary>描述。</summary>
        public string Description { get; }

        /// <summary>风格锚点：字段名到原始 JSON 文本的映射。</summary>
        public IReadOnlyDictionary<string, string> StyleAnchors { get; }

        /// <summary>变体数。</summary>
        public int VariantCount { get; }

        /// <summary>预算调用上限，取自「预算.调用上限」，缺省 0。</summary>
        public int BudgetCallLimit { get; }

        /// <summary>依赖的资产 id 列表。</summary>
        public IReadOnlyList<string> Dependencies { get; }

        /// <summary>是否人工产出。</summary>
        public bool IsManual { get; }

        /// <summary>契约版本。</summary>
        public string ContractVersion { get; }

        /// <summary>
        /// 从 JSON 文件读一份资产请求；字段缺失或类型不对时填默认值，任何异常都不抛出。
        /// </summary>
        /// <param name="filePath">资产请求 JSON 文件路径。</param>
        public static AssetRequest Read(string filePath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Empty();
                }

                return new AssetRequest(
                    GetString(root, "id"),
                    GetString(root, "需求id"),
                    GetString(root, "工作项id"),
                    GetString(root, "域"),
                    GetString(root, "资产类型"),
                    GetObjectAsRawText(root, "规格"),
                    GetString(root, "落点"),
                    GetString(root, "命名"),
                    GetString(root, "描述"),
                    GetObjectAsRawText(root, "风格锚点"),
                    GetInt32(root, "变体数"),
                    GetBudgetCallLimit(root),
                    GetStringArray(root, "依赖"),
                    GetBoolean(root, "人工产出"),
                    GetString(root, "契约版本"));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return Empty();
            }
        }

        /// <summary>
        /// 把这份资产请求按 schema 的中文键与字段顺序写回 JSON 文件；目录不存在时先创建。
        /// </summary>
        /// <param name="filePath">目标 JSON 文件路径。</param>
        public void WriteTo(string filePath)
        {
            var root = new JsonObject
            {
                ["id"] = Identifier,
                ["需求id"] = RequirementIdentifier,
                ["工作项id"] = WorkItemIdentifier,
                ["域"] = Domain,
                ["资产类型"] = AssetType,
                ["规格"] = ToObjectNode(Specification),
                ["落点"] = Destination,
                ["命名"] = NamingText,
                ["描述"] = Description,
                ["风格锚点"] = ToObjectNode(StyleAnchors),
                ["变体数"] = VariantCount,
                ["预算"] = new JsonObject { ["调用上限"] = BudgetCallLimit },
                ["依赖"] = new JsonArray(Dependencies.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                ["人工产出"] = IsManual,
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
        /// 分配下一个资产 id：需求号取自需求 id 的四位数字，序号是该需求下现存最大序号加一，
        /// 格式化成「ASSET-0042-03」；一个都没有时序号从 01 起。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <exception cref="ArgumentException">需求 id 里抠不出四位数字时抛出。</exception>
        public static string AllocateIdentifier(string repositoryRoot, string requirementIdentifier)
        {
            var match = Regex.Match(requirementIdentifier ?? "", @"\d{4}");
            if (!match.Success)
            {
                throw new ArgumentException($"需求 id「{requirementIdentifier}」里没有四位编号");
            }

            var requirementNumber = match.Value;
            var directory = AssetPaths.AssetRequestDirectory(repositoryRoot, requirementIdentifier);
            var prefix = $"ASSET-{requirementNumber}-";
            var maxSequence = 0;

            if (Directory.Exists(directory))
            {
                foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var sequenceText = fileName.Substring(prefix.Length);
                    if (sequenceText.Length == 2 && int.TryParse(sequenceText, out var sequence) && sequence > maxSequence)
                    {
                        maxSequence = sequence;
                    }
                }
            }

            return prefix + (maxSequence + 1).ToString().PadLeft(2, '0');
        }

        /// <summary>全字段默认值的一份空资产请求。</summary>
        private static AssetRequest Empty()
        {
            return new AssetRequest("", "", "", "", "", new Dictionary<string, string>(), "", "", "", new Dictionary<string, string>(), 0, 0, Array.Empty<string>(), false, "");
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

        /// <summary>从「预算」对象里取「调用上限」；缺失、类型不对给 0。</summary>
        private static int GetBudgetCallLimit(JsonElement root)
        {
            if (root.TryGetProperty("预算", out var budget) && budget.ValueKind == JsonValueKind.Object
                && budget.TryGetProperty("调用上限", out var limit) && limit.ValueKind == JsonValueKind.Number)
            {
                return limit.GetInt32();
            }

            return 0;
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
