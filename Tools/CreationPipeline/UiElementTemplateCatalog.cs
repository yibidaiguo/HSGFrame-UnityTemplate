using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一类元素的字段模板：必填哪些、选填哪些、要不要出图。</summary>
    public sealed class UiElementTemplate
    {
        /// <summary>构造一份模板。</summary>
        /// <param name="typeName">元素类型名。</param>
        /// <param name="requiredFields">必填字段。</param>
        /// <param name="optionalFields">选填字段。</param>
        /// <param name="needsImage">这类元素要不要出一张图。</param>
        public UiElementTemplate(
            string typeName,
            IReadOnlyList<string> requiredFields,
            IReadOnlyList<string> optionalFields,
            bool needsImage)
        {
            TypeName = typeName ?? "";
            RequiredFields = requiredFields ?? Array.Empty<string>();
            OptionalFields = optionalFields ?? Array.Empty<string>();
            NeedsImage = needsImage;
        }

        /// <summary>元素类型名。</summary>
        public string TypeName { get; }

        /// <summary>这一类必填的字段。</summary>
        public IReadOnlyList<string> RequiredFields { get; }

        /// <summary>这一类选填的字段。</summary>
        public IReadOnlyList<string> OptionalFields { get; }

        /// <summary>
        /// 这类元素要不要出一张图。
        /// Label 不出（文案由 UI Toolkit 的 Label 出，生图模型写不对字）；
        /// Container 不出（底图是另一个元素）；Decoration 默认不出（属于底图的一部分，
        /// 单独切只会往图集里塞没人引用的碎图）。
        /// </summary>
        public bool NeedsImage { get; }
    }

    /// <summary>
    /// 元素类型模板目录：基线 ⊕ 项目 ⊕ 业务三层合并（与资产规格同一套分层语义）。
    ///
    /// **为什么按类型给模板，不给所有元素同一套必填**：全量固定的话，
    /// 纯装饰件也要写「失败处理」，人只能一路填「无」——
    /// 填满「无」的表单和没填是一回事，而校验器还以为它填了。
    /// </summary>
    public sealed class UiElementTemplateCatalog
    {
        /// <summary>构造一份目录。</summary>
        /// <param name="commonRequiredFields">所有类型都得有的字段。</param>
        /// <param name="templates">类型名 → 模板。</param>
        /// <param name="findings">加载过程中的问题。</param>
        public UiElementTemplateCatalog(
            IReadOnlyList<string> commonRequiredFields,
            IReadOnlyDictionary<string, UiElementTemplate> templates,
            IReadOnlyList<PoolFinding> findings)
        {
            CommonRequiredFields = commonRequiredFields ?? Array.Empty<string>();
            Templates = templates ?? new Dictionary<string, UiElementTemplate>(StringComparer.Ordinal);
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>所有类型都得有的字段：id 是身份、类型决定用哪份模板、布局决定摆哪、验收决定怎么算做完。</summary>
        public IReadOnlyList<string> CommonRequiredFields { get; }

        /// <summary>类型名 → 模板。</summary>
        public IReadOnlyDictionary<string, UiElementTemplate> Templates { get; }

        /// <summary>加载过程中的问题。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>基线模板文件：Specifications/Baseline/ui-element-template.baseline.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string BaselineFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Baseline", "ui-element-template.baseline.json");
        }

        /// <summary>项目层模板文件：Specifications/Project/ui-element-template.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ProjectFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "ui-element-template.json");
        }

        /// <summary>业务层模板文件：Specifications/Business/&lt;模块&gt;/ui-element-template.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名。</param>
        public static string BusinessFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Business", moduleName, "ui-element-template.json");
        }

        /// <summary>按类型取模板；没有这一类给 null——**不给一个「通用模板」兜底**，
        /// 兜底等于默许任何拼错的类型名一路通过。</summary>
        /// <param name="typeName">元素类型名。</param>
        public UiElementTemplate Find(string typeName)
        {
            return Templates.TryGetValue(typeName ?? "", out var template) ? template : null;
        }

        /// <summary>
        /// 读模板目录。基线缺失是硬错（那是模板发的东西）；项目层与业务层缺失属正常。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名，留空表示只用基线与项目两层。</param>
        public static UiElementTemplateCatalog Load(string repositoryRoot, string moduleName)
        {
            var findings = new List<PoolFinding>();
            var templates = new Dictionary<string, UiElementTemplate>(StringComparer.Ordinal);
            var commonRequired = new List<string>();

            var baselineFile = BaselineFile(repositoryRoot);
            if (!File.Exists(baselineFile))
            {
                findings.Add(new PoolFinding(
                    baselineFile,
                    "元素类型模板基线文件不存在",
                    "从模板同步一份 Specifications/Baseline/ui-element-template.baseline.json",
                    "Specifications/Baseline/ui-element-template.baseline.json"));
                return new UiElementTemplateCatalog(commonRequired, templates, findings);
            }

            foreach (var (layerName, filePath) in CollectLayers(repositoryRoot, moduleName))
            {
                if (!TryParse(filePath, out var root, out var reason))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"{layerName}层元素类型模板读不动：{reason}",
                        "把文件修好；读不动与「没配」是两回事，不许当成没配",
                        "Specifications/Baseline/ui-element-template.baseline.json"));
                    continue;
                }

                if (root["通用必填"] is JsonArray common)
                {
                    // 上层整体替换下层：合并语义与资产规格一致——
                    // 逐条并起来的话，上层想去掉一条永远做不到。
                    commonRequired.Clear();
                    foreach (var item in common)
                    {
                        var name = ReadValue(item);
                        if (name.Length > 0)
                        {
                            commonRequired.Add(name);
                        }
                    }
                }

                if (root["元素类型"] is not JsonObject types)
                {
                    continue;
                }

                foreach (var pair in types)
                {
                    if (pair.Key.StartsWith("_", StringComparison.Ordinal) || pair.Value is not JsonObject body)
                    {
                        continue;
                    }

                    templates[pair.Key] = new UiElementTemplate(
                        pair.Key,
                        ReadList(body["必填"]),
                        ReadList(body["选填"]),
                        body["要出图"] is JsonValue flag && flag.TryGetValue<bool>(out var needsImage) && needsImage);
                }
            }

            return new UiElementTemplateCatalog(commonRequired, templates, findings);
        }

        /// <summary>按分层顺序列出要读的文件：基线 → 项目 → 业务，后读的覆盖先读的。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名。</param>
        private static List<(string LayerName, string FilePath)> CollectLayers(string repositoryRoot, string moduleName)
        {
            var layers = new List<(string, string)> { ("基线", BaselineFile(repositoryRoot)) };

            var projectFile = ProjectFile(repositoryRoot);
            if (File.Exists(projectFile))
            {
                layers.Add(("项目", projectFile));
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var businessFile = BusinessFile(repositoryRoot, moduleName);
                if (File.Exists(businessFile))
                {
                    layers.Add(("业务", businessFile));
                }
            }

            return layers;
        }

        /// <summary>读一份模板文件的顶层对象。</summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="root">顶层对象。</param>
        /// <param name="reason">失败原因。</param>
        private static bool TryParse(string filePath, out JsonObject root, out string reason)
        {
            root = null;
            reason = "";
            try
            {
                root = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                reason = exception.Message;
                return false;
            }

            if (root == null)
            {
                reason = "顶层不是 JSON 对象";
                return false;
            }

            return true;
        }

        /// <summary>读一个字符串数组；不是数组给空表。</summary>
        /// <param name="node">数组节点。</param>
        private static IReadOnlyList<string> ReadList(JsonNode node)
        {
            var items = new List<string>();
            if (node is not JsonArray array)
            {
                return items;
            }

            foreach (var item in array)
            {
                var value = ReadValue(item);
                if (value.Length > 0)
                {
                    items.Add(value);
                }
            }

            return items;
        }

        /// <summary>读一个字符串值；不是字符串给空串。</summary>
        /// <param name="node">值节点。</param>
        private static string ReadValue(JsonNode node)
        {
            return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }
    }
}
