using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Bridges.Oaiimage
{
    /// <summary>
    /// 线上生图预设：从 <c>Bridges/oaiimage/presets/&lt;预设名&gt;/preset.json</c> 读出来的一份配方。
    ///
    /// **为什么不叫 recipes/、不复用 RecipeDefinition**：那一套的正本是一份 workflow.json，
    /// 也就是 ComfyUI 的节点图——「把值填进第几号节点的哪个参数」。线上 HTTP 接口没有节点，
    /// 只有 model / prompt / n / size 几个平铺的字段，塞不进那个形状。
    /// 而 <c>gate.recipe</c> 会扫每个 driver 的 <c>recipes/</c> 目录并要求 workflow.json 在，
    /// 放进去这道门禁必红。所以线上驱动自开一格 <c>presets/</c>，格式自定、桥自己读。
    /// </summary>
    public sealed class ImagePreset
    {
        /// <summary>预设目录名：Bridges/&lt;driver&gt;/presets。</summary>
        private const string PresetDirectoryName = "presets";

        /// <summary>预设正本的文件名。</summary>
        private const string PresetFileName = "preset.json";

        /// <summary>「尺寸」写成这个值时，尺寸从资产请求的「规格」里算，而不是写死在预设里。</summary>
        public const string SizeFromSpecification = "规格";

        /// <summary>参考图锚点槽的槽名，与 comfyui 的图生图配方同名。</summary>
        public const string ReferenceImageSlotName = "参考图";

        /// <summary>调 /images/generations 的「接口」取值。</summary>
        public const string GenerationsApiName = "generations";

        /// <summary>调 /images/edits 的「接口」取值。</summary>
        public const string EditsApiName = "edits";

        /// <summary>
        /// 构造一份预设。
        /// </summary>
        /// <param name="name">预设名，与目录名一致。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="apiName">走哪个接口：generations 或 edits。</param>
        /// <param name="modelName">模型名；空串表示用本机配置里的模型。</param>
        /// <param name="size">尺寸；空串表示用本机配置里的尺寸，「规格」表示从资产请求算。</param>
        /// <param name="promptTemplate">提示词模板，用 {字段} 引用资产请求里的字段。</param>
        /// <param name="anchorSlotNames">锚点槽名列表，传 null 视为空列表。</param>
        public ImagePreset(
            string name,
            string assetType,
            string apiName,
            string modelName,
            string size,
            string promptTemplate,
            IReadOnlyList<string> anchorSlotNames)
        {
            Name = name ?? "";
            AssetType = assetType ?? "";
            ApiName = apiName ?? "";
            ModelName = modelName ?? "";
            Size = size ?? "";
            PromptTemplate = promptTemplate ?? "";
            AnchorSlotNames = anchorSlotNames ?? Array.Empty<string>();
        }

        /// <summary>预设名，与目录名一致。</summary>
        public string Name { get; }

        /// <summary>资产类型，如「图标」。</summary>
        public string AssetType { get; }

        /// <summary>走哪个接口：generations 或 edits。</summary>
        public string ApiName { get; }

        /// <summary>模型名；空串表示用本机配置里的模型。</summary>
        public string ModelName { get; }

        /// <summary>尺寸；空串表示用本机配置里的尺寸，「规格」表示从资产请求的「规格.宽 / 规格.高」算。</summary>
        public string Size { get; }

        /// <summary>提示词模板，用 {字段} 引用资产请求里的字段（支持「规格.宽」这样的点路径）。</summary>
        public string PromptTemplate { get; }

        /// <summary>锚点槽名列表；edits 接口必须声明「参考图」。</summary>
        public IReadOnlyList<string> AnchorSlotNames { get; }

        /// <summary>这份预设要不要参考图（声明了「参考图」锚点槽即为要）。</summary>
        public bool WantsReferenceImage
        {
            get { return AnchorSlotNames.Any(slotName => string.Equals(slotName, ReferenceImageSlotName, StringComparison.Ordinal)); }
        }

        /// <summary>
        /// 某 driver 的预设根目录：&lt;仓库根&gt;/Bridges/&lt;driver&gt;/presets。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        public static string PresetRootDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "Bridges", driverName, PresetDirectoryName);
        }

        /// <summary>
        /// 某预设的正本文件：预设根目录/&lt;预设名&gt;/preset.json。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        /// <param name="presetName">预设名。</param>
        public static string PresetFile(string repositoryRoot, string driverName, string presetName)
        {
            return Path.Combine(PresetRootDirectory(repositoryRoot, driverName), presetName, PresetFileName);
        }

        /// <summary>
        /// 读一份预设并校验。文件缺失、JSON 坏掉、接口取值不合法、名字与目录名不符、
        /// edits 却没声明「参考图」锚点槽时抛 InvalidOperationException——
        /// 预设是调用契约，坏了必须当场亮出来，不做静默降级。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        /// <param name="presetName">预设名。</param>
        /// <exception cref="InvalidOperationException">预设缺失或不合法时抛出。</exception>
        public static ImagePreset Load(string repositoryRoot, string driverName, string presetName)
        {
            var filePath = PresetFile(repositoryRoot, driverName, presetName);
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"找不到预设文件：{filePath}（线上驱动的配方目录是 presets/，不是 recipes/）");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"预设文件不是合法 JSON：{filePath}：{exception.Message}", exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"预设文件的顶层必须是 JSON 对象：{filePath}");
                }

                var declaredName = ReadStringOrEmpty(root, "配方名");
                if (declaredName.Length > 0 && !string.Equals(declaredName, presetName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"预设里的配方名「{declaredName}」与目录名「{presetName}」不一致：{filePath}");
                }

                var apiName = ReadStringOrEmpty(root, "接口");
                if (!string.Equals(apiName, GenerationsApiName, StringComparison.Ordinal)
                    && !string.Equals(apiName, EditsApiName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"预设里的接口「{apiName}」不合法，只能是「{GenerationsApiName}」或「{EditsApiName}」：{filePath}");
                }

                var preset = new ImagePreset(
                    presetName,
                    ReadStringOrEmpty(root, "资产类型"),
                    apiName,
                    ReadStringOrEmpty(root, "模型"),
                    ReadStringOrEmpty(root, "尺寸"),
                    ReadStringOrEmpty(root, "提示词模板"),
                    ReadStringList(root, "锚点槽"));

                if (string.Equals(apiName, EditsApiName, StringComparison.Ordinal) && !preset.WantsReferenceImage)
                {
                    // edits 端点没有参考图无从谈起。预设不声明这个槽，调用方就看不出这份预设要图，
                    // 报错会推迟到下游那句「image is a required parameter」——
                    // 那句话指不到「这份预设要参考图」这件事上。
                    throw new InvalidOperationException(
                        $"预设「{presetName}」的接口是 {EditsApiName}，必须在「锚点槽」里声明「{ReferenceImageSlotName}」：{filePath}");
                }

                return preset;
            }
        }

        /// <summary>
        /// 列出某 driver 下全部预设名（目录里有 preset.json 才算），按序数序排序。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        public static IReadOnlyList<string> DiscoverNames(string repositoryRoot, string driverName)
        {
            var rootDirectory = PresetRootDirectory(repositoryRoot, driverName);
            if (!Directory.Exists(rootDirectory))
            {
                return Array.Empty<string>();
            }

            var names = Directory.EnumerateDirectories(rootDirectory)
                .Where(directoryPath => File.Exists(Path.Combine(directoryPath, PresetFileName)))
                .Select(Path.GetFileName)
                .ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>读字符串字段；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return (element.GetString() ?? "").Trim();
            }

            return "";
        }

        /// <summary>读字符串数组字段；缺失或类型不对给空列表，非字符串元素跳过。</summary>
        private static IReadOnlyList<string> ReadStringList(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
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
