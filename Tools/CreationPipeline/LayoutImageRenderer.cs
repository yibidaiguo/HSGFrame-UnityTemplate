using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把界面规格渲成一张白块布局图（SVG）。
    ///
    /// **确定性渲染，不由模型画。** 模型画的图不可复现、改一处要重画全图，
    /// 而且图上画的与规格里写的会漂——拆图那块已经吃过这个亏
    /// （从图上猜元素，猜出一屏一百多个还跟需求对不上）。
    /// 确定性渲染让「布局图与规格一致」成为**结构保证**而不是纪律：
    /// 改规格即改图，重跑无 diff，可以进幂等门禁。
    ///
    /// 它**不追求好看**——追求好看的是美术稿，那是下一步的事。
    /// 这张图只回答两个问题：功能位齐不齐（给策划看）、大致摆哪（给美术当底稿）。
    ///
    /// 选 SVG 不选 PNG：SVG 是文本，能被 git diff 看见（铁律 2），
    /// 改了哪个块一目了然；PNG 只能看出「变了」。
    /// </summary>
    public static class LayoutImageRenderer
    {
        /// <summary>不同元素类型的填色。刻意都用浅灰阶——这是白块图，不是配色稿。</summary>
        private static readonly Dictionary<string, string> FillByType = new(StringComparer.Ordinal)
        {
            ["Background"] = "#f2f2f2",
            ["Container"] = "#e8e8e8",
            ["Button"] = "#d8d8d8",
            ["Toggle"] = "#dcdcdc",
            ["Image"] = "#e0e0e0",
            ["ProgressBar"] = "#e4e4e4",
            ["Label"] = "#ffffff",
            ["Decoration"] = "#f6f6f6"
        };

        /// <summary>没登记的类型用这个色，不报错——布局图的职责不是校验。</summary>
        private const string DefaultFill = "#eeeeee";

        /// <summary>布局图的落点：_Generated/Interfaces/&lt;id&gt;.layout.svg。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">界面 id。</param>
        public static string OutputPath(string repositoryRoot, string identifier)
        {
            return Path.Combine(repositoryRoot, "_Generated", "Interfaces", identifier + ".layout.svg");
        }

        /// <summary>
        /// 渲成 SVG 文本。
        ///
        /// 元素**按父子深度排序**再画：父在前、子在后，后画的盖在前面画的上面。
        /// 不排的话，一个铺满全屏的底图写在清单最后就会把所有元素盖住。
        /// </summary>
        /// <param name="spec">界面规格。</param>
        public static string Render(InterfaceSpec spec)
        {
            if (spec == null)
            {
                return "";
            }

            var width = spec.CanvasWidth > 0 ? spec.CanvasWidth : 1920;
            var height = spec.CanvasHeight > 0 ? spec.CanvasHeight : 1080;

            var builder = new StringBuilder();
            builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
                .Append(Number(width)).Append(' ').Append(Number(height))
                .Append("\" width=\"").Append(Number(width))
                .Append("\" height=\"").Append(Number(height)).Append("\">\n");

            builder.Append("  <title>").Append(Escape(spec.Identifier))
                .Append(' ').Append(Escape(spec.Title)).Append("</title>\n");
            builder.Append("  <rect x=\"0\" y=\"0\" width=\"").Append(Number(width))
                .Append("\" height=\"").Append(Number(height))
                .Append("\" fill=\"#ffffff\" stroke=\"#bbbbbb\" stroke-width=\"2\"/>\n");

            foreach (var element in SortByDepth(spec))
            {
                AppendElement(builder, element);
            }

            builder.Append("</svg>\n");
            return builder.ToString();
        }

        /// <summary>
        /// 写一份布局图到磁盘。**写之前先比对**：内容没变就不动文件——
        /// 幂等门禁比的是内容，而无谓的重写会让 git 里多出一堆没有实质改动的 diff。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="spec">界面规格。</param>
        /// <param name="changed">文件动没动过。</param>
        /// <param name="reason">写失败的原因；成功为空串。</param>
        public static string Write(string repositoryRoot, InterfaceSpec spec, out bool changed, out string reason)
        {
            changed = false;
            reason = "";
            if (spec == null || spec.Identifier.Length == 0)
            {
                reason = "界面规格没有 id，不知道写到哪";
                return "";
            }

            var path = OutputPath(repositoryRoot, spec.Identifier);
            var content = Render(spec);

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

        /// <summary>按父子深度排序：父在前、子在后，后画的盖在前面画的上面。</summary>
        /// <param name="spec">界面规格。</param>
        private static IReadOnlyList<InterfaceElement> SortByDepth(InterfaceSpec spec)
        {
            var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var element in spec.Elements)
            {
                if (element.Identifier.Length > 0)
                {
                    parentOf[element.Identifier] = element.ParentIdentifier;
                }
            }

            var ordered = new List<(int Depth, int Index, InterfaceElement Element)>();
            for (var index = 0; index < spec.Elements.Count; index++)
            {
                ordered.Add((Depth(parentOf, spec.Elements[index], spec.Elements.Count), index, spec.Elements[index]));
            }

            // 同深度的保持原顺序（拿原下标当次键）：稳定排序才谈得上幂等。
            ordered.Sort((left, right) =>
                left.Depth != right.Depth ? left.Depth.CompareTo(right.Depth) : left.Index.CompareTo(right.Index));

            var result = new List<InterfaceElement>();
            foreach (var item in ordered)
            {
                result.Add(item.Element);
            }

            return result;
        }

        /// <summary>算一个元素的父子深度。链长以元素总数封顶——成环时不至于转成死循环。</summary>
        /// <param name="parentOf">元素 id → 父 id。</param>
        /// <param name="element">元素。</param>
        /// <param name="limit">链长上限。</param>
        private static int Depth(IReadOnlyDictionary<string, string> parentOf, InterfaceElement element, int limit)
        {
            var depth = 0;
            var current = element.ParentIdentifier;
            while (current.Length > 0 && depth <= limit && parentOf.TryGetValue(current, out var next))
            {
                depth++;
                current = next;
            }

            return depth;
        }

        /// <summary>写一个元素的白块与标注。</summary>
        /// <param name="builder">输出缓冲。</param>
        /// <param name="element">元素。</param>
        private static void AppendElement(StringBuilder builder, InterfaceElement element)
        {
            element.ReadLayout(out var x, out var y, out var width, out var height);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var fill = FillByType.TryGetValue(element.ElementType, out var known) ? known : DefaultFill;

            builder.Append("  <g>\n");
            builder.Append("    <rect x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(y))
                .Append("\" width=\"").Append(Number(width)).Append("\" height=\"").Append(Number(height))
                .Append("\" fill=\"").Append(fill).Append("\" stroke=\"#888888\" stroke-width=\"2\"/>\n");

            // 标注写 id 与类型，不写人话名字：这张图是给程序与美术对齐用的，
            // id 才是双方共同认的那个东西。重复件顺带标 ×N，免得看图的人以为漏画了。
            var caption = element.Identifier + "  " + element.ElementType
                + (element.RepeatCount > 1 ? "  ×" + element.RepeatCount : "")
                + (element.IsShared ? "  [通用]" : "");

            var fontSize = Math.Max(12, Math.Min(24, height / 3));
            builder.Append("    <text x=\"").Append(Number(x + 8)).Append("\" y=\"").Append(Number(y + fontSize + 4))
                .Append("\" font-family=\"monospace\" font-size=\"").Append(Number(fontSize))
                .Append("\" fill=\"#333333\">").Append(Escape(caption)).Append("</text>\n");
            builder.Append("  </g>\n");
        }

        /// <summary>数字按不变文化输出——跟着机器区域设置走的话，小数点会变成逗号，SVG 当场坏掉。</summary>
        /// <param name="value">数值。</param>
        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>转义 XML 里有特殊含义的五个字符。</summary>
        /// <param name="text">原文。</param>
        private static string Escape(string text)
        {
            return (text ?? "")
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
        }
    }
}
