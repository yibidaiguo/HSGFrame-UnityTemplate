using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>索引里的一条：一张已经产出的资产。</summary>
    /// <param name="Naming">文件名（不含扩展名），如 T_ButtonSort。</param>
    /// <param name="AssetType">资产类型；推不出时为空串。</param>
    /// <param name="Module">模块名；通用件是 Shared。</param>
    /// <param name="Destination">工程内相对路径。</param>
    /// <param name="Palette">主色（hex，按权重降序）。</param>
    /// <param name="StyleFinal">照哪一版定稿出的；不知道为空串。</param>
    /// <param name="Origin">产出方式：效果图（人点确认的那张整屏原稿）/ 元素图（模型重绘出来的单件）。</param>
    public sealed record DesignLibraryEntry(
        string Naming,
        string AssetType,
        string Module,
        string Destination,
        IReadOnlyList<string> Palette,
        string StyleFinal,
        string Origin);

    /// <summary>
    /// 资产库索引：「这个项目已经做过什么」。
    ///
    /// **这一条是「不重新设计风格」的机器实现**（子文档 09 §四）。
    /// 没有它的话，美术每出一张图都是从零理解风格——配方里那几句提示词就是它知道的全部，
    /// 第 5 个界面和第 1 个之间没有任何机器可读的联系，风格靠人盯着，盯漏了就跑偏。
    ///
    /// 索引是**生成物**：扫磁盘上真有的资产算出来，不是手写账本。
    /// 手写的账本迟早跟磁盘对不上，而**对不上的索引比没有索引更糟**——
    /// 它会让「查过了，没有」这句话变成假的。
    /// </summary>
    public sealed class DesignLibraryIndex
    {
        /// <summary>写盘选项：缩进、中文原样。这份要给人读，也要能看 git diff。
        /// **必须以 JsonSerializerOptions.Default 为基底**——裸 new 出来的没有 TypeInfoResolver，
        /// ToJsonString 会当场抛「must specify a TypeInfoResolver」，而那句话指不到真因上。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>通用件的模块名，与落点目录同名。</summary>
        public const string SharedModuleName = "Shared";

        /// <summary>产出方式：人点确认的那张整屏原稿。**它才是该当风格锚点的东西。**</summary>
        public const string OriginReference = "效果图";

        /// <summary>产出方式：模型照着裁片重绘出来的单件。</summary>
        public const string OriginElement = "元素图";

        /// <summary>主色取几个。比定稿的 8 色少——索引是拿来比对的，不是拿来当色板用的。</summary>
        private const int PaletteClusterCount = 3;

        /// <summary>构造一份索引。</summary>
        /// <param name="entries">条目。</param>
        public DesignLibraryIndex(IReadOnlyList<DesignLibraryEntry> entries)
        {
            Entries = entries ?? Array.Empty<DesignLibraryEntry>();
        }

        /// <summary>全部条目。</summary>
        public IReadOnlyList<DesignLibraryEntry> Entries { get; }

        /// <summary>索引落点：Pools/Designs/library.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string FilePathFor(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "library.json");
        }

        /// <summary>UI 贴图的扫描根：UnityProject/Assets/Game/Art/Texture/Ui。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ScanRoot(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "UnityProject", "Assets", "Game", "Art", "Texture", "Ui");
        }

        /// <summary>设计库里各模块的参考图根：Pools/Designs/Art。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ReferenceRoot(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "Art");
        }

        /// <summary>某个模块的参考图目录：Pools/Designs/Art/&lt;模块&gt;/refs。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名。</param>
        public static string ReferenceDirectory(string repositoryRoot, string moduleName)
        {
            return Path.Combine(ReferenceRoot(repositoryRoot), moduleName, "refs");
        }

        /// <summary>
        /// 扫磁盘重建索引。
        ///
        /// **以落点里真有的文件为准**，资产请求只用来补充类型与定稿版本。
        /// 反过来（以请求为准）的话，请求建了但图没出来的也会进索引，
        /// 而索引存在的意义正是回答「这张图现在有没有」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="withPalette">要不要算主色。算主色要逐张解码 PNG，几百张时明显变慢；
        /// 只想对账「有没有」时关掉它。</param>
        public static DesignLibraryIndex Rebuild(string repositoryRoot, bool withPalette)
        {
            var entries = new List<DesignLibraryEntry>();
            var byNaming = ReadAssetRequests(repositoryRoot);

            // 先扫设计库里的**效果图**：那是人点确认的整屏原稿。
            // 它们不是游戏资产（不进包、不被代码引用），但**它们才是该当风格锚点的东西**——
            // 拿重绘产物当锚点会世代退化：模型参考自己的输出，再参考「参考自己输出的输出」，
            // 几轮之后离原稿越来越远，而每一步看着都合理。
            var referenceRoot = ReferenceRoot(repositoryRoot);
            if (Directory.Exists(referenceRoot))
            {
                foreach (var moduleDirectory in Directory.EnumerateDirectories(referenceRoot))
                {
                    var moduleName = Path.GetFileName(moduleDirectory);
                    var refsDirectory = Path.Combine(moduleDirectory, "refs");
                    if (!Directory.Exists(refsDirectory))
                    {
                        continue;
                    }

                    foreach (var filePath in Directory.EnumerateFiles(refsDirectory, "*.png", SearchOption.TopDirectoryOnly))
                    {
                        var naming = Path.GetFileNameWithoutExtension(filePath);
                        var found = byNaming.TryGetValue(naming, out var request);

                        entries.Add(new DesignLibraryEntry(
                            naming,
                            found ? request.AssetType : "",
                            moduleName,
                            ToRepositoryRelative(repositoryRoot, filePath),
                            withPalette ? ReadPalette(filePath) : Array.Empty<string>(),
                            found ? request.StyleFinal : "",
                            OriginReference));
                    }
                }
            }

            var scanRoot = ScanRoot(repositoryRoot);
            if (!Directory.Exists(scanRoot))
            {
                entries.Sort((left, right) => string.CompareOrdinal(left.Destination, right.Destination));
                return new DesignLibraryIndex(entries);
            }

            foreach (var filePath in Directory.EnumerateFiles(scanRoot, "*.png", SearchOption.AllDirectories))
            {
                var naming = Path.GetFileNameWithoutExtension(filePath);
                var module = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";

                // 直接摆在 Ui/ 根下（没有模块目录）的算无模块，如实留空——
                // 编一个模块名给它，会让「按模块查同类」查出一堆不相干的东西。
                if (string.Equals(module, "Ui", StringComparison.Ordinal))
                {
                    module = "";
                }

                // 请求里查不到就留空：**不许猜类型**——猜错的话「按类型查同类」
                // 会把不相干的东西当成风格参考发下去。
                var found = byNaming.TryGetValue(naming, out var request);

                entries.Add(new DesignLibraryEntry(
                    naming,
                    found ? request.AssetType : "",
                    module,
                    ToUnityRelative(repositoryRoot, filePath),
                    withPalette ? ReadPalette(filePath) : Array.Empty<string>(),
                    found ? request.StyleFinal : "",
                    OriginElement));
            }

            // 按落点排序：扫盘顺序随文件系统而变，不排的话「重扫无 diff」这条门禁永远红。
            entries.Sort((left, right) => string.CompareOrdinal(left.Destination, right.Destination));
            return new DesignLibraryIndex(entries);
        }

        /// <summary>
        /// 查同类：同模块、同资产类型的已有资产。给出图当风格参考图用。
        /// 通用件（Shared）**也算同类**——它们本来就是给全项目用的。
        /// </summary>
        /// <param name="moduleName">模块名。</param>
        /// <param name="assetType">资产类型；空串表示不挑类型。</param>
        /// <param name="limit">最多取几张。</param>
        public IReadOnlyList<DesignLibraryEntry> FindSimilar(string moduleName, string assetType, int limit)
        {
            var references = new List<DesignLibraryEntry>();
            var elements = new List<DesignLibraryEntry>();

            foreach (var entry in Entries)
            {
                var sameModule = string.Equals(entry.Module, moduleName, StringComparison.Ordinal)
                    || string.Equals(entry.Module, SharedModuleName, StringComparison.Ordinal);
                var sameType = assetType.Length == 0
                    || string.Equals(entry.AssetType, assetType, StringComparison.Ordinal);

                if (!sameModule || !sameType)
                {
                    continue;
                }

                if (string.Equals(entry.Origin, OriginReference, StringComparison.Ordinal))
                {
                    references.Add(entry);
                }
                else
                {
                    elements.Add(entry);
                }
            }

            // **效果图优先**：那是人点确认的原稿。取不到才退回重绘产物——
            // 拿重绘产物当锚点会世代退化，调用方该在流水里说一句。
            var matched = new List<DesignLibraryEntry>(references);
            matched.AddRange(elements);

            var capped = Math.Max(1, limit);
            return matched.Count <= capped ? matched : matched.GetRange(0, capped);
        }

        /// <summary>渲成 JSON 文本。字段顺序写死，保证重扫逐字节一样。</summary>
        public string Render()
        {
            var array = new JsonArray();
            foreach (var entry in Entries)
            {
                var palette = new JsonArray();
                foreach (var color in entry.Palette)
                {
                    palette.Add(color);
                }

                array.Add(new JsonObject
                {
                    ["命名"] = entry.Naming,
                    ["资产类型"] = entry.AssetType,
                    ["模块"] = entry.Module,
                    ["落点"] = entry.Destination,
                    ["主色"] = palette,
                    ["定稿"] = entry.StyleFinal,
                    ["产出方式"] = entry.Origin
                });
            }

            var root = new JsonObject { ["契约版本"] = "1.0.0", ["资产"] = array };
            return root.ToJsonString(WriteOptions) + "\n";
        }

        /// <summary>
        /// 写索引到磁盘。内容没变就不动文件——无谓的重写会让 git 里多出没有实质改动的 diff。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="changed">文件动没动过。</param>
        /// <param name="reason">写失败的原因。</param>
        public string Write(string repositoryRoot, out bool changed, out string reason)
        {
            changed = false;
            reason = "";
            var path = FilePathFor(repositoryRoot);
            var content = Render();

            try
            {
                if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                {
                    return path;
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, content, new UTF8Encoding(false));
                changed = true;
                return path;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = exception.Message;
                return "";
            }
        }

        /// <summary>读磁盘上那份索引；不存在给空索引（那只是还没重建过）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static DesignLibraryIndex Read(string repositoryRoot)
        {
            var entries = new List<DesignLibraryEntry>();
            var path = FilePathFor(repositoryRoot);
            if (!File.Exists(path))
            {
                return new DesignLibraryIndex(entries);
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new DesignLibraryIndex(entries);
            }

            if (node is not JsonObject root || root["资产"] is not JsonArray array)
            {
                return new DesignLibraryIndex(entries);
            }

            foreach (var item in array)
            {
                if (item is not JsonObject body)
                {
                    continue;
                }

                var palette = new List<string>();
                if (body["主色"] is JsonArray colors)
                {
                    foreach (var color in colors)
                    {
                        if (color is JsonValue value && value.TryGetValue<string>(out var hex))
                        {
                            palette.Add(hex);
                        }
                    }
                }

                entries.Add(new DesignLibraryEntry(
                    ReadString(body, "命名"),
                    ReadString(body, "资产类型"),
                    ReadString(body, "模块"),
                    ReadString(body, "落点"),
                    palette,
                    ReadString(body, "定稿"),
                    ReadString(body, "产出方式")));
            }

            return new DesignLibraryIndex(entries);
        }

        /// <summary>扫资产请求，按命名建索引，用来给条目补类型与定稿版本。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        private static Dictionary<string, (string AssetType, string StyleFinal)> ReadAssetRequests(string repositoryRoot)
        {
            var byNaming = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            var tasksDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(tasksDirectory))
            {
                return byNaming;
            }

            foreach (var requirementDirectory in Directory.EnumerateDirectories(tasksDirectory))
            {
                var requestDirectory = Path.Combine(requirementDirectory, "asset-requests");
                if (!Directory.Exists(requestDirectory))
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(requestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    JsonNode node;
                    try
                    {
                        node = JsonNode.Parse(File.ReadAllText(filePath));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                    {
                        // 读不动一份请求只是这一条少点补充信息，不该让整次重建停下。
                        continue;
                    }

                    if (node is not JsonObject root)
                    {
                        continue;
                    }

                    var naming = ReadString(root, "命名");
                    if (naming.Length == 0)
                    {
                        continue;
                    }

                    var styleFinal = root["风格锚点"] is JsonObject anchor ? ReadString(anchor, "定稿") : "";
                    byNaming[naming] = (ReadString(root, "资产类型"), styleFinal);
                }
            }

            return byNaming;
        }

        /// <summary>算一张图的主色。读不动就给空表——少几个色不该让整次重建失败。</summary>
        /// <param name="filePath">图片路径。</param>
        private static IReadOnlyList<string> ReadPalette(string filePath)
        {
            var decoded = PngDecoder.DecodeFile(filePath);
            if (!decoded.Succeeded)
            {
                return Array.Empty<string>();
            }

            var result = ColorPalette.Cluster(decoded.Image, PaletteClusterCount);
            if (!result.Clustered)
            {
                return Array.Empty<string>();
            }

            var colors = new List<string>();
            foreach (var swatch in result.Swatches)
            {
                colors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:x2}{1:x2}{2:x2}",
                    swatch.Color.Red,
                    swatch.Color.Green,
                    swatch.Color.Blue));
            }

            return colors;
        }

        /// <summary>把绝对路径缩成相对仓库根的路径。效果图住 Pools/ 下，不在 UnityProject 里。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="filePath">绝对路径。</param>
        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return filePath.Replace('\\', '/');
            }
        }

        /// <summary>把绝对路径缩成 Assets/ 起头的工程内路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="filePath">绝对路径。</param>
        private static string ToUnityRelative(string repositoryRoot, string filePath)
        {
            var unityRoot = Path.Combine(repositoryRoot, "UnityProject");
            try
            {
                return Path.GetRelativePath(unityRoot, filePath).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return filePath.Replace('\\', '/');
            }
        }

        /// <summary>读一个字符串字段；缺失给空串。</summary>
        /// <param name="holder">所在对象。</param>
        /// <param name="propertyName">字段名。</param>
        private static string ReadString(JsonObject holder, string propertyName)
        {
            return holder[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }
    }
}
