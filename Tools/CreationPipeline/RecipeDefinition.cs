using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条请求字段到 workflow 节点参数的映射：请求字段、节点 id 与参数名。</summary>
    public sealed class RecipeMappingEntry
    {
        /// <summary>
        /// 构造一条请求字段映射。
        /// </summary>
        /// <param name="requestField">请求字段，如「描述」或「规格.宽」。</param>
        /// <param name="nodeIdentifier">workflow 节点 id，如「2」。</param>
        /// <param name="parameterName">节点参数名，如「text」。</param>
        public RecipeMappingEntry(string requestField, string nodeIdentifier, string parameterName)
        {
            RequestField = requestField ?? "";
            NodeIdentifier = nodeIdentifier ?? "";
            ParameterName = parameterName ?? "";
        }

        /// <summary>请求字段，如「描述」或「规格.宽」。</summary>
        public string RequestField { get; }

        /// <summary>workflow 节点 id，如「2」。</summary>
        public string NodeIdentifier { get; }

        /// <summary>节点参数名，如「text」。</summary>
        public string ParameterName { get; }
    }

    /// <summary>一个锚点槽：给 workflow 注入参考图等固定输入的槽位。</summary>
    public sealed class RecipeAnchorSlot
    {
        /// <summary>
        /// 构造一个锚点槽。
        /// </summary>
        /// <param name="slotName">槽名，如「参考图」。</param>
        /// <param name="nodeIdentifier">workflow 节点 id。</param>
        /// <param name="parameterName">节点参数名。</param>
        public RecipeAnchorSlot(string slotName, string nodeIdentifier, string parameterName)
        {
            SlotName = slotName ?? "";
            NodeIdentifier = nodeIdentifier ?? "";
            ParameterName = parameterName ?? "";
        }

        /// <summary>槽名，如「参考图」。</summary>
        public string SlotName { get; }

        /// <summary>workflow 节点 id。</summary>
        public string NodeIdentifier { get; }

        /// <summary>节点参数名。</summary>
        public string ParameterName { get; }
    }

    /// <summary>
    /// 一份生图配方：workflow 节点集合、请求字段映射、锚点槽与依赖名，从
    /// &lt;仓库根&gt;/Bridges/&lt;driver&gt;/配方/&lt;配方名&gt;/ 的 workflow.json 与 mapping.json 读出。
    /// 文件缺失、JSON 坏掉或顶层不是对象时抛 InvalidOperationException，不做静默降级。
    /// </summary>
    public sealed class RecipeDefinition
    {
        /// <summary>
        /// 构造一份配方定义。
        /// </summary>
        /// <param name="name">配方名，如「图标@v5」。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="contractVersion">映射文件的契约版本。</param>
        /// <param name="workflowNodeIdentifiers">workflow 顶层的全部节点 id，序数序。</param>
        /// <param name="mappingEntries">请求字段映射列表。</param>
        /// <param name="anchorSlots">锚点槽列表。</param>
        /// <param name="dependencyNames">配方声明的依赖名列表。</param>
        public RecipeDefinition(
            string name,
            string assetType,
            string contractVersion,
            IReadOnlyList<string> workflowNodeIdentifiers,
            IReadOnlyList<RecipeMappingEntry> mappingEntries,
            IReadOnlyList<RecipeAnchorSlot> anchorSlots,
            IReadOnlyList<string> dependencyNames)
        {
            Name = name ?? "";
            AssetType = assetType ?? "";
            ContractVersion = contractVersion ?? "";
            WorkflowNodeIdentifiers = workflowNodeIdentifiers ?? Array.Empty<string>();
            MappingEntries = mappingEntries ?? Array.Empty<RecipeMappingEntry>();
            AnchorSlots = anchorSlots ?? Array.Empty<RecipeAnchorSlot>();
            DependencyNames = dependencyNames ?? Array.Empty<string>();
        }

        /// <summary>配方名，如「图标@v5」。</summary>
        public string Name { get; }

        /// <summary>资产类型，如「图标」。</summary>
        public string AssetType { get; }

        /// <summary>映射文件的契约版本。</summary>
        public string ContractVersion { get; }

        /// <summary>workflow 顶层的全部节点 id，序数序。</summary>
        public IReadOnlyList<string> WorkflowNodeIdentifiers { get; }

        /// <summary>请求字段映射列表。</summary>
        public IReadOnlyList<RecipeMappingEntry> MappingEntries { get; }

        /// <summary>锚点槽列表。</summary>
        public IReadOnlyList<RecipeAnchorSlot> AnchorSlots { get; }

        /// <summary>配方声明的依赖名列表。</summary>
        public IReadOnlyList<string> DependencyNames { get; }

        /// <summary>
        /// 列某 driver 配方根目录下的全部配方名，序数序；目录不存在返回空列表，不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static IReadOnlyList<string> DiscoverNames(string repositoryRoot, string driverName)
        {
            var recipeRoot = RecipePaths.RecipeRootDirectory(repositoryRoot, driverName);
            var recipeNames = new List<string>();
            if (!Directory.Exists(recipeRoot))
            {
                return recipeNames;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(recipeRoot))
            {
                recipeNames.Add(Path.GetFileName(directoryPath));
            }

            recipeNames.Sort(StringComparer.Ordinal);
            return recipeNames;
        }

        /// <summary>
        /// 读取并校验一份配方：workflow.json 与 mapping.json 都要在、顶层是对象。
        /// 文件缺失、JSON 语法错误或顶层不是对象时抛 InvalidOperationException。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="recipeName">配方名，如「图标@v5」。</param>
        /// <exception cref="InvalidOperationException">配方文件缺失、非法或顶层不是对象时抛出。</exception>
        public static RecipeDefinition Load(string repositoryRoot, string driverName, string recipeName)
        {
            var workflowPath = RecipePaths.WorkflowFile(repositoryRoot, driverName, recipeName);
            if (!File.Exists(workflowPath))
            {
                throw new InvalidOperationException($"找不到配方 workflow 文件：{workflowPath}");
            }

            List<string> workflowNodeIdentifiers;
            using (var workflowDocument = ParseObject(workflowPath, "配方 workflow 文件"))
            {
                workflowNodeIdentifiers = new List<string>();
                foreach (var property in workflowDocument.RootElement.EnumerateObject())
                {
                    workflowNodeIdentifiers.Add(property.Name);
                }

                workflowNodeIdentifiers.Sort(StringComparer.Ordinal);
            }

            var mappingPath = RecipePaths.MappingFile(repositoryRoot, driverName, recipeName);
            if (!File.Exists(mappingPath))
            {
                throw new InvalidOperationException($"找不到配方映射文件：{mappingPath}");
            }

            using var mappingDocument = ParseObject(mappingPath, "配方映射文件");
            var root = mappingDocument.RootElement;

            var mappingEntries = new List<RecipeMappingEntry>();
            if (root.TryGetProperty("映射", out var mappingElement) && mappingElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mappingElement.EnumerateArray())
                {
                    mappingEntries.Add(new RecipeMappingEntry(
                        ReadStringOrEmpty(item, "请求字段"),
                        ReadStringOrEmpty(item, "节点id"),
                        ReadStringOrEmpty(item, "参数名")));
                }
            }

            var anchorSlots = new List<RecipeAnchorSlot>();
            if (root.TryGetProperty("锚点槽", out var anchorElement) && anchorElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in anchorElement.EnumerateArray())
                {
                    anchorSlots.Add(new RecipeAnchorSlot(
                        ReadStringOrEmpty(item, "槽名"),
                        ReadStringOrEmpty(item, "节点id"),
                        ReadStringOrEmpty(item, "参数名")));
                }
            }

            return new RecipeDefinition(
                ReadStringOrEmpty(root, "配方名"),
                ReadStringOrEmpty(root, "资产类型"),
                ReadStringOrEmpty(root, "契约版本"),
                workflowNodeIdentifiers,
                mappingEntries,
                anchorSlots,
                ReadStringList(root, "依赖"));
        }

        /// <summary>解析一份必须是顶层对象的 JSON 文件；缺失、坏 JSON 或顶层不是对象时抛 InvalidOperationException。</summary>
        private static JsonDocument ParseObject(string filePath, string description)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"{description}不是合法 JSON：{filePath}：{exception.Message}", exception);
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidOperationException($"{description}不是合法 JSON：{filePath}");
            }

            return document;
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

        /// <summary>读字符串数组；缺失或类型不对给空列表。</summary>
        private static IReadOnlyList<string> ReadStringList(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (!element.TryGetProperty(propertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }
    }
}
