using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一层：叫什么、在整图里的哪一块（归一化的 0~1 坐标）。</summary>
    public sealed class UiLayer
    {
        /// <summary>构造一层。</summary>
        /// <param name="name">层名，英文小写下划线。</param>
        /// <param name="left">左边界，0~1。</param>
        /// <param name="top">上边界，0~1。</param>
        /// <param name="right">右边界，0~1。</param>
        /// <param name="bottom">下边界，0~1。</param>
        public UiLayer(string name, double left, double top, double right, double bottom)
        {
            Name = name ?? "";
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        /// <summary>层名。</summary>
        public string Name { get; }

        /// <summary>左边界，0~1。</summary>
        public double Left { get; }

        /// <summary>上边界，0~1。</summary>
        public double Top { get; }

        /// <summary>右边界，0~1。</summary>
        public double Right { get; }

        /// <summary>下边界，0~1。</summary>
        public double Bottom { get; }

        /// <summary>这一块在图里是不是立得住（有面积、没越界）。</summary>
        public bool IsUsable
        {
            get
            {
                return Name.Length > 0
                    && Left >= 0 && Top >= 0 && Right <= 1 && Bottom <= 1
                    && Right - Left > 0.005 && Bottom - Top > 0.005;
            }
        }

        /// <summary>换算成整图上的像素框。</summary>
        /// <param name="width">整图宽。</param>
        /// <param name="height">整图高。</param>
        /// <param name="x">左，像素。</param>
        /// <param name="y">上，像素。</param>
        /// <param name="boxWidth">宽，像素。</param>
        /// <param name="boxHeight">高，像素。</param>
        public void ToPixels(int width, int height, out int x, out int y, out int boxWidth, out int boxHeight)
        {
            x = (int)Math.Round(Left * width);
            y = (int)Math.Round(Top * height);
            boxWidth = Math.Max(1, (int)Math.Round((Right - Left) * width));
            boxHeight = Math.Max(1, (int)Math.Round((Bottom - Top) * height));

            x = Math.Max(0, Math.Min(x, width - 1));
            y = Math.Max(0, Math.Min(y, height - 1));
            boxWidth = Math.Min(boxWidth, width - x);
            boxHeight = Math.Min(boxHeight, height - y);
        }
    }

    /// <summary>
    /// 把一张 UI 设计图按元素拆成一张张单图。
    ///
    /// **拆是裁，不是重新生成**：先出一张整屏定方向，定了再按元素切开——
    /// 每层重新生一次的话，十层就是十种画风，拼回去根本不像一个界面。
    ///
    /// 元素框由视觉模型标（这一层只负责解析它的回答与真裁），
    /// 所以**框准不准是模型的事，这里只保证「不合法的框一律不用」**：
    /// 越界、零面积、没名字的层直接丢掉并报出来，绝不裁出一张空图当成果。
    /// </summary>
    public static class UiLayerCutter
    {
        /// <summary>问视觉模型要元素框的提示词。写成常量是为了让它进提示词版本哈希。</summary>
        public const string LayerPrompt =
            "这是一张游戏 UI 界面设计图。把画面里**每一个可以单独切出来的元素**都框出来——"
            + "面板底、分区框、按钮、图标、进度条、格子、装饰件都算，"
            + "要做动效的元素也要单独框。\n"
            + "只回一份 JSON，不要解释、不要代码块，形状：\n"
            + "{\"模块\": \"Inventory\", "
            + "\"层\": [{\"名字\": \"英文小写下划线，用完整单词不用缩写，如 panel_background / button_close / icon_coin\", "
            + "\"左\": 0.0, \"上\": 0.0, \"右\": 1.0, \"下\": 1.0}]}\n"
            + "硬规矩：\n"
            + "0. 「模块」是这一屏属于哪个功能，用**英文 PascalCase**，如 Inventory / Shop / Settings / Battle。"
            + "拆出来的图会按它建目录归档，所以一屏只给一个，认不出就给 Common。\n"
            + "1. 坐标是**归一化**的 0~1（左上角是 0,0），不是像素。\n"
            + "2. 框要**贴着元素本身**，别把一大片背景framed 进来——切出来是要直接进图集的。\n"
            + "3. 同一个元素只框一次；重复出现的（比如四个一样的格子）各框各的，名字加序号。\n"
            + "4. 认不出是什么的不要硬编名字，用 deco_1 这种；**但别漏**——漏一个就少一张图。\n"
            + "5. 名字里**别用缩写**：写 button 不写 btn，写 background 不写 bg。"
            + "这些名字会变成代码里的标识符，而缩写过不了命名门禁。";

        /// <summary>
        /// 把模型给的模块名收拾成一个能当目录名的东西：只留字母数字，首字母大写。
        ///
        /// **不许原样拿来拼路径**：那是模型现编的字符串，可能带斜杠、点、中文、空格。
        /// 带斜杠的话就是往上跳目录，带中文的话 gate.pathascii 当场判红（全仓路径只许 ASCII）。
        /// 收拾完什么都不剩就给空串，调用方退回不分模块的落点——
        /// 宁可少一层目录，也不许写到一个算不准的地方去。
        /// </summary>
        /// <param name="rawModule">模型给的原始模块名。</param>
        public static string SafeModuleName(string rawModule)
        {
            var builder = new StringBuilder();
            var atWordStart = true;
            foreach (var character in rawModule ?? "")
            {
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9'))
                {
                    builder.Append(atWordStart ? char.ToUpperInvariant(character) : character);
                    atWordStart = false;
                }
                else
                {
                    atWordStart = true;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 组一份「重拆」的提示词：把上一次的框原样摆出来，加上人的意见，让模型在此基础上改。
        ///
        /// **给它看上一次的框，而不是从头再标一遍**：从头标等于把已经标对的那些也重掷一次骰子，
        /// 人明明只说了「关闭按钮框大了」，结果整套框全变——那不是「改」，是「重来」。
        /// </summary>
        /// <param name="previousLayers">上一次的层清单。</param>
        /// <param name="feedback">人这次说的意见。</param>
        public static string BuildRecutPrompt(IReadOnlyList<UiLayer> previousLayers, string feedback)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(LayerPrompt);
            builder.Append("\n\n上一次你是这么框的：\n");

            foreach (var layer in previousLayers ?? Array.Empty<UiLayer>())
            {
                builder.Append("· ").Append(layer.Name)
                    .Append("：左 ").Append(layer.Left.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append("，上 ").Append(layer.Top.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append("，右 ").Append(layer.Right.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append("，下 ").Append(layer.Bottom.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            builder.Append("\n人看完说：").Append(feedback ?? "").Append('\n');
            builder.Append("**在上一次的基础上改**：他没提到的那些层原样保留（名字与框都别动），"
                + "只动他说的那几处；说漏了就补一层，说多了就删掉那一层。"
                + "照旧只回那份 JSON。");
            return builder.ToString();
        }

        /// <summary>
        /// 解析视觉模型的回答，取出层清单。
        /// 解析不出来给空表**并带上原因**——空表与「读不懂」是两支，
        /// 合并的话人只会看到「没拆出东西」，查不到是模型没回还是回了个读不懂的形状。
        /// </summary>
        /// <param name="modelText">模型回答原文。</param>
        /// <param name="failureReason">解析失败原因；成功时为空串。</param>
        public static IReadOnlyList<UiLayer> ParseLayers(string modelText, out string failureReason)
        {
            return ParseLayers(modelText, out failureReason, out _);
        }

        /// <summary>
        /// 同上，另外读出模型给的模块名（拆出来的图按它建目录归档）。
        /// </summary>
        /// <param name="modelText">视觉模型的原文。</param>
        /// <param name="failureReason">读不出层时的原因。</param>
        /// <param name="moduleName">模块名；模型没给、或给了不能当目录名的东西时为空串。</param>
        public static IReadOnlyList<UiLayer> ParseLayers(
            string modelText, out string failureReason, out string moduleName)
        {
            failureReason = "";
            moduleName = "";
            var layers = new List<UiLayer>();

            if (string.IsNullOrWhiteSpace(modelText))
            {
                failureReason = "视觉模型回了空文本";
                return layers;
            }

            var json = ExtractJsonObject(modelText);
            if (json.Length == 0)
            {
                failureReason = "回答里找不到一份 JSON 对象（原文前 200 字：" + Preview(modelText) + "）";
                return layers;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException exception)
            {
                failureReason = "回答里那段 JSON 解析失败：" + exception.Message;
                return layers;
            }

            if (node is not JsonObject root || root["层"] is not JsonArray array)
            {
                failureReason = "回答里没有「层」数组";
                return layers;
            }

            moduleName = SafeModuleName(root["模块"] is JsonValue moduleValue
                && moduleValue.TryGetValue<string>(out var rawModule) ? rawModule : "");

            foreach (var item in array)
            {
                if (item is not JsonObject layerObject)
                {
                    continue;
                }

                layers.Add(new UiLayer(
                    ReadString(layerObject, "名字"),
                    ReadNumber(layerObject, "左"),
                    ReadNumber(layerObject, "上"),
                    ReadNumber(layerObject, "右"),
                    ReadNumber(layerObject, "下")));
            }

            if (layers.Count == 0)
            {
                failureReason = "「层」数组是空的，一个元素都没框出来";
            }

            return layers;
        }

        /// <summary>
        /// 从整图里裁出一层。坐标不合法时返回 null——**不许裁出一张空图当成果**。
        /// </summary>
        /// <param name="source">整图。</param>
        /// <param name="layer">这一层。</param>
        public static PngImage Cut(PngImage source, UiLayer layer)
        {
            if (source == null || layer == null || !layer.IsUsable)
            {
                return null;
            }

            layer.ToPixels(source.Width, source.Height, out var x, out var y, out var width, out var height);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
            {
                var sourceOffset = (((y + row) * source.Width) + x) * 4;
                var targetOffset = row * width * 4;
                for (var index = 0; index < width * 4; index++)
                {
                    pixels[targetOffset + index] = source.Pixels[sourceOffset + index];
                }
            }

            return new PngImage(width, height, pixels);
        }

        /// <summary>读对象里的字符串键；缺失给空串。</summary>
        private static string ReadString(JsonObject node, string key)
        {
            return node.TryGetPropertyValue(key, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        /// <summary>读对象里的数字键；缺失或不是数字给 -1（那会让这一层判成不可用）。</summary>
        private static double ReadNumber(JsonObject node, string key)
        {
            if (!node.TryGetPropertyValue(key, out var value) || value is not JsonValue jsonValue)
            {
                return -1;
            }

            if (jsonValue.TryGetValue<double>(out var number))
            {
                return number;
            }

            return jsonValue.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;
        }

        /// <summary>从一段文本里抠出第一个花括号配平的 JSON 对象；抠不到给空串。</summary>
        private static string ExtractJsonObject(string text)
        {
            var depth = 0;
            var start = -1;
            var inString = false;
            var escaped = false;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }

                if (character == '{')
                {
                    if (depth == 0)
                    {
                        start = index;
                    }

                    depth++;
                    continue;
                }

                if (character == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        return text.Substring(start, index - start + 1);
                    }
                }
            }

            return "";
        }

        /// <summary>原文前 200 字，用在报错里。</summary>
        private static string Preview(string text)
        {
            var single = text.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 200 ? single : single.Substring(0, 200) + "…";
        }
    }
}
