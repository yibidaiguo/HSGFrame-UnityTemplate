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
            IReadOnlyList<string> anchorSlotNames,
            IReadOnlyList<string> sizeOptions = null)
        {
            Name = name ?? "";
            AssetType = assetType ?? "";
            ApiName = apiName ?? "";
            ModelName = modelName ?? "";
            Size = size ?? "";
            PromptTemplate = promptTemplate ?? "";
            AnchorSlotNames = anchorSlotNames ?? Array.Empty<string>();
            SizeOptions = sizeOptions ?? Array.Empty<string>();
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

        /// <summary>
        /// 下游真能出的尺寸档位（如 1024x1024 / 1536x1024 / 1024x1536）；空表示不吸附、原样发。
        ///
        /// **为什么要有**：资产规格写的是项目要什么（1920×1080），而下游只出它自己那几档。
        /// 不吸附的话，要么被下游按参数非法退回，要么它自己挑一档回来——挑成什么样我们不知道，
        /// 溯源里也记不下「其实没按你要的尺寸出」。吸附放在这里做，是因为
        /// 「这家能出哪几档」是下游知识（决策 93），引擎那边不该知道。
        /// </summary>
        public IReadOnlyList<string> SizeOptions { get; }

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
            return Load(repositoryRoot, driverName, presetName, new List<string>());
        }

        /// <summary>
        /// 读一份预设，带上「已经走过哪几份」用来拦继承成环。
        ///
        /// **继承是给公共约束用的**：UI 那一类图共享「透明底、不出现文字、元素可切」这几条，
        /// 写在每份子配方里迟早会各改各的。父配方出公共的提示词片段与接口/尺寸缺省，
        /// 子配方只写自己那一层的差异——改一处公共规则，所有子配方跟着变。
        ///
        /// 合并规则只有一条：**子配方写了的就用子的，没写的才向上取**。
        /// 提示词是拼接（父在前、子在后）——父那几条是约束，约束该先立住。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        /// <param name="presetName">预设名。</param>
        /// <param name="visited">这条继承链上已经走过的预设名，用来拦环。</param>
        private static ImagePreset Load(
            string repositoryRoot, string driverName, string presetName, List<string> visited)
        {
            if (visited.Contains(presetName))
            {
                throw new InvalidOperationException(
                    $"配方继承成环了：{string.Join(" → ", visited)} → {presetName}。"
                    + "父配方不许直接或间接继承自己的子配方。");
            }

            visited.Add(presetName);
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

                // 先把父配方读出来，子配方没写的向上取。
                var parentName = ReadStringOrEmpty(root, "继承");
                var parent = parentName.Length > 0
                    ? Load(repositoryRoot, driverName, parentName, visited)
                    : null;

                var apiName = ReadStringOrEmpty(root, "接口");
                if (apiName.Length == 0 && parent != null)
                {
                    apiName = parent.ApiName;
                }

                if (!string.Equals(apiName, GenerationsApiName, StringComparison.Ordinal)
                    && !string.Equals(apiName, EditsApiName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"预设里的接口「{apiName}」不合法，只能是「{GenerationsApiName}」或「{EditsApiName}」：{filePath}");
                }

                // 提示词父在前、子在后：父那几条是约束（透明底、不出现文字），约束该先立住。
                var ownPrompt = ReadStringOrEmpty(root, "提示词模板");
                var prompt = parent == null || parent.PromptTemplate.Length == 0
                    ? ownPrompt
                    : (ownPrompt.Length == 0 ? parent.PromptTemplate : ownPrompt + "。" + parent.PromptTemplate);

                var slots = new List<string>(ReadStringList(root, "锚点槽"));
                if (parent != null)
                {
                    foreach (var slot in parent.AnchorSlotNames)
                    {
                        if (!slots.Contains(slot))
                        {
                            slots.Add(slot);
                        }
                    }
                }

                var preset = new ImagePreset(
                    presetName,
                    Inherit(ReadStringOrEmpty(root, "资产类型"), parent?.AssetType),
                    apiName,
                    Inherit(ReadStringOrEmpty(root, "模型"), parent?.ModelName),
                    Inherit(ReadStringOrEmpty(root, "尺寸"), parent?.Size),
                    prompt,
                    slots,
                    ReadStringList(root, "尺寸档位").Count > 0
                        ? ReadStringList(root, "尺寸档位")
                        : parent?.SizeOptions);

                if (string.Equals(apiName, GenerationsApiName, StringComparison.Ordinal) && preset.WantsReferenceImage)
                {
                    // **反过来这一条才是真会咬人的**：声明了要参考图，接口却是文生图。
                    // 这时调用方老老实实把参考图传进来了，而这条链路根本不发它——
                    // 图照出、钱照花，跟那张参考图一点关系都没有，没有任何一处报错。
                    // 真炸过：ui-element@v1 只写了「继承 ui-base@v1」没写「接口」，
                    // 静默继承了父配方的 generations，于是「照着设计图重画每个元素」
                    // 变成了「凭元素名字自由发挥」，出来一堆跟原图毫无关系的漂亮图标。
                    //
                    // 从前只查了 edits 缺参考图那一支——那一支下游会替我们报错，
                    // 恰恰是不会报错的这一支没人查。
                    throw new InvalidOperationException(
                        $"预设「{presetName}」声明了「{ReferenceImageSlotName}」锚点槽，接口却是 {GenerationsApiName}——"
                        + $"文生图不发参考图，出来的东西跟参考图无关。要图生图就把「接口」写成 {EditsApiName}"
                        + $"（继承来的接口不会自动变，子配方得自己写）：{filePath}");
                }

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

        /// <summary>子配方写了就用子的，没写才向上取父的；父也没有给空串。</summary>
        /// <param name="own">子配方自己写的值。</param>
        /// <param name="inherited">父配方的值。</param>
        private static string Inherit(string own, string inherited)
        {
            return own.Length > 0 ? own : (inherited ?? "");
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
