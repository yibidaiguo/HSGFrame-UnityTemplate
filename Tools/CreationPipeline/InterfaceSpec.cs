using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SystemDirectory = System.IO.Directory;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 界面规格里的一个元素。
    ///
    /// **字段刻意留成一份原始 JSON 对象**，只把机器要用的那几个抽成属性。
    /// 理由跟需求的「业务字段」同款（总纲 §二）：哪些字段必填由**元素类型模板**说了算，
    /// 那是规则数据、会随项目层增删；在这里把每个字段都写成 C# 属性，
    /// 等于把规则焊进引擎，加一类元素就要改代码。
    /// </summary>
    public sealed class InterfaceElement
    {
        /// <summary>构造一个元素。</summary>
        /// <param name="raw">这个元素的原始 JSON 对象。</param>
        public InterfaceElement(JsonObject raw)
        {
            Raw = raw ?? new JsonObject();
        }

        /// <summary>原始 JSON 对象。校验与生成都从它读，不另存一份。</summary>
        public JsonObject Raw { get; }

        /// <summary>元素 id，PascalCase；同时是 C# 标识符。</summary>
        public string Identifier
        {
            get { return ReadString("id"); }
        }

        /// <summary>人话名字。</summary>
        public string DisplayName
        {
            get { return ReadString("名称"); }
        }

        /// <summary>元素类型，决定用哪份模板：Button / Label / Image / …</summary>
        public string ElementType
        {
            get { return ReadString("类型"); }
        }

        /// <summary>父容器的元素 id；顶层为空串。</summary>
        public string ParentIdentifier
        {
            get { return ReadString("父容器"); }
        }

        /// <summary>复用档：本界面专有 / 通用。通用件出图前先查 Shared/。</summary>
        public string Reuse
        {
            get { return ReadString("复用"); }
        }

        /// <summary>是不是通用件。</summary>
        public bool IsShared
        {
            get { return string.Equals(Reuse, "通用", StringComparison.Ordinal); }
        }

        /// <summary>
        /// 同款几个。缺省 1。
        /// **只出一张图**，其余是同一资产的实例——一屏从上百个收敛到二十几个靠的就是这里。
        /// </summary>
        public int RepeatCount
        {
            get
            {
                var value = Raw["重复"];
                return value is JsonValue number && number.TryGetValue<int>(out var count) && count > 0 ? count : 1;
            }
        }

        /// <summary>布局矩形；读不出来时四个数都是 0。</summary>
        /// <param name="x">左上角 x。</param>
        /// <param name="y">左上角 y。</param>
        /// <param name="width">宽。</param>
        /// <param name="height">高。</param>
        public void ReadLayout(out int x, out int y, out int width, out int height)
        {
            x = 0;
            y = 0;
            width = 0;
            height = 0;
            if (Raw["布局"] is not JsonObject layout)
            {
                return;
            }

            ReadPair(layout["位置"], out x, out y);
            ReadPair(layout["尺寸"], out width, out height);
        }

        /// <summary>读一个两元素的数字数组。</summary>
        /// <param name="node">数组节点。</param>
        /// <param name="first">第一个数。</param>
        /// <param name="second">第二个数。</param>
        private static void ReadPair(JsonNode node, out int first, out int second)
        {
            first = 0;
            second = 0;
            if (node is not JsonArray array || array.Count < 2)
            {
                return;
            }

            if (array[0] is JsonValue a && a.TryGetValue<int>(out var x))
            {
                first = x;
            }

            if (array[1] is JsonValue b && b.TryGetValue<int>(out var y))
            {
                second = y;
            }
        }

        /// <summary>读一个字符串字段；缺失或类型不对给空串。</summary>
        /// <param name="propertyName">字段名。</param>
        public string ReadString(string propertyName)
        {
            return Raw[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }

        /// <summary>这个字段填没填。空串、空数组、空对象都算没填——填了个空壳和没填是一回事。</summary>
        /// <param name="propertyName">字段名。</param>
        public bool HasValue(string propertyName)
        {
            var node = Raw[propertyName];
            return node switch
            {
                null => false,
                JsonArray array => array.Count > 0,
                JsonObject item => item.Count > 0,
                JsonValue value => !value.TryGetValue<string>(out var text) || text.Trim().Length > 0,
                _ => true
            };
        }
    }

    /// <summary>
    /// 一份界面规格：一屏 UI 的功能契约。
    ///
    /// 它是下游三样东西的唯一来源——布局图、uidef、资产清单（子文档 08）。
    /// **依赖方向不许反过来**：从前是「拆图结果 → uidef」，那条箭头掉过来之后，
    /// 程序拿到的界面结构才是策划定的，而不是生图模型随手画出来的。
    /// </summary>
    public sealed class InterfaceSpec
    {
        /// <summary>构造一份界面规格。</summary>
        /// <param name="raw">整份规格的原始 JSON 对象。</param>
        /// <param name="filePath">来源文件路径，报错时指路用。</param>
        public InterfaceSpec(JsonObject raw, string filePath)
        {
            Raw = raw ?? new JsonObject();
            FilePath = filePath ?? "";

            var elements = new List<InterfaceElement>();
            if (Raw["元素"] is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonObject element)
                    {
                        elements.Add(new InterfaceElement(element));
                    }
                }
            }

            Elements = elements;
        }

        /// <summary>原始 JSON 对象。</summary>
        public JsonObject Raw { get; }

        /// <summary>来源文件路径。</summary>
        public string FilePath { get; }

        /// <summary>元素清单。</summary>
        public IReadOnlyList<InterfaceElement> Elements { get; }

        /// <summary>界面 id，形如 UI-0007。</summary>
        public string Identifier
        {
            get { return ReadString("id"); }
        }

        /// <summary>面板名，PascalCase；决定 uidef 名与资产模块目录。</summary>
        public string PanelName
        {
            get { return ReadString("面板"); }
        }

        /// <summary>人话标题。</summary>
        public string Title
        {
            get { return ReadString("标题"); }
        }

        /// <summary>画布宽；读不出给 0。</summary>
        public int CanvasWidth
        {
            get { return ReadCanvas("宽"); }
        }

        /// <summary>画布高；读不出给 0。</summary>
        public int CanvasHeight
        {
            get { return ReadCanvas("高"); }
        }

        /// <summary>来源需求 id 列表；一个界面会被多条需求改，所以这是数组不是单值。</summary>
        public IReadOnlyList<string> SourceRequirements
        {
            get
            {
                var result = new List<string>();
                if (Raw["来源需求"] is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        var text = item?.GetValue<string>() ?? "";
                        if (text.Length > 0)
                        {
                            result.Add(text);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 找出归属某条需求的全部界面规格。
        ///
        /// **认的是规格里的「来源需求」，不是需求里的一个指针**：一个界面会被多条需求改，
        /// 反过来一条需求也可能动好几屏，指针放需求那边就得两边同时维护，迟早对不上。
        /// 规格自己声明它为谁而生，这层关系只有一份写处。
        ///
        /// 目录不在、某一份读不动都**不算失败**——那只是「这条需求还没出功能图」，
        /// 或者某一份坏了不该连累别的；读不动的那份跳过，理由进 reasons 供调用方如实说。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="reasons">跳过了哪些、为什么；一份一条。</param>
        public static IReadOnlyList<InterfaceSpec> FindByRequirement(
            string repositoryRoot, string requirementIdentifier, out IReadOnlyList<string> reasons)
        {
            var skipped = new List<string>();
            reasons = skipped;

            var result = new List<InterfaceSpec>();
            var directory = Directory(repositoryRoot);
            if (string.IsNullOrWhiteSpace(requirementIdentifier) || !SystemDirectory.Exists(directory))
            {
                return result;
            }

            var files = SystemDirectory.GetFiles(directory, "*.json");
            Array.Sort(files, StringComparer.Ordinal);

            foreach (var file in files)
            {
                if (!TryRead(file, out var spec, out var reason))
                {
                    skipped.Add(Path.GetFileName(file) + "：" + reason);
                    continue;
                }

                foreach (var source in spec.SourceRequirements)
                {
                    if (string.Equals(source, requirementIdentifier, StringComparison.Ordinal))
                    {
                        result.Add(spec);
                        break;
                    }
                }
            }

            // 按 id 排序：生成区要幂等，文件系统的枚举顺序不是保证。
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Identifier, right.Identifier));
            return result;
        }

        /// <summary>界面规格目录：Pools/Designs/Interfaces/。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string Directory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "Interfaces");
        }

        /// <summary>某一份界面规格的文件路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">界面 id。</param>
        public static string FilePathFor(string repositoryRoot, string identifier)
        {
            return Path.Combine(Directory(repositoryRoot), identifier + ".json");
        }

        /// <summary>
        /// 读一份界面规格。读不动、不是 JSON、顶层不是对象都算失败并写清原因——
        /// **不许拿空规格顶上去**（决策 42：读不动与「没有内容」是两支）。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="spec">读成功时的规格；失败时为 null。</param>
        /// <param name="reason">失败原因，人能看懂。</param>
        public static bool TryRead(string filePath, out InterfaceSpec spec, out string reason)
        {
            spec = null;
            reason = "";

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = "界面规格读不动：" + exception.Message;
                return false;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = "界面规格不是合法 JSON：" + exception.Message;
                return false;
            }

            if (node is not JsonObject root)
            {
                reason = "界面规格的顶层不是 JSON 对象";
                return false;
            }

            spec = new InterfaceSpec(root, filePath);
            return true;
        }

        /// <summary>读一个字符串字段；缺失或类型不对给空串。</summary>
        /// <param name="propertyName">字段名。</param>
        public string ReadString(string propertyName)
        {
            return Raw[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }

        /// <summary>读画布里的一个数；读不出给 0。</summary>
        /// <param name="propertyName">宽或高。</param>
        private int ReadCanvas(string propertyName)
        {
            return Raw["画布"] is JsonObject canvas
                && canvas[propertyName] is JsonValue value
                && value.TryGetValue<int>(out var number)
                ? number
                : 0;
        }
    }
}
