using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>入站信封：Inbox 里一条下游记录的全部内容，从 JSON 文件解析出来后只读使用。</summary>
    public sealed class InboxEnvelope
    {
        /// <summary>
        /// 构造一条入站信封。
        /// </summary>
        /// <param name="channel">渠道名。</param>
        /// <param name="recordIdentifier">下游记录 id。</param>
        /// <param name="revision">下游记录修订号，非负整数。</param>
        /// <param name="submitter">提交人。</param>
        /// <param name="submitTime">提交时间，字符串原样保留不解析。</param>
        /// <param name="linkedRequirement">关联的需求 id，可为 null。</param>
        /// <param name="fields">策划端字段集合。</param>
        /// <param name="sourceFilePath">这条信封的来源文件路径。</param>
        public InboxEnvelope(
            string channel,
            string recordIdentifier,
            int revision,
            string submitter,
            string submitTime,
            string linkedRequirement,
            IReadOnlyDictionary<string, JsonElement> fields,
            string sourceFilePath)
        {
            Channel = channel;
            RecordIdentifier = recordIdentifier;
            Revision = revision;
            Submitter = submitter;
            SubmitTime = submitTime;
            LinkedRequirement = linkedRequirement;
            Fields = fields;
            SourceFilePath = sourceFilePath;
        }

        /// <summary>渠道名，取自下游 driver 的名称。</summary>
        public string Channel { get; }

        /// <summary>下游记录 id。</summary>
        public string RecordIdentifier { get; }

        /// <summary>下游记录修订号，非负整数。</summary>
        public int Revision { get; }

        /// <summary>提交人。</summary>
        public string Submitter { get; }

        /// <summary>提交时间，字符串原样保留不解析。</summary>
        public string SubmitTime { get; }

        /// <summary>关联的需求 id，新记录为 null。</summary>
        public string LinkedRequirement { get; }

        /// <summary>策划端字段集合：字段名到 JSON 值的映射。</summary>
        public IReadOnlyDictionary<string, JsonElement> Fields { get; }

        /// <summary>这条信封的来源文件路径。</summary>
        public string SourceFilePath { get; }

        /// <summary>
        /// 从 JSON 文件解析一条信封；失败时返回 false 并在 failureReason 里写中文原因。
        /// 校验规则：JSON 语法必须正确；「渠道」「记录id」「修订」「字段」任一缺失或类型不对即失败；
        /// 「修订」必须是非负整数；「字段」必须是 JSON 对象。
        /// </summary>
        /// <param name="filePath">信封 JSON 文件路径。</param>
        /// <param name="envelope">解析成功时的信封，失败时为 null。</param>
        /// <param name="failureReason">失败原因，成功时为空串。</param>
        public static bool TryRead(string filePath, out InboxEnvelope envelope, out string failureReason)
        {
            envelope = null;
            failureReason = "";

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                failureReason = $"无法读取文件：{exception.Message}";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                failureReason = $"JSON 语法错误：{exception.Message}";
                return false;
            }

            using (document)
            {
                var root = document.RootElement;

                if (!TryGetString(root, "渠道", out var channel))
                {
                    failureReason = "缺少字符串字段「渠道」";
                    return false;
                }

                if (!TryGetString(root, "记录id", out var recordIdentifier))
                {
                    failureReason = "缺少字符串字段「记录id」";
                    return false;
                }

                if (!root.TryGetProperty("修订", out var revisionElement)
                    || revisionElement.ValueKind != JsonValueKind.Number
                    || !revisionElement.TryGetInt32(out var revision)
                    || revision < 0)
                {
                    failureReason = "字段「修订」必须是非负整数";
                    return false;
                }

                if (!root.TryGetProperty("字段", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Object)
                {
                    failureReason = "字段「字段」必须是 JSON 对象";
                    return false;
                }

                // JsonElement 依赖所属 JsonDocument 的生命周期，必须 Clone 后才能带出作用域。
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in fieldsElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.Clone();
                }

                envelope = new InboxEnvelope(
                    channel,
                    recordIdentifier,
                    revision,
                    GetStringOrEmpty(root, "提交人"),
                    GetStringOrEmpty(root, "提交时间"),
                    GetStringOrNull(root, "关联需求"),
                    fields,
                    filePath);
                return true;
            }
        }

        /// <summary>读取必须为字符串的属性；缺失或类型不对返回 false。</summary>
        private static bool TryGetString(JsonElement root, string propertyName, out string value)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                value = element.GetString() ?? "";
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>读取可选的字符串属性；缺失或类型不对给空串。</summary>
        private static string GetStringOrEmpty(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读取可空字符串属性；缺失或类型不对给 null。</summary>
        private static string GetStringOrNull(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            return null;
        }
    }
}
