using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 界面规格的判定逻辑：面板级必填、元素 id 唯一与合规、父子无环、按类型模板查必填、
    /// 「失败」必须是数组、「验收」必须写。
    ///
    /// 这一层拦的东西跟别处不同：**它拦的是「还没想清楚」**。
    /// 写不出可测的验收断言、把三种失败合成一句话——这些不是格式问题，
    /// 是需求本身还没定，而它们一路溜到下游就会变成「程序照着写出来三种失败一个提示框」。
    /// </summary>
    public static class InterfaceSpecInspector
    {
        /// <summary>
        /// 元素 id 会**原样变成 C# 标识符**，所以命名门禁的缩写黑名单在这儿就得拦。
        /// 拖到生成三件套之后再红的话，报错指向的是一份生成物，人得倒推半天才找到源头。
        /// </summary>
        private static readonly string[] ForbiddenAbbreviations =
        {
            "Btn", "Bg", "Img", "Txt", "Lbl", "Mgr", "Cfg", "Idx", "Tmp", "Ctx"
        };

        /// <summary>
        /// 查一份界面规格。
        /// </summary>
        /// <param name="spec">界面规格。</param>
        /// <param name="catalog">元素类型模板目录。</param>
        public static IReadOnlyList<PoolFinding> Inspect(InterfaceSpec spec, UiElementTemplateCatalog catalog)
        {
            var findings = new List<PoolFinding>();
            if (spec == null)
            {
                return findings;
            }

            findings.AddRange(catalog?.Findings ?? Array.Empty<PoolFinding>());
            var where = spec.FilePath;

            foreach (var field in new[] { "id", "面板", "标题", "状态" })
            {
                if (spec.ReadString(field).Length == 0)
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"界面规格缺「{field}」",
                        $"补上「{field}」",
                        "Pools/Schema/Baseline/interface-spec.schema.json"));
                }
            }

            if (spec.CanvasWidth <= 0 || spec.CanvasHeight <= 0)
            {
                findings.Add(new PoolFinding(
                    where,
                    "画布尺寸缺失或非正数",
                    "补上「画布」的宽与高；尺寸取资产规格数据，不由 AI 即兴",
                    "Specifications/Baseline/asset-spec.baseline.json"));
            }

            if (spec.Elements.Count == 0)
            {
                findings.Add(new PoolFinding(
                    where,
                    "界面规格里一个元素都没有",
                    "至少写一个元素——没有元素清单，下游的布局图、uidef、资产清单都无从谈起",
                    "Doc/creation-pipeline-subdocs/08-interface-spec.md"));
                return findings;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in spec.Elements)
            {
                InspectElement(spec, element, catalog, seen, findings);
            }

            InspectParentChain(spec, findings);
            return findings;
        }

        /// <summary>逐条查一个元素。</summary>
        /// <param name="spec">所属界面规格。</param>
        /// <param name="element">元素。</param>
        /// <param name="catalog">模板目录。</param>
        /// <param name="seen">已见过的 id。</param>
        /// <param name="findings">发现表。</param>
        private static void InspectElement(
            InterfaceSpec spec,
            InterfaceElement element,
            UiElementTemplateCatalog catalog,
            HashSet<string> seen,
            List<PoolFinding> findings)
        {
            var where = spec.FilePath;
            var identifier = element.Identifier;
            var label = identifier.Length > 0 ? identifier : "（没有 id 的一条）";

            if (identifier.Length == 0)
            {
                findings.Add(new PoolFinding(
                    where, "有元素没写 id", "给它一个 PascalCase 的 id", "Pools/Schema/Baseline/interface-spec.schema.json"));
            }
            else if (!seen.Add(identifier))
            {
                findings.Add(new PoolFinding(
                    where,
                    $"元素 id「{identifier}」重复",
                    "id 要唯一——重复的话 uidef 里两个元素会撞成一个",
                    "Pools/Schema/Baseline/interface-spec.schema.json"));
            }

            foreach (var abbreviation in ForbiddenAbbreviations)
            {
                if (identifier.Contains(abbreviation, StringComparison.Ordinal))
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"元素 id「{identifier}」含缩写「{abbreviation}」",
                        $"换成完整单词（{abbreviation} → 写全）——这个 id 会原样变成 C# 标识符，缩写过不了命名门禁",
                        "Tools/Gates/Config/gate-config.json"));
                    break;
                }
            }

            var elementType = element.ElementType;
            var template = catalog?.Find(elementType);
            if (template == null)
            {
                findings.Add(new PoolFinding(
                    where,
                    $"元素「{label}」的类型「{(elementType.Length == 0 ? "空" : elementType)}」没有对应的模板",
                    "在 ui-element-template 里加这一类，或改成已有类型；不给「通用模板」兜底——"
                        + "兜底等于默许任何拼错的类型名一路通过",
                    "Specifications/Baseline/ui-element-template.baseline.json"));
                return;
            }

            var required = new List<string>(catalog.CommonRequiredFields);
            required.AddRange(template.RequiredFields);

            foreach (var field in required)
            {
                if (string.Equals(field, "id", StringComparison.Ordinal)
                    || string.Equals(field, "类型", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(field, FailureField, StringComparison.Ordinal))
                {
                    InspectFailureField(element, label, elementType, where, findings);
                    continue;
                }

                if (!element.HasValue(field))
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"元素「{label}」（{elementType}）缺必填字段「{field}」",
                        $"按 {elementType} 的模板补上「{field}」",
                        "Specifications/Baseline/ui-element-template.baseline.json"));
                }
            }
        }

        /// <summary>「失败」这一格的取值前缀：一句以它开头的话表示「查过了，这个件不会失败」。</summary>
        public const string NoFailurePrefix = "不会失败";

        /// <summary>「失败」这一格的字段名。</summary>
        private const string FailureField = "失败";

        /// <summary>
        /// 查「失败」这一格。它有两种合法形状，**空数组不在其中**。
        ///
        /// ① **数组，一种失败一条** `{条件, 提示, 处置}`。写成一句话是最常见也最贵的偷懒：
        ///    背包满了 / 钱不够 / 网络断了，三条的文案与处置完全不同，合成一句等于没写，
        ///    而程序照着这句写出来的就是三种失败一个提示框。
        ///
        /// ② **一句以「不会失败」开头、后面跟理由的话**。有些件真的不会失败——
        ///    纯本地的列表选中就没有可失败的一步，逼它编一条假失败比空着更坏。
        ///
        /// **空数组两种都不算**：它分不清「还没写」与「查过了，确实没有」，
        /// 而这两件事对程序的意思完全相反（决策 42 那一类）。要说没有，就把理由说出来。
        /// </summary>
        /// <param name="element">元素。</param>
        /// <param name="label">元素的人话名字，报错时指给人看。</param>
        /// <param name="elementType">元素类型。</param>
        /// <param name="where">位置。</param>
        /// <param name="findings">发现表。</param>
        private static void InspectFailureField(
            InterfaceElement element,
            string label,
            string elementType,
            string where,
            List<PoolFinding> findings)
        {
            var raw = element.Raw[FailureField];

            if (raw is JsonArray array)
            {
                if (array.Count == 0)
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"元素「{label}」（{elementType}）的「失败」是个空数组",
                        $"这个件真的不会失败的话，写成一句「{NoFailurePrefix}：<为什么>」；"
                            + "会失败就一种一条 {条件, 提示, 处置}。"
                            + "空数组分不清「还没写」与「查过了没有」，而这两件事对程序的意思完全相反",
                        "Pools/Schema/Baseline/interface-spec.schema.json"));
                }

                return;
            }

            if (raw is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var trimmed = (text ?? "").Trim();
                if (!trimmed.StartsWith(NoFailurePrefix, StringComparison.Ordinal))
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"元素「{label}」的「失败」是一句话，但不是「{NoFailurePrefix}」那一种",
                        "会失败就写成数组，一种一条 {条件, 提示, 处置}——合成一句等于没写；"
                            + $"真不会失败就写「{NoFailurePrefix}：<为什么>」",
                        "Pools/Schema/Baseline/interface-spec.schema.json"));
                    return;
                }

                // 只写「不会失败」三个字不算——**理由才是这一格的价值**：
                // 往后有人要加失败分支时，靠它判断当初是想过还是漏了。
                if (trimmed.Length <= NoFailurePrefix.Length + 1)
                {
                    findings.Add(new PoolFinding(
                        where,
                        $"元素「{label}」写了「{NoFailurePrefix}」但没给理由",
                        $"写成「{NoFailurePrefix}：<为什么>」。往后有人要加失败分支时，"
                            + "靠这句话判断当初是想过还是漏了",
                        "Pools/Schema/Baseline/interface-spec.schema.json"));
                }

                return;
            }

            findings.Add(new PoolFinding(
                where,
                $"元素「{label}」（{elementType}）缺必填字段「{FailureField}」",
                $"会失败就写成数组，一种一条 {{条件, 提示, 处置}}；"
                    + $"真不会失败就写「{NoFailurePrefix}：<为什么>」",
                "Specifications/Baseline/ui-element-template.baseline.json"));
        }

        /// <summary>父容器必须存在，且不许成环。</summary>
        /// <param name="spec">界面规格。</param>
        /// <param name="findings">发现表。</param>
        private static void InspectParentChain(InterfaceSpec spec, List<PoolFinding> findings)
        {
            var byIdentifier = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var element in spec.Elements)
            {
                if (element.Identifier.Length > 0)
                {
                    byIdentifier[element.Identifier] = element.ParentIdentifier;
                }
            }

            foreach (var element in spec.Elements)
            {
                var parent = element.ParentIdentifier;
                if (parent.Length == 0)
                {
                    continue;
                }

                if (!byIdentifier.ContainsKey(parent))
                {
                    findings.Add(new PoolFinding(
                        spec.FilePath,
                        $"元素「{element.Identifier}」的父容器「{parent}」不存在",
                        "改成已有的元素 id，或把父容器留空表示顶层",
                        "Pools/Schema/Baseline/interface-spec.schema.json"));
                    continue;
                }

                // 顺着父链往上走，走回自己就是成环。链长以元素总数封顶——
                // 不封顶的话一个环就会把这里转成死循环。
                var current = parent;
                for (var step = 0; step <= spec.Elements.Count; step++)
                {
                    if (string.Equals(current, element.Identifier, StringComparison.Ordinal))
                    {
                        findings.Add(new PoolFinding(
                            spec.FilePath,
                            $"元素「{element.Identifier}」的父容器链成环",
                            "把环拆开——成环的话生成 UXML 时会无限递归",
                            "Pools/Schema/Baseline/interface-spec.schema.json"));
                        break;
                    }

                    if (current.Length == 0 || !byIdentifier.TryGetValue(current, out current))
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 扫 <c>Pools/Designs/Interfaces/</c> 下全部界面规格；目录不存在时返回空表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名，留空表示只用基线与项目两层。</param>
        public static IReadOnlyList<PoolFinding> InspectAll(string repositoryRoot, string moduleName)
        {
            var findings = new List<PoolFinding>();
            var directory = InterfaceSpec.Directory(repositoryRoot);
            if (!Directory.Exists(directory))
            {
                return findings;
            }

            var catalog = UiElementTemplateCatalog.Load(repositoryRoot, moduleName ?? "");
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (!InterfaceSpec.TryRead(filePath, out var spec, out var reason))
                {
                    findings.Add(new PoolFinding(
                        filePath, reason, "把文件修好", "Pools/Schema/Baseline/interface-spec.schema.json"));
                    continue;
                }

                findings.AddRange(Inspect(spec, catalog));
            }

            return findings;
        }
    }
}
