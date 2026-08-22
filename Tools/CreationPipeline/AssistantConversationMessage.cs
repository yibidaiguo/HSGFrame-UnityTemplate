using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 消息里带的一个附件：图片或文件。
    ///
    /// 只留「哪条消息里的哪个 key、叫什么名」——**没有本地路径**：
    /// 下载要调下游接口，那是桥的事（决策 93）。引擎要用时再让桥取回来。
    /// </summary>
    /// <param name="Kind">image 或 file。这两种在下游是两种取法，不能混。</param>
    /// <param name="Key">下游给的资源标识。</param>
    /// <param name="FileName">原始文件名；图片一般没有，为空串。</param>
    public sealed record AssistantAttachment(string Kind, string Key, string FileName)
    {
        /// <summary>是不是一张图。图能当参考图喂给出图，别的类型不能。</summary>
        public bool IsImage
        {
            get { return string.Equals(Kind, "image", StringComparison.Ordinal); }
        }

        /// <summary>存到本地时该用的扩展名。文件按原名推，图片一律 .png。</summary>
        public string FileExtension
        {
            get
            {
                if (IsImage)
                {
                    return ".png";
                }

                var extension = System.IO.Path.GetExtension(FileName ?? "");
                return extension.Length > 0 ? extension : ".bin";
            }
        }
    }

    /// <summary>
    /// 一条待回话的会话消息：从会话信号文件里的「会话」块读出来的归一形状。
    ///
    /// **信号文件里还有一份原始载荷，引擎一个字都不看**——那份是下游特有的形状，
    /// 解析它属于下游知识（决策 93），所以归一这一步由桥的旁路做，引擎只认「会话」块。
    /// 这样换一个消息下游，引擎侧零改动。
    /// </summary>
    public sealed class AssistantConversationMessage
    {
        /// <summary>
        /// 构造一条会话消息。
        /// </summary>
        /// <param name="conversationIdentifier">会话标识，回话时按它找回去。</param>
        /// <param name="senderIdentifier">发件人标识。</param>
        /// <param name="messageIdentifier">消息标识，用于幂等与回复定位。</param>
        /// <param name="messageKind">消息类型，如 text；非文字类型引擎不处理但要如实说。</param>
        /// <param name="text">消息正文；非文字消息为空串。</param>
        /// <param name="receivedAt">收到时间，原样字符串。</param>
        /// <param name="actionName">按钮动作名；不是按钮点击时为空串。</param>
        /// <param name="actionValue">按钮带回来的键值；不是按钮点击时为空对象。</param>
        /// <param name="attachments">消息里带的图片与文件；没有时为空表。</param>
        public AssistantConversationMessage(
            string conversationIdentifier,
            string senderIdentifier,
            string messageIdentifier,
            string messageKind,
            string text,
            string receivedAt,
            string actionName = "",
            JsonObject actionValue = null,
            IReadOnlyList<AssistantAttachment> attachments = null)
        {
            ConversationIdentifier = conversationIdentifier ?? "";
            SenderIdentifier = senderIdentifier ?? "";
            MessageIdentifier = messageIdentifier ?? "";
            MessageKind = messageKind ?? "";
            Text = text ?? "";
            ReceivedAt = receivedAt ?? "";
            ActionName = actionName ?? "";
            ActionValue = actionValue ?? new JsonObject();
            Attachments = attachments ?? Array.Empty<AssistantAttachment>();
        }

        /// <summary>会话标识，回话时按它找回去。</summary>
        public string ConversationIdentifier { get; }

        /// <summary>发件人标识。</summary>
        public string SenderIdentifier { get; }

        /// <summary>消息标识，用于幂等与回复定位。</summary>
        public string MessageIdentifier { get; }

        /// <summary>消息类型，如 text。</summary>
        public string MessageKind { get; }

        /// <summary>消息正文；非文字消息为空串。</summary>
        public string Text { get; }

        /// <summary>收到时间，原样字符串。</summary>
        public string ReceivedAt { get; }

        /// <summary>按钮动作名；不是按钮点击时为空串。</summary>
        public string ActionName { get; }

        /// <summary>按钮带回来的键值；不是按钮点击时为空对象。</summary>
        public JsonObject ActionValue { get; }

        /// <summary>消息里带的图片与文件；没有时为空表，不为 null。</summary>
        public IReadOnlyList<AssistantAttachment> Attachments { get; }

        /// <summary>卡片按钮点击的消息类型。</summary>
        public const string CardActionKind = "card_action";

        /// <summary>
        /// 是不是一条纯文字消息。**已被 <see cref="IsHandleable"/> 取代**，生产路径一处都不再用它；
        /// 留着只为让「改测试断言」那一步单独走（铁律 3），下一次提交就删。
        /// </summary>
        public bool IsHandleableText
        {
            get { return string.Equals(MessageKind, "text", StringComparison.Ordinal) && Text.Trim().Length > 0; }
        }

        /// <summary>
        /// 这条消息能不能处理：有正文、或者带了附件。
        ///
        /// 判据**不是「类型是不是 text」**：人发一张参考图配一句「照这个再出一张」，
        /// 那是 post；直接甩一个 psd 过来，那是 file——两种都是他在说话，
        /// 只认 text 的话助手会回一句「我只认文字消息」，而他明明已经说完了。
        /// 真正处理不了的是**什么都没有**：正文空、附件也空（表情包、语音就落在这儿）。
        /// </summary>
        public bool IsHandleable
        {
            get { return !IsCardAction && (Text.Trim().Length > 0 || Attachments.Count > 0); }
        }

        /// <summary>
        /// 是不是一次卡片按钮点击。**与文字消息分成两支**：按钮点击不该再去问执行后端，
        /// 它带的是一个明确的动作，过一趟模型只会平白多一次不确定与一次花销。
        /// </summary>
        public bool IsCardAction
        {
            get { return string.Equals(MessageKind, CardActionKind, StringComparison.Ordinal) && ActionName.Length > 0; }
        }

        /// <summary>读按钮携带的某个字符串键；缺失给空串。</summary>
        /// <param name="propertyName">键名。</param>
        public string ReadActionValue(string propertyName)
        {
            return ActionValue != null
                && ActionValue.TryGetPropertyValue(propertyName, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        /// <summary>
        /// 从会话信号文件读一条消息。文件读不动、不是 JSON、缺「会话」块都算失败并写清原因——
        /// **不许拿空对象顶上去**（决策 42：读不动与「没有内容」是两支）。
        /// </summary>
        /// <param name="signalFilePath">会话信号文件路径。</param>
        /// <param name="message">读成功时的消息；失败时为 null。</param>
        /// <param name="reason">失败原因，人能看懂。</param>
        public static bool TryReadFile(string signalFilePath, out AssistantConversationMessage message, out string reason)
        {
            message = null;
            reason = "";

            string text;
            try
            {
                text = File.ReadAllText(signalFilePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = "会话信号文件读不动：" + exception.Message;
                return false;
            }

            return TryParse(text, out message, out reason);
        }

        /// <summary>
        /// 从会话信号 JSON 文本解析一条消息。
        /// </summary>
        /// <param name="signalText">信号文件的 JSON 文本。</param>
        /// <param name="message">解析成功时的消息；失败时为 null。</param>
        /// <param name="reason">失败原因，人能看懂。</param>
        public static bool TryParse(string signalText, out AssistantConversationMessage message, out string reason)
        {
            message = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(signalText))
            {
                reason = "会话信号是空的";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(signalText);
            }
            catch (JsonException exception)
            {
                reason = "会话信号不是合法 JSON：" + exception.Message;
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "会话信号的顶层不是 JSON 对象";
                    return false;
                }

                if (!root.TryGetProperty("会话", out var conversation) || conversation.ValueKind != JsonValueKind.Object)
                {
                    reason = "会话信号里没有「会话」块——这份信号是旧格式或不是会话事件，"
                        + "归一那一步该由下游旁路做（决策 93）";
                    return false;
                }

                var conversationIdentifier = ReadString(conversation, "会话标识");
                if (conversationIdentifier.Length == 0)
                {
                    reason = "「会话」块里没有会话标识，回话就没有去处";
                    return false;
                }

                message = new AssistantConversationMessage(
                    conversationIdentifier,
                    ReadString(conversation, "发件人标识"),
                    ReadString(conversation, "消息标识"),
                    ReadString(conversation, "消息类型"),
                    ReadString(conversation, "文本"),
                    ReadString(root, "收到时间"),
                    ReadString(conversation, "按钮动作"),
                    ReadObject(conversation, "按钮携带"),
                    ReadAttachments(conversation));
                return true;
            }
        }

        /// <summary>
        /// 读「附件」表。缺失、类型不对、单项缺 key 都跳过——
        /// 少一个附件只是这一条少一张参考图，把整条消息判成读不动才是坏事。
        /// </summary>
        /// <param name="element">「会话」块。</param>
        private static IReadOnlyList<AssistantAttachment> ReadAttachments(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("附件", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<AssistantAttachment>();
            }

            var attachments = new List<AssistantAttachment>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var key = ReadString(item, "key");
                if (key.Length == 0)
                {
                    continue;
                }

                attachments.Add(new AssistantAttachment(ReadString(item, "类型"), key, ReadString(item, "文件名")));
            }

            return attachments;
        }

        /// <summary>
        /// 读对象里的对象键，拷成可写的 <see cref="JsonObject"/>；缺失或类型不对给空对象。
        /// 空对象而不是 null：调用方读键时不必先判空，少一处能崩的地方。
        /// </summary>
        /// <param name="element">所在对象。</param>
        /// <param name="propertyName">键名。</param>
        private static JsonObject ReadObject(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Object)
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(value.GetRawText()) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return new JsonObject();
            }
        }

        /// <summary>读对象里的字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
