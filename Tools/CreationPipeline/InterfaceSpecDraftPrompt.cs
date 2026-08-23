using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 组「从需求产一份界面规格草案」的提示词，并把模型的回答解析回来。
    ///
    /// **这一层只管组和解，不管调**：调执行后端要走桥，那是命令层的事。
    /// 分开之后这一整块可以被单测钉住——提示词里少写一句、模型多包一层代码块，
    /// 都能在不花一分钱的情况下测出来。
    ///
    /// 读什么按三档策略（子文档 10 §三）：默认档只带总设计层与定稿那几行，
    /// **不带设计记录、不带汇总、不带别的模块的东西**。
    /// </summary>
    public static class InterfaceSpecDraftPrompt
    {
        /// <summary>给模型的角色交代。</summary>
        public const string SystemContextText =
            "你是给游戏做界面功能规格的人。产出的是**功能契约**，不是美术稿——"
            + "先定契约再定外观，美术返工不该影响程序。只回一份 JSON，不要解释、不要代码块。";

        /// <summary>
        /// 组提示词。
        /// </summary>
        /// <param name="requirementText">需求正文（标题、描述、验收标准那些）。</param>
        /// <param name="panelName">面板名，PascalCase；决定 uidef 名与资产模块目录。</param>
        /// <param name="canvasWidth">画布宽。</param>
        /// <param name="canvasHeight">画布高。</param>
        /// <param name="catalog">元素类型模板，**把可用类型与各自必填摆给模型看**——
        /// 不摆的话它会自造类型名，而那些类型没有模板，校验时全部判红。</param>
        /// <param name="anchor">风格锚点；只用它的总设计层与负面清单，色板不进（这一步不谈配色）。</param>
        public static string Build(
            string requirementText,
            string panelName,
            int canvasWidth,
            int canvasHeight,
            UiElementTemplateCatalog catalog,
            StyleAnchor anchor)
        {
            var builder = new StringBuilder();

            builder.Append("照下面这条需求，产一份**界面规格**：这一屏该有哪些元素、每个元素干什么。\n\n");
            builder.Append("## 需求\n").Append(requirementText ?? "").Append("\n\n");

            if (anchor != null && anchor.DirectionText.Length > 0)
            {
                builder.Append("## 项目总设计（只作参考，别照抄进规格）\n")
                    .Append(anchor.DirectionText).Append("\n\n");
            }

            if (anchor != null && anchor.NegativeList.Count > 0)
            {
                builder.Append("## 明确不要\n- ").Append(string.Join("\n- ", anchor.NegativeList)).Append("\n\n");
            }

            builder.Append("## 可用的元素类型（只许用这几种）\n");
            foreach (var pair in catalog?.Templates ?? new Dictionary<string, UiElementTemplate>())
            {
                builder.Append("- **").Append(pair.Key).Append("**：必填 ")
                    .Append(pair.Value.RequiredFields.Count == 0 ? "（无额外必填）" : string.Join("、", pair.Value.RequiredFields))
                    .Append(pair.Value.NeedsImage ? "；要出图" : "；不出图")
                    .Append('\n');
            }

            builder.Append('\n');
            builder.Append("## 回什么\n只回这一份 JSON：\n");
            builder.Append("{\"面板\": \"")
                .Append(string.IsNullOrWhiteSpace(panelName) ? "PascalCase 的面板名，如 Inventory" : panelName)
                .Append("\", \"标题\": \"人话名字\", ");
            builder.Append("\"画布\": {\"宽\": ").Append(canvasWidth).Append(", \"高\": ").Append(canvasHeight).Append("}, ");
            builder.Append("\"元素\": [{\"id\": \"ButtonSort\", \"名称\": \"排序\", \"类型\": \"Button\", ");
            builder.Append("\"父容器\": \"\", \"布局\": {\"锚定\": \"右上\", \"位置\": [1680, 12], \"尺寸\": [96, 96]}, ");
            builder.Append("\"复用\": \"本界面专有\", \"重复\": 1, ");
            builder.Append("\"交互\": [{\"事件\": \"点击\", \"动作\": \"…\"}], \"成功\": \"…\", ");
            builder.Append("\"失败\": [{\"条件\": \"…\", \"提示\": \"…\", \"处置\": \"…\"}]"
                + "（这个件不会失败时改成一句 \"不会失败：为什么\"）, ");
            builder.Append("\"状态\": [\"常态\", \"禁用\"], \"验收\": \"一句能测的断言\"}]}\n\n");

            builder.Append("## 硬规矩\n");
            builder.Append("1. **id 用 PascalCase 且不许缩写**——写 Button 不写 Btn、写 Background 不写 Bg。"
                + "这个 id 会原样变成代码里的标识符，缩写过不了命名门禁。\n");
            builder.Append("2. **「失败」是数组，一种失败一条**。「背包满了」「钱不够」「网络断了」"
                + "三条的文案与处置完全不同，合成一句「失败提示」等于没写。\n"
                + "   这个件**真的不会失败**时（纯本地的列表选中就没有可失败的一步），"
                + "写成一句「不会失败：为什么」，**不要给空数组**——"
                + "空数组分不清「还没写」与「查过了没有」，而这两件事对程序的意思完全相反；"
                + "也不要为了填格子编一条假失败。\n");
            builder.Append("3. **「验收」必须能测**。「动效要流畅」不算；"
                + "「进场 200ms、ease-out、可被点击打断」才算。写不出来说明这条还没想清楚，"
                + "那就在「验收」里写清楚缺什么，别编一句漂亮话。\n");
            builder.Append("4. **重复的元素只写一条**，用「重复」记个数：四十个一样的格子是一个元素重复四十次，"
                + "不是四十个元素。\n");
            builder.Append("5. **跨界面通用的件标「复用」: \"通用\"**（关闭按钮、通用页签这种），"
                + "本界面独有的标「本界面专有」。\n");
            builder.Append("6. 元素数量**克制**：一屏十几二十个可交互件是正常的，"
                + "上百个说明把装饰纹样和重复格子都当成独立元素了。\n");
            builder.Append("7. 父容器填元素 id，顶层留空串；**不许成环**。\n");

            if (string.IsNullOrWhiteSpace(panelName))
            {
                builder.Append("8. **面板名由你定**：PascalCase、英文、看得出是哪一屏"
                    + "（Inventory / Shop / Settings）。它会变成资产目录名与 uidef 名，"
                    + "所以别带空格与中文。\n");
            }


            return builder.ToString();
        }

        /// <summary>
        /// 把模型的回答解析成一份界面规格的 JSON 对象，并补上机器该填的字段。
        ///
        /// **模型不许自己发 id**：id 由机器按「现存最大号 + 1」发（与 REQ-/DR-/ASSET- 同一套规矩），
        /// 让模型编的话，重跑两次就会撞号或者跳号。
        /// </summary>
        /// <param name="modelText">模型原文。</param>
        /// <param name="identifier">机器发的界面 id。</param>
        /// <param name="requirementIdentifier">来源需求 id。</param>
        /// <param name="moduleName">模块归属；空串表示退回面板名。</param>
        /// <param name="spec">解析出来的规格 JSON；失败时为 null。</param>
        /// <param name="reason">失败原因，人能看懂。</param>
        public static bool TryParse(
            string modelText,
            string identifier,
            string requirementIdentifier,
            string moduleName,
            out JsonObject spec,
            out string reason)
        {
            spec = null;
            reason = "";

            if (string.IsNullOrWhiteSpace(modelText))
            {
                reason = "执行后端回了空文本";
                return false;
            }

            var json = ExtractJsonObject(modelText);
            if (json.Length == 0)
            {
                reason = "回答里找不到一份 JSON 对象（原文前 200 字：" + Preview(modelText) + "）";
                return false;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException exception)
            {
                reason = "回答里那段 JSON 解析失败：" + exception.Message;
                return false;
            }

            if (node is not JsonObject root)
            {
                reason = "回答里那段 JSON 的顶层不是对象";
                return false;
            }

            if (root["元素"] is not JsonArray elements || elements.Count == 0)
            {
                reason = "回答里没有「元素」数组，或者是空的";
                return false;
            }

            // 机器该填的几样，一律以机器为准覆盖模型写的。
            root["id"] = identifier;
            root["状态"] = "草稿";
            root["schema版本"] = "1.0.0";
            root["来源需求"] = new JsonArray { requirementIdentifier };

            // 模块归属：**这一屏是模块的属性，不是那条需求的属性**——需求做完就归档，
            // 而这一屏还在。归属由调用方按需求的「专项」给，模型不参与；
            // 给不出来时退回面板名（下面那一刀归一之后的），不留空。
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                root["模块"] = UiLayerCutter.SafeModuleName(moduleName);
            }

            // 面板名归一：它会变成资产目录名与 uidef 名，**不能带空格与中文**
            // （决策 1：全仓路径只许 ASCII）。模型给的名字九成是对的，
            // 但归一这一刀不能省——一个空格就让落点变成两截。
            if (root["面板"] is JsonValue panelValue && panelValue.TryGetValue<string>(out var rawPanel))
            {
                var safePanel = UiLayerCutter.SafeModuleName(rawPanel);
                root["面板"] = safePanel.Length > 0 ? safePanel : "Common";
            }

            if (root["模块"] == null)
            {
                root["模块"] = root["面板"]?.GetValue<string>() ?? "Common";
            }

            spec = root;
            return true;
        }

        /// <summary>
        /// 从一段可能带着闲话与代码块的文本里抠出第一个完整的 JSON 对象。
        /// 按花括号配平找，不用正则——正则数不清嵌套。
        /// </summary>
        /// <param name="text">原文。</param>
        private static string ExtractJsonObject(string text)
        {
            var start = text.IndexOf('{');
            if (start < 0)
            {
                return "";
            }

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = start; index < text.Length; index++)
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
                }
                else if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, index - start + 1);
                    }
                }
            }

            return "";
        }

        /// <summary>原文前 200 字，报错时给人看。</summary>
        /// <param name="text">原文。</param>
        private static string Preview(string text)
        {
            var trimmed = (text ?? "").Trim();
            return trimmed.Length <= 200 ? trimmed : trimmed.Substring(0, 200) + "…";
        }

        /// <summary>
        /// 发下一个界面 id：扫 <c>Pools/Designs/Interfaces/</c> 里现存最大号 + 1。
        /// 与 REQ-/DR-/ASSET- 同一套规矩（子文档 01 §一）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string AllocateIdentifier(string repositoryRoot)
        {
            var directory = InterfaceSpec.Directory(repositoryRoot);
            var maximum = 0;

            if (System.IO.Directory.Exists(directory))
            {
                foreach (var filePath in System.IO.Directory.EnumerateFiles(directory, "UI-*.json"))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    if (name.Length == 7 && int.TryParse(name.Substring(3), out var number) && number > maximum)
                    {
                        maximum = number;
                    }
                }
            }

            return "UI-" + (maximum + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
