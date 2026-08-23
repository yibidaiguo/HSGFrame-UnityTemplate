using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一份美术定稿：色板、负面清单、参考图、版本、来源。
    ///
    /// 分项目级与模块级两层，**模块级只能在项目级里收紧**（子文档 09 §三）：
    /// 色板只能是项目色板的子集，负面清单只能在项目那份上再加。
    /// 想引入新色 = 改项目级定稿，那是一次显式的、要走审查的动作——
    /// **风格跑偏几乎总是从「就这个模块特殊一下」开始的**。
    /// </summary>
    public sealed class ArtStyleFinal
    {
        /// <summary>来源：人定。</summary>
        public const string OriginHuman = "人定";

        /// <summary>来源：选片带出——人挑中的那张自动成为第一版（子文档 10 §四）。</summary>
        public const string OriginSelection = "选片带出";

        /// <summary>构造一份定稿。</summary>
        /// <param name="raw">原始 JSON 对象。</param>
        /// <param name="filePath">来源文件路径。</param>
        /// <param name="moduleName">模块名；项目级为空串。</param>
        public ArtStyleFinal(JsonObject raw, string filePath, string moduleName)
        {
            Raw = raw ?? new JsonObject();
            FilePath = filePath ?? "";
            ModuleName = moduleName ?? "";
        }

        /// <summary>原始 JSON 对象。</summary>
        public JsonObject Raw { get; }

        /// <summary>来源文件路径。</summary>
        public string FilePath { get; }

        /// <summary>模块名；项目级为空串。</summary>
        public string ModuleName { get; }

        /// <summary>是不是项目级那一份。</summary>
        public bool IsProjectLevel
        {
            get { return ModuleName.Length == 0; }
        }

        /// <summary>色板（hex 数组）。</summary>
        public IReadOnlyList<string> Palette
        {
            get { return ReadList("色板"); }
        }

        /// <summary>负面清单：明确不要什么。**这是唯一能拦住跑偏的东西**。</summary>
        public IReadOnlyList<string> NegativeList
        {
            get { return ReadList("负面清单"); }
        }

        /// <summary>参考图的相对路径。</summary>
        public IReadOnlyList<string> ReferenceImages
        {
            get { return ReadList("参考图"); }
        }

        /// <summary>
        /// 这份定稿是谁定的：人定 / 选片带出。
        /// **不许出现「机器生成」**——编出来的定稿会被往后所有资产当成事实继承，
        /// 那比没有定稿更糟（子文档 10 §四、§六「无假定稿」）。
        /// </summary>
        public string Origin
        {
            get { return ReadString("来源"); }
        }

        /// <summary>版本号；读不出给 0。</summary>
        public int Version
        {
            get { return Raw["版本"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0; }
        }

        /// <summary>定稿名，进溯源边车的「风格锚点」。</summary>
        public string Name
        {
            get { return ReadString("名称"); }
        }

        /// <summary>项目级定稿的落点：Pools/Designs/Art/project/final.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ProjectFilePath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "Art", "project", "final.json");
        }

        /// <summary>模块级定稿的落点：Pools/Designs/Art/&lt;模块&gt;/final.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名。</param>
        public static string ModuleFilePath(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "Art", moduleName, "final.json");
        }

        /// <summary>
        /// 读一份定稿。**文件不存在不算失败**——那是冷启动的入口。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="moduleName">模块名；项目级传空串。</param>
        /// <param name="final">读到的定稿；不存在时为 null。</param>
        /// <param name="reason">读不动或不是合法 JSON 的原因。</param>
        public static bool TryRead(string filePath, string moduleName, out ArtStyleFinal final, out string reason)
        {
            final = null;
            reason = "";

            if (!File.Exists(filePath))
            {
                return true;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                reason = "定稿读不动：" + exception.Message;
                return false;
            }

            if (node is not JsonObject root)
            {
                reason = "定稿的顶层不是 JSON 对象";
                return false;
            }

            final = new ArtStyleFinal(root, filePath, moduleName);
            return true;
        }

        /// <summary>
        /// 查模块级定稿有没有越出项目级：色板不许引入新色，负面清单不许删。
        /// 项目级那份传进来是 null 时不查——还没有项目级定稿时，模块级怎么写都不算越界。
        /// </summary>
        /// <param name="project">项目级定稿；可为 null。</param>
        /// <param name="module">模块级定稿。</param>
        public static IReadOnlyList<PoolFinding> InspectNarrowing(ArtStyleFinal project, ArtStyleFinal module)
        {
            var findings = new List<PoolFinding>();
            if (module == null || project == null)
            {
                return findings;
            }

            var projectPalette = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var color in project.Palette)
            {
                projectPalette.Add(color.Trim());
            }

            foreach (var color in module.Palette)
            {
                if (projectPalette.Count > 0 && !projectPalette.Contains(color.Trim()))
                {
                    findings.Add(new PoolFinding(
                        module.FilePath,
                        $"模块色板引入了项目色板之外的颜色「{color}」",
                        "模块只能在项目色板里挑，想加新色去改项目级定稿——"
                            + "风格跑偏几乎总是从「就这个模块特殊一下」开始的",
                        "Doc/creation-pipeline-subdocs/09-design-library.md"));
                }
            }

            var moduleNegative = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in module.NegativeList)
            {
                moduleNegative.Add(item.Trim());
            }

            foreach (var item in project.NegativeList)
            {
                if (!moduleNegative.Contains(item.Trim()))
                {
                    findings.Add(new PoolFinding(
                        module.FilePath,
                        $"模块负面清单丢了项目级那条「{item}」",
                        "负面清单取并集，模块只能往上加不能删——"
                            + "只让模块覆盖的话，一个模块的疏忽就能把项目级约束整条丢掉",
                        "Doc/creation-pipeline-subdocs/09-design-library.md"));
                }
            }

            return findings;
        }

        /// <summary>查这份定稿的来源合不合法：只许「人定」或「选片带出」。</summary>
        /// <param name="final">定稿。</param>
        public static IReadOnlyList<PoolFinding> InspectOrigin(ArtStyleFinal final)
        {
            var findings = new List<PoolFinding>();
            if (final == null)
            {
                return findings;
            }

            if (!string.Equals(final.Origin, OriginHuman, StringComparison.Ordinal)
                && !string.Equals(final.Origin, OriginSelection, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    final.FilePath,
                    $"定稿的「来源」是「{(final.Origin.Length == 0 ? "空" : final.Origin)}」，"
                        + $"只许「{OriginHuman}」或「{OriginSelection}」",
                    "定稿不许由机器编——编出来的会被往后所有资产当成事实继承，"
                        + "比空着更糟：空着只是这一批飘，假定稿是让整个模块照着一个谁也没同意过的方向一直走",
                    "Doc/creation-pipeline-subdocs/10-direction-and-reading.md"));
            }

            return findings;
        }

        /// <summary>读一个字符串数组；不是数组给空表。</summary>
        /// <param name="propertyName">字段名。</param>
        private IReadOnlyList<string> ReadList(string propertyName)
        {
            var items = new List<string>();
            if (Raw[propertyName] is not JsonArray array)
            {
                return items;
            }

            foreach (var item in array)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
                {
                    items.Add(text);
                }
            }

            return items;
        }

        /// <summary>读一个字符串字段；缺失给空串。</summary>
        /// <param name="propertyName">字段名。</param>
        private string ReadString(string propertyName)
        {
            return Raw[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }
    }
}
