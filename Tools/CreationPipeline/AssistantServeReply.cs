using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 执行后端一轮回答的解析结果。
    ///
    /// **解析失败绝不许当成「没什么要建的」**（决策 42）：解析失败是一支，
    /// 「模型说信息不够」是另一支，两支的回话内容完全不同，合并会把故障印成正常结论。
    /// </summary>
    public sealed class AssistantServeReply
    {
        /// <summary>
        /// 构造一份解析结果。
        /// </summary>
        /// <param name="parsed">解析成功与否。</param>
        /// <param name="replyText">给人看的回话。</param>
        /// <param name="wantsRequirement">模型认为这轮该不该建需求。</param>
        /// <param name="missingItems">模型说还缺什么。</param>
        /// <param name="draft">需求草稿；没有时为 null。</param>
        /// <param name="parseFailureReason">解析失败原因；成功时为空串。</param>
        /// <param name="intentSummary">模型对「这个人想干什么」的一句话复述；没有时为空串。</param>
        public AssistantServeReply(
            bool parsed,
            string replyText,
            bool wantsRequirement,
            IReadOnlyList<string> missingItems,
            JsonObject draft,
            string parseFailureReason,
            string intentSummary = "")
        {
            Parsed = parsed;
            ReplyText = replyText ?? "";
            WantsRequirement = wantsRequirement;
            MissingItems = missingItems ?? Array.Empty<string>();
            Draft = draft;
            ParseFailureReason = parseFailureReason ?? "";
            IntentSummary = intentSummary ?? "";
        }

        /// <summary>解析成功与否。</summary>
        public bool Parsed { get; }

        /// <summary>给人看的回话。</summary>
        public string ReplyText { get; }

        /// <summary>模型认为这轮该不该建需求。解析失败时恒为 false。</summary>
        public bool WantsRequirement { get; }

        /// <summary>
        /// 模型这一轮想跟人确认的点（契约键「要问的问题」，兼容旧键「还缺什么」）。
        /// 这里**不是**给人看的必填字段清单——把 schema 字段名罗列给人，正是助手最招人烦的老毛病。
        /// </summary>
        public IReadOnlyList<string> MissingItems { get; }

        /// <summary>模型对「这个人想干什么」的一句话复述；没有时为空串。</summary>
        public string IntentSummary { get; }

        /// <summary>需求草稿；没有时为 null。</summary>
        public JsonObject Draft { get; }

        /// <summary>解析失败原因；成功时为空串。</summary>
        public string ParseFailureReason { get; }

        /// <summary>解析失败时的结果：回话如实说「我没读懂模型的回答」，绝不冒充正常结论。</summary>
        /// <param name="reason">失败原因。</param>
        public static AssistantServeReply NotParsed(string reason)
        {
            return new AssistantServeReply(
                parsed: false,
                replyText: "我这边没能读懂执行后端的回答，所以这一轮什么都没建。原因：" + reason,
                wantsRequirement: false,
                missingItems: Array.Empty<string>(),
                draft: null,
                parseFailureReason: reason);
        }

        /// <summary>
        /// 解析模型回答。容忍最常见的两种脏：外面包了 ```json 代码块、前后有闲话。
        /// 但**不容忍缺「回话」**——没有回话就等于没法给人回复，那是失败不是降级。
        /// </summary>
        /// <param name="modelText">模型回答原文。</param>
        /// <param name="reply">解析结果，无论成功失败都非 null。</param>
        public static bool TryParse(string modelText, out AssistantServeReply reply)
        {
            if (string.IsNullOrWhiteSpace(modelText))
            {
                reply = NotParsed("执行后端回了空文本");
                return false;
            }

            var json = ExtractJsonObject(modelText);
            if (json.Length == 0)
            {
                reply = NotParsed("回答里找不到一份 JSON 对象（原文前 200 字：" + Preview(modelText) + "）");
                return false;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException exception)
            {
                reply = NotParsed("回答里那段 JSON 解析失败：" + exception.Message);
                return false;
            }

            if (node is not JsonObject root)
            {
                reply = NotParsed("回答的顶层不是 JSON 对象");
                return false;
            }

            var replyText = ReadString(root, "回话");
            if (replyText.Trim().Length == 0)
            {
                reply = NotParsed("回答里没有「回话」，没法给人回复");
                return false;
            }

            var wants = ReadBool(root, "要不要建需求");

            // 「要问的问题」是现在的契约键，「还缺什么」是上一版的。两个都读，
            // 是因为提示词版本一变、旧模型缓存与新契约会并存一阵，只读新键会让那一阵的问题全丢。
            var missing = ReadStringArray(root, "要问的问题");
            if (missing.Count == 0)
            {
                missing = ReadStringArray(root, "还缺什么");
            }

            var intent = ReadString(root, "我理解你想干的");

            JsonObject draft = null;
            if (root.TryGetPropertyValue("需求草稿", out var draftNode) && draftNode is JsonObject draftObject)
            {
                draft = (JsonObject)draftObject.DeepClone();
            }

            if (wants && draft == null)
            {
                // 说要建却没给草稿，是自相矛盾。按「不建」处理并在回话里说清楚，
                // 不许悄悄当成「建了一个空需求」。
                reply = new AssistantServeReply(
                    parsed: true,
                    replyText: replyText + "\n\n（引擎注：模型说要建需求却没给草稿内容，这一轮没有写表。）",
                    wantsRequirement: false,
                    missingItems: missing,
                    draft: null,
                    parseFailureReason: "",
                    intentSummary: intent);
                return true;
            }

            reply = new AssistantServeReply(true, replyText, wants, missing, draft, "", intent);
            return true;
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
                var ch = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    if (depth == 0)
                    {
                        start = index;
                    }

                    depth++;
                    continue;
                }

                if (ch == '}')
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

        /// <summary>读字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject root, string propertyName)
        {
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text ?? "";
            }

            return "";
        }

        /// <summary>读布尔键；缺失或类型不对给 false（保守：默认不建需求）。</summary>
        private static bool ReadBool(JsonObject root, string propertyName)
        {
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag))
            {
                return flag;
            }

            return false;
        }

        /// <summary>读字符串数组键；缺失给空列表，元素里非字符串的跳过。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonObject root, string propertyName)
        {
            var items = new List<string>();
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                    {
                        items.Add(text);
                    }
                }
            }

            return items;
        }

        /// <summary>原文预览：截断到 200 字。</summary>
        private static string Preview(string text)
        {
            return text.Length <= 200 ? text : text.Substring(0, 200) + "…（已截断）";
        }
    }
}
