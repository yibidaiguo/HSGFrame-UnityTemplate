using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 拆完图之后写一份面板定义 <c>UI/Definitions/&lt;标识名&gt;.uidef.json</c>。
    ///
    /// **写的是源，不是产物**：项目里这条链是 uidef（源）→ `ui.scaffold` → UXML/USS/C#。
    /// 拆图往源里写，UXML 照常生成——那份 UXML 就是程序侧要读的「这个界面有哪些元素、
    /// 各自用哪张图、在哪一格」，比让 AI 去读那张图既准又省。
    /// 另写一份 XML 等于开第二个事实源，两边迟早说不一样的话。
    ///
    /// 元素类型是**按层名猜的**（btn_ 开头当 Button，text_/label_ 当 Label，其余 VisualElement）。
    /// 猜错了人改一行 uidef 就行——这件事没有确定的算法，与其假装有，不如把它标成猜的。
    /// </summary>
    public static class UiPanelDefinitionWriter
    {
        /// <summary>写盘选项：缩进、中文原样。这份要给人读，也要能看 git diff。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>面板定义目录：&lt;仓库根&gt;/UI/Definitions。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string DefinitionDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "UI", "Definitions");
        }

        /// <summary>某个面板的定义文件路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="panelIdentifier">面板标识名，如 BackpackPanel。</param>
        public static string DefinitionFile(string repositoryRoot, string panelIdentifier)
        {
            return Path.Combine(DefinitionDirectory(repositoryRoot), panelIdentifier + ".uidef.json");
        }

        /// <summary>
        /// 按层清单写一份面板定义。写失败返回空串、不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="panelName">面板中文名。</param>
        /// <param name="panelIdentifier">面板标识名（C# 类名与文件名）。</param>
        /// <param name="layers">层清单（名字 + 像素框 + 贴图的工程内路径）。</param>
        public static string Write(
            string repositoryRoot,
            string panelName,
            string panelIdentifier,
            IReadOnlyList<UiPanelElement> layers)
        {
            var elements = new JsonArray();
            foreach (var layer in layers ?? Array.Empty<UiPanelElement>())
            {
                var element = new JsonObject
                {
                    ["元素名"] = layer.DisplayName,
                    ["标识名"] = layer.IdentifierName,
                    ["元素类型"] = layer.ElementType,
                    ["贴图"] = layer.TexturePath,
                    ["位置"] = new JsonObject
                    {
                        ["左"] = layer.Left,
                        ["上"] = layer.Top,
                        ["宽"] = layer.Width,
                        ["高"] = layer.Height
                    }
                };
                elements.Add(element);
            }

            var definition = new JsonObject
            {
                ["_说明"] = "由拆图那条链写出来的面板定义：一张界面设计图按元素拆成单图之后，"
                    + "每层在这里占一条。元素类型是按层名猜的，猜错了改这一行就行。"
                    + "改完跑 ui.scaffold 重新生成 UXML/USS/C#——那份 UXML 就是程序侧要读的界面层次。",
                ["面板名"] = panelName,
                ["面板标识名"] = panelIdentifier,
                ["元素清单"] = elements
            };

            try
            {
                Directory.CreateDirectory(DefinitionDirectory(repositoryRoot));
                var filePath = DefinitionFile(repositoryRoot, panelIdentifier);
                File.WriteAllText(filePath, definition.ToJsonString(WriteOptions) + Environment.NewLine, new UTF8Encoding(false));
                return filePath;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "";
            }
        }

        /// <summary>
        /// 按层名猜 UI Toolkit 控件类型。**这是猜的**，不是推导——
        /// 一个叫 btn_close 的元素多半是按钮，但没有算法能保证。猜错了改 uidef 一行。
        /// </summary>
        /// <param name="layerName">层名，如 btn_close。</param>
        public static string GuessElementType(string layerName)
        {
            var name = (layerName ?? "").ToLowerInvariant();
            if (name.StartsWith("btn_", StringComparison.Ordinal) || name.Contains("button"))
            {
                return "Button";
            }

            if (name.StartsWith("text_", StringComparison.Ordinal)
                || name.StartsWith("label_", StringComparison.Ordinal)
                || name.Contains("title"))
            {
                return "Label";
            }

            if (name.Contains("progress") || name.Contains("bar_"))
            {
                return "ProgressBar";
            }

            return "VisualElement";
        }

        /// <summary>
        /// 从层名推一个 C# 侧的标识名（Pascal）。纯装饰件（deco_ 开头）不给标识名——
        /// 留空在这套定义里是有意义的取值：不生成 C# 属性、不写 name，正是装饰件该有的样子。
        /// </summary>
        /// <param name="layerName">层名。</param>
        public static string GuessIdentifier(string layerName)
        {
            var name = (layerName ?? "").Trim();
            if (name.Length == 0 || name.StartsWith("deco", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var builder = new StringBuilder();
            var upperNext = true;
            foreach (var character in name)
            {
                if (character == '_' || character == '-' || character == ' ')
                {
                    upperNext = true;
                    continue;
                }

                if (!char.IsLetterOrDigit(character))
                {
                    continue;
                }

                builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
                upperNext = false;
            }

            var identifier = builder.ToString();
            return identifier.Length > 0 && char.IsDigit(identifier[0]) ? "Element" + identifier : identifier;
        }
    }

    /// <summary>写进面板定义的一个元素：显示名、标识名、控件类型、贴图与像素框。</summary>
    public sealed class UiPanelElement
    {
        /// <summary>构造一个元素。</summary>
        /// <param name="displayName">元素显示名。</param>
        /// <param name="identifierName">C# 侧标识名；装饰件给空串。</param>
        /// <param name="elementType">UI Toolkit 控件名。</param>
        /// <param name="texturePath">贴图的 Unity 工程内路径。</param>
        /// <param name="left">左，像素。</param>
        /// <param name="top">上，像素。</param>
        /// <param name="width">宽，像素。</param>
        /// <param name="height">高，像素。</param>
        public UiPanelElement(
            string displayName,
            string identifierName,
            string elementType,
            string texturePath,
            int left,
            int top,
            int width,
            int height)
        {
            DisplayName = displayName ?? "";
            IdentifierName = identifierName ?? "";
            ElementType = elementType ?? "VisualElement";
            TexturePath = texturePath ?? "";
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        /// <summary>元素显示名。</summary>
        public string DisplayName { get; }

        /// <summary>C# 侧标识名；装饰件为空串。</summary>
        public string IdentifierName { get; }

        /// <summary>UI Toolkit 控件名。</summary>
        public string ElementType { get; }

        /// <summary>贴图的 Unity 工程内路径。</summary>
        public string TexturePath { get; }

        /// <summary>左，像素。</summary>
        public int Left { get; }

        /// <summary>上，像素。</summary>
        public int Top { get; }

        /// <summary>宽，像素。</summary>
        public int Width { get; }

        /// <summary>高，像素。</summary>
        public int Height { get; }
    }
}
