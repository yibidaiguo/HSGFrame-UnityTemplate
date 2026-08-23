using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 策划文档规范：`doc.render` 生成什么、`gate.plandoc` 查什么，两边读的是同一份契约。
    ///
    /// 契约的正本是 `Specifications/Baseline/planning-doc.baseline.md` 里那段 JSON——
    /// **规范与它的机器可读形式住在同一个文件里**，不是散文一份、配置一份。
    /// 散文与 JSON 分家的规范活不过三次改动：改的人只会改自己看得见的那一份。
    /// </summary>
    public sealed class PlanningDocumentSpec
    {
        /// <summary>基线文件里那段 JSON 的锚点：这个小标题之后的第一个 json 围栏就是契约。</summary>
        private const string ContractHeadingMarker = "机器读的那份";

        /// <summary>参考示例路径，报错时指给人看。</summary>
        public const string ReferencePath = "Specifications/Baseline/planning-doc.baseline.md";

        private PlanningDocumentSpec(
            IReadOnlyList<string> frontMatterRequiredKeys,
            IReadOnlyList<string> authorityValues,
            IReadOnlyDictionary<string, IReadOnlyList<string>> requiredSectionsByType,
            IReadOnlyList<string> optionalSections,
            string acceptanceSection,
            string generatedSection,
            string generatedRegionBegin,
            string generatedRegionEnd)
        {
            FrontMatterRequiredKeys = frontMatterRequiredKeys;
            AuthorityValues = authorityValues;
            RequiredSectionsByType = requiredSectionsByType;
            OptionalSections = optionalSections;
            AcceptanceSection = acceptanceSection;
            GeneratedSection = generatedSection;
            GeneratedRegionBegin = generatedRegionBegin;
            GeneratedRegionEnd = generatedRegionEnd;
        }

        /// <summary>frontmatter 必备键，按基线里写的顺序。</summary>
        public IReadOnlyList<string> FrontMatterRequiredKeys { get; }

        /// <summary>「权威侧」这个键的合法取值。</summary>
        public IReadOnlyList<string> AuthorityValues { get; }

        /// <summary>需求类型 → 必填小节（按序）。类型不在表里时没有必填小节。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredSectionsByType { get; }

        /// <summary>选填小节：在位不报错，缺了也不报错，但顺序仍按基线排。</summary>
        public IReadOnlyList<string> OptionalSections { get; }

        /// <summary>验收标准那一节的标题，逐条判真假的那一节。</summary>
        public string AcceptanceSection { get; }

        /// <summary>生成区里那一节的标题。</summary>
        public string GeneratedSection { get; }

        /// <summary>生成区开始标记（整行比对）。</summary>
        public string GeneratedRegionBegin { get; }

        /// <summary>生成区结束标记（整行比对）。</summary>
        public string GeneratedRegionEnd { get; }

        /// <summary>frontmatter 里记生成区哈希的键名。</summary>
        public const string GeneratedHashKey = "生成区hash";

        /// <summary>
        /// 取某类型的必填小节（按序）；类型没在契约里登记时返回空列表。
        /// </summary>
        /// <param name="requirementType">需求类型，如「系统」。</param>
        public IReadOnlyList<string> RequiredSectionsFor(string requirementType)
        {
            if (requirementType != null && RequiredSectionsByType.TryGetValue(requirementType, out var sections))
            {
                return sections;
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// 读基线契约，再把项目层的追加项叠上去。
        ///
        /// 项目层文件（`Specifications/Project/planning-doc.json`）**只表达得出「加」**：
        /// 两个键都叫「追加…」，没有任何一个键能删掉基线定的小节。
        /// 「不可删」这条规矩靠形状保证，不靠校验器事后骂人——校验器骂完，人照样能把它删了。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <exception cref="FileNotFoundException">基线规范文件不在时抛出。</exception>
        /// <exception cref="InvalidOperationException">基线里找不到契约 JSON，或契约 JSON 语法不合法时抛出。</exception>
        public static PlanningDocumentSpec Load(string repositoryRoot)
        {
            var baselineFile = SpecificationPaths.BaselinePlanningDocumentFile(repositoryRoot);
            if (!File.Exists(baselineFile))
            {
                throw new FileNotFoundException(
                    $"策划文档规范基线不在：{baselineFile}。从模板同步一份 {ReferencePath}。",
                    baselineFile);
            }

            var contractJson = ExtractContractJson(File.ReadAllText(baselineFile));
            using (var document = ParseContract(contractJson, baselineFile))
            {
                var root = document.RootElement;

                var frontMatterKeys = ReadStringList(root, "frontmatter必备键");
                var authorityValues = ReadStringList(root, "权威侧取值");
                var requiredSections = ReadSectionMap(root, "必填小节");
                var optionalSections = ReadStringList(root, "选填小节");

                ApplyProjectLayer(repositoryRoot, frontMatterKeys, requiredSections);

                var readOnlySections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var pair in requiredSections)
                {
                    readOnlySections[pair.Key] = pair.Value;
                }

                return new PlanningDocumentSpec(
                    frontMatterKeys,
                    authorityValues,
                    readOnlySections,
                    optionalSections,
                    ReadString(root, "验收标准小节"),
                    ReadString(root, "生成区小节"),
                    ReadString(root, "生成区标记开始"),
                    ReadString(root, "生成区标记结束"));
            }
        }

        // 把项目层的追加项叠到基线上：小节追加在该类型必填小节的后面，
        // frontmatter 键追加在必备键的后面。重复的不重复加，顺序按项目层写的来。
        private static void ApplyProjectLayer(
            string repositoryRoot,
            List<string> frontMatterKeys,
            Dictionary<string, List<string>> requiredSections)
        {
            var projectFile = SpecificationPaths.ProjectPlanningDocumentFile(repositoryRoot);
            if (!File.Exists(projectFile))
            {
                return;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(projectFile));
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"项目层策划文档规范 JSON 语法不合法：{projectFile}：{exception.Message}",
                    exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                foreach (var key in ReadStringList(root, "追加frontmatter必备键"))
                {
                    if (!frontMatterKeys.Contains(key, StringComparer.Ordinal))
                    {
                        frontMatterKeys.Add(key);
                    }
                }

                if (!root.TryGetProperty("追加小节", out var appended) || appended.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                foreach (var property in appended.EnumerateObject())
                {
                    if (!requiredSections.TryGetValue(property.Name, out var sections))
                    {
                        sections = new List<string>();
                        requiredSections[property.Name] = sections;
                    }

                    foreach (var section in ReadStringArray(property.Value))
                    {
                        if (!sections.Contains(section, StringComparer.Ordinal))
                        {
                            sections.Add(section);
                        }
                    }
                }
            }
        }

        // 从基线 md 里抠出契约 JSON：锚在「机器读的那份」这个小标题上，取它之后第一个 ```json 围栏。
        // 不能取「文件里最后一个 json 围栏」——第七节讲项目层怎么写，那里也有一个 json 围栏。
        private static string ExtractContractJson(string markdown)
        {
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var headingIndex = -1;
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].StartsWith("#", StringComparison.Ordinal)
                    && lines[index].Contains(ContractHeadingMarker, StringComparison.Ordinal))
                {
                    headingIndex = index;
                    break;
                }
            }

            if (headingIndex < 0)
            {
                throw new InvalidOperationException(
                    $"策划文档规范基线里找不到「{ContractHeadingMarker}」那一节，读不出契约 JSON。");
            }

            for (var index = headingIndex + 1; index < lines.Length; index++)
            {
                if (!lines[index].TrimEnd().Equals("```json", StringComparison.Ordinal))
                {
                    continue;
                }

                var body = new List<string>();
                for (var inner = index + 1; inner < lines.Length; inner++)
                {
                    if (lines[inner].TrimEnd().Equals("```", StringComparison.Ordinal))
                    {
                        return string.Join("\n", body);
                    }

                    body.Add(lines[inner]);
                }

                throw new InvalidOperationException("策划文档规范基线里的契约 JSON 围栏没有闭合。");
            }

            throw new InvalidOperationException(
                $"策划文档规范基线的「{ContractHeadingMarker}」一节底下没有 json 围栏。");
        }

        private static JsonDocument ParseContract(string contractJson, string baselineFile)
        {
            try
            {
                return JsonDocument.Parse(contractJson);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"策划文档规范基线里的契约 JSON 语法不合法：{baselineFile}：{exception.Message}",
                    exception);
            }
        }

        private static List<string> ReadStringList(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value))
            {
                return new List<string>();
            }

            return ReadStringArray(value);
        }

        private static List<string> ReadStringArray(JsonElement value)
        {
            var result = new List<string>();
            if (value.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    result.Add(item.GetString());
                }
            }

            return result;
        }

        private static Dictionary<string, List<string>> ReadSectionMap(JsonElement root, string propertyName)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in value.EnumerateObject())
            {
                result[property.Name] = ReadStringArray(property.Value);
            }

            return result;
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return "";
        }
    }
}
