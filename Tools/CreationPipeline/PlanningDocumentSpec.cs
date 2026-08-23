using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 模块策划案规范：`plan.render` 生成什么、`gate.plandoc` 查什么，两边读的是同一份契约。
    ///
    /// 契约的正本是 `Specifications/Baseline/planning-doc.baseline.md` 里那段 JSON——
    /// **规范与它的机器可读形式住在同一个文件里**，不是散文一份、配置一份（决策 100）。
    /// </summary>
    public sealed class PlanningDocumentSpec
    {
        /// <summary>基线文件里那段 JSON 的锚点：这个小标题之后的第一个 json 围栏就是契约。</summary>
        private const string ContractHeading = "## 三、必填与选填";

        /// <summary>参考示例路径，报错时指给人看。</summary>
        public const string ReferencePath = "Specifications/Baseline/planning-doc.baseline.md";

        /// <summary>frontmatter 的「生成区hash」键名。</summary>
        public const string GeneratedHashKey = "生成区hash";

        private PlanningDocumentSpec(
            IReadOnlyList<string> frontMatterRequiredKeys,
            IReadOnlyList<string> statusValues,
            IReadOnlyList<string> authorityValues,
            IReadOnlyList<string> requiredSections,
            IReadOnlyList<string> optionalSections,
            string generatedSection,
            IReadOnlyList<string> generatedSubsections,
            string generatedRegionBegin,
            string generatedRegionEnd)
        {
            FrontMatterRequiredKeys = frontMatterRequiredKeys;
            StatusValues = statusValues;
            AuthorityValues = authorityValues;
            RequiredSections = requiredSections;
            OptionalSections = optionalSections;
            GeneratedSection = generatedSection;
            GeneratedSubsections = generatedSubsections;
            GeneratedRegionBegin = generatedRegionBegin;
            GeneratedRegionEnd = generatedRegionEnd;
        }

        /// <summary>frontmatter 必备键。</summary>
        public IReadOnlyList<string> FrontMatterRequiredKeys { get; }

        /// <summary>「状态」这一格的合法取值。</summary>
        public IReadOnlyList<string> StatusValues { get; }

        /// <summary>「权威侧」这一格的合法取值。</summary>
        public IReadOnlyList<string> AuthorityValues { get; }

        /// <summary>人写区的必填小节，按顺序。</summary>
        public IReadOnlyList<string> RequiredSections { get; }

        /// <summary>人写区的选填小节。</summary>
        public IReadOnlyList<string> OptionalSections { get; }

        /// <summary>生成区那一节的标题。</summary>
        public string GeneratedSection { get; }

        /// <summary>生成区里固定的几个子节，按顺序；缺料写「暂无」而不是省略。</summary>
        public IReadOnlyList<string> GeneratedSubsections { get; }

        /// <summary>生成区开始标记行。</summary>
        public string GeneratedRegionBegin { get; }

        /// <summary>生成区结束标记行。</summary>
        public string GeneratedRegionEnd { get; }

        /// <summary>
        /// 读规范：基线为底，项目层只能往上加。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <exception cref="FileNotFoundException">基线文件不在时抛出。</exception>
        public static PlanningDocumentSpec Load(string repositoryRoot)
        {
            var baselineFile = SpecificationPaths.BaselinePlanningDocumentFile(repositoryRoot);
            if (!File.Exists(baselineFile))
            {
                throw new FileNotFoundException(
                    $"模块策划案规范基线不在：{baselineFile}。从模板同步一份 {ReferencePath}。",
                    baselineFile);
            }

            var contractJson = ExtractContractJson(File.ReadAllText(baselineFile), baselineFile);
            using var document = ParseContract(contractJson, baselineFile);
            var root = document.RootElement;

            var frontMatterKeys = ReadStringList(root, "frontmatter必备键");
            var requiredSections = ReadStringList(root, "必填小节");
            ApplyProjectLayer(repositoryRoot, frontMatterKeys, requiredSections);

            return new PlanningDocumentSpec(
                frontMatterKeys,
                ReadStringList(root, "状态取值"),
                ReadStringList(root, "权威侧取值"),
                requiredSections,
                ReadStringList(root, "选填小节"),
                ReadString(root, "生成区小节"),
                ReadStringList(root, "生成区子节"),
                ReadString(root, "生成区标记开始"),
                ReadString(root, "生成区标记结束"));
        }

        // 项目层只表达得出「加」：两个键都叫「追加…」，删不出来（决策 100 推论一）。
        private static void ApplyProjectLayer(
            string repositoryRoot, List<string> frontMatterKeys, List<string> requiredSections)
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
                    $"项目层模块策划案规范 JSON 语法不合法：{projectFile}：{exception.Message}", exception);
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

                foreach (var section in ReadStringList(root, "追加小节"))
                {
                    if (!requiredSections.Contains(section, StringComparer.Ordinal))
                    {
                        requiredSections.Add(section);
                    }
                }
            }
        }

        // 契约那段 JSON 藏在散文里：锚点小标题之后的第一个 ```json 围栏。
        private static string ExtractContractJson(string markdown, string baselineFile)
        {
            var text = (markdown ?? "").Replace("\r\n", "\n");
            var headingIndex = text.IndexOf(ContractHeading, StringComparison.Ordinal);
            if (headingIndex < 0)
            {
                throw new InvalidOperationException(
                    $"模块策划案规范基线里找不到锚点小标题「{ContractHeading}」：{baselineFile}");
            }

            var fenceIndex = text.IndexOf("```json", headingIndex, StringComparison.Ordinal);
            if (fenceIndex < 0)
            {
                throw new InvalidOperationException(
                    $"模块策划案规范基线的「{ContractHeading}」之后没有 json 围栏：{baselineFile}");
            }

            var bodyStart = text.IndexOf('\n', fenceIndex);
            var bodyEnd = text.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (bodyStart < 0 || bodyEnd < 0)
            {
                throw new InvalidOperationException(
                    $"模块策划案规范基线的 json 围栏没有收尾：{baselineFile}");
            }

            return text.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
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
                    $"模块策划案规范基线里那段契约 JSON 语法不合法：{baselineFile}：{exception.Message}", exception);
            }
        }

        private static List<string> ReadStringList(JsonElement root, string propertyName)
        {
            var values = new List<string>();
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out var element)
                || element.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }

            return values;
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var element)
                && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : "";
        }
    }
}
