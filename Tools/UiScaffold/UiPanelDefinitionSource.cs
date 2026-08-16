using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Template.Toolkit.UiScaffold
{
    /// <summary>面板定义的轻量模型，对应 uidef.json 的顶层字段。</summary>
    public sealed class UiPanelDefinitionSource
    {
        /// <summary>面板显示名，例如「主界面」。</summary>
        [JsonPropertyName("面板名")]
        public string PanelName { get; set; }

        /// <summary>面板标识名，用作文件名与 C# 类名，例如 MainPanel。</summary>
        [JsonPropertyName("面板标识名")]
        public string PanelIdentifierName { get; set; }

        // 层名直接写进生成的 C#（PanelLayer.<层名>），所以取值必须是 HSGFrame.UiFramework.PanelLayer
        // 上真有的成员名：Hud / Normal / Dialog / Tip / Loading。写错了会在 Unity 编译那一级报出来。
        /// <summary>面板所在的层名，缺省是 Normal。</summary>
        [JsonPropertyName("面板层")]
        public string PanelLayer
        {
            // 空白一律归成 Normal：字段缺失、写成 null、写成空串三种情形在生成物里表现完全一样
            //（都会渲染出 `PanelLayer.` 这种编不过的残句），在这里一次性收口比在模板里判省事。
            get => string.IsNullOrWhiteSpace(_panelLayer) ? "Normal" : _panelLayer;
            set => _panelLayer = value;
        }

        private string _panelLayer;

        /// <summary>面板根元素额外挂的样式类，`panel-root` 恒在，这里写的追加在后面。</summary>
        [JsonPropertyName("根样式类")]
        public List<string> RootStyleClasses { get; set; } = new List<string>();

        /// <summary>面板包含的元素清单，元素自身可以再带子元素。</summary>
        [JsonPropertyName("元素清单")]
        public List<UiElementSource> Elements { get; set; } = new List<UiElementSource>();

        /// <summary>根元素的属性串，供 UXML 模板直接贴在标签名后面。</summary>
        [JsonIgnore]
        public string RootAttributeMarkup =>
            UiMarkupWriter.AttributeMarkup(PanelIdentifierName, RootStyleClasses.Prepend("panel-root"), null);

        /// <summary>整棵元素树摊平后的 UXML 正文行，缩进已经算好。</summary>
        [JsonIgnore]
        public IReadOnlyList<string> MarkupLines => UiMarkupWriter.BodyLines(Elements);

        /// <summary>整棵树里所有带标识名的元素，摊平成一维——C# 侧按标识名 Q 取，与层级无关。</summary>
        [JsonIgnore]
        public IReadOnlyList<UiElementSource> BindableElements =>
            Flatten(Elements).Where(element => !string.IsNullOrWhiteSpace(element.IdentifierName)).ToList();

        /// <summary>本面板用到的全部样式类名，去重后排序，供 USS 生成占位规则。</summary>
        [JsonIgnore]
        public IReadOnlyList<string> StyleClassNames =>
            RootStyleClasses
                .Concat(Flatten(Elements).SelectMany(element => element.StyleClasses))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        /// <summary>校验定义自身，返回四要素格式的问题描述；空列表表示可以生成。</summary>
        /// <remarks>
        /// 重名的标识名会生成出重复的 C# 属性——那是编译期才炸的错，而且报在生成物上、
        /// 看不出根因在定义里。在这里拦住，报出来的位置才是作者能改的地方。
        /// </remarks>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(PanelIdentifierName))
            {
                problems.Add("位置：面板定义；原因：缺「面板标识名」，它要当 C# 类名与文件名；修复：补上一个英文标识名；参考：UI/Definitions/主界面.uidef.json");
            }

            foreach (var element in Flatten(Elements))
            {
                if (string.IsNullOrWhiteSpace(element.ElementType))
                {
                    problems.Add($"位置：元素「{element.ElementName}」；原因：缺「元素类型」，UXML 标签名无从生成；修复：填一个 UI Toolkit 控件名，如 Label / Button / VisualElement；参考：UI/Definitions/主界面.uidef.json");
                }
            }

            var duplicates = BindableElements
                .GroupBy(element => element.IdentifierName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            foreach (var duplicate in duplicates)
            {
                problems.Add($"位置：元素「{duplicate.Key}」；原因：标识名在本面板里出现了 {duplicate.Count()} 次，生成的 C# 会有重复属性；修复：改成互不相同的标识名，纯布局容器可以留空标识名；参考：UI/Definitions/主界面.uidef.json");
            }

            return problems;
        }

        private static IEnumerable<UiElementSource> Flatten(IEnumerable<UiElementSource> elements)
        {
            foreach (var element in elements ?? Enumerable.Empty<UiElementSource>())
            {
                if (element == null)
                {
                    continue;
                }

                yield return element;

                foreach (var child in Flatten(element.Children))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>面板单个元素的轻量模型，对应 uidef.json 元素清单里的每一项。</summary>
    public sealed class UiElementSource
    {
        /// <summary>元素显示名，例如「血条」。</summary>
        [JsonPropertyName("元素名")]
        public string ElementName { get; set; }

        // 留空是有意义的取值，不是漏填：纯布局容器（一行、一列、一块背景）不需要 C# 侧句柄，
        // 给它取名反而逼作者为每个盒子编一个用不上的英文名。留空即「不生成属性、不写 name」。
        /// <summary>元素标识名，例如 HealthBar；留空表示纯布局容器，不生成 C# 属性。</summary>
        [JsonPropertyName("标识名")]
        public string IdentifierName { get; set; }

        /// <summary>元素类型，对应 UI Toolkit 控件名，例如 ProgressBar。</summary>
        [JsonPropertyName("元素类型")]
        public string ElementType { get; set; }

        /// <summary>元素的初始文本，只对 Label / Button 这类带 text 特性的控件有意义。</summary>
        [JsonPropertyName("文本")]
        public string Text { get; set; }

        /// <summary>元素挂的样式类名，写进 UXML 的 class 特性，样式本身在 USS 里写。</summary>
        [JsonPropertyName("样式类")]
        public List<string> StyleClasses { get; set; } = new List<string>();

        /// <summary>子元素清单，可以继续嵌套。</summary>
        [JsonPropertyName("子元素")]
        public List<UiElementSource> Children { get; set; } = new List<UiElementSource>();

        /// <summary>本元素的属性串，供 UXML 模板直接贴在标签名后面。</summary>
        [JsonIgnore]
        public string AttributeMarkup => UiMarkupWriter.AttributeMarkup(IdentifierName, StyleClasses, Text);
    }

    /// <summary>把元素树写成 UXML 片段：属性串拼装、转义与缩进都收在这里。</summary>
    /// <remarks>
    /// 标记生成放 C# 不放 scriban，是因为转义与递归缩进正是模板语言最容易写错的两件事，
    /// 而写错的结果是一份看着正常、Unity 打开时才报错的 UXML。模板仍然拥有文档骨架
    /// （xmlns 声明与根元素包装），只是不再负责逐节点递归。
    /// </remarks>
    public static class UiMarkupWriter
    {
        private const int IndentWidth = 4;
        private const int BodyIndentLevel = 2;

        /// <summary>拼一个元素的属性串，形如 ` name="X" class="a b" text="Y"`，无属性时返回空串。</summary>
        /// <param name="identifierName">元素标识名，留空则不写 name。</param>
        /// <param name="styleClasses">样式类名清单，全空则不写 class。</param>
        /// <param name="text">初始文本，留空则不写 text。</param>
        public static string AttributeMarkup(string identifierName, IEnumerable<string> styleClasses, string text)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(identifierName))
            {
                builder.Append($" name=\"{Escape(identifierName)}\"");
            }

            var classNames = (styleClasses ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            if (classNames.Count > 0)
            {
                builder.Append($" class=\"{Escape(string.Join(" ", classNames))}\"");
            }

            if (!string.IsNullOrEmpty(text))
            {
                builder.Append($" text=\"{Escape(text)}\"");
            }

            return builder.ToString();
        }

        /// <summary>把元素树渲染成 UXML 正文行，每行的缩进已经算好，不含根元素与 xmlns 声明。</summary>
        /// <param name="elements">顶层元素清单。</param>
        public static IReadOnlyList<string> BodyLines(IEnumerable<UiElementSource> elements)
        {
            var lines = new List<string>();
            AppendElements(lines, elements, BodyIndentLevel);
            return lines;
        }

        private static void AppendElements(List<string> lines, IEnumerable<UiElementSource> elements, int depth)
        {
            foreach (var element in elements ?? Enumerable.Empty<UiElementSource>())
            {
                if (element == null)
                {
                    continue;
                }

                var indent = new string(' ', depth * IndentWidth);
                var tag = element.ElementType;
                var children = element.Children ?? new List<UiElementSource>();

                if (children.Count == 0)
                {
                    lines.Add($"{indent}<ui:{tag}{element.AttributeMarkup} />");
                    continue;
                }

                lines.Add($"{indent}<ui:{tag}{element.AttributeMarkup}>");
                AppendElements(lines, children, depth + 1);
                lines.Add($"{indent}</ui:{tag}>");
            }
        }

        private static string Escape(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
