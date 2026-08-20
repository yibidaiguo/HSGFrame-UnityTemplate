using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 专项认领的入站信封：下游专项表同步过来的认领变更，只带认领字段与工程溯源信息。
    /// 认领字段之外的专项字段归策划端，下游不许经这条通道改——信封里多余的键一律忽略并记名。
    /// </summary>
    public sealed class EpicClaimEnvelope
    {
        /// <summary>专项 id 的格式：EP- 后跟四位数字。</summary>
        private static readonly Regex EpicIdentifierPattern = new Regex("^EP-\\d{4}$", RegexOptions.Compiled);

        /// <summary>
        /// 构造一条专项认领信封。
        /// </summary>
        /// <param name="channel">渠道名。</param>
        /// <param name="epicIdentifier">专项 id，如「EP-0003」。</param>
        /// <param name="revision">下游修订号，非负整数。</param>
        /// <param name="submitter">提交人。</param>
        /// <param name="submitTime">提交时间，字符串原样保留不解析。</param>
        /// <param name="claims">认领表：职责名 → open_id 列表。</param>
        /// <param name="ignoredFieldNames">被忽略的多余键名，序数序。</param>
        /// <param name="sourceFilePath">这条信封的来源文件路径。</param>
        public EpicClaimEnvelope(
            string channel,
            string epicIdentifier,
            int revision,
            string submitter,
            string submitTime,
            IReadOnlyDictionary<string, IReadOnlyList<string>> claims,
            IReadOnlyList<string> ignoredFieldNames,
            string sourceFilePath)
        {
            Channel = channel ?? "";
            EpicIdentifier = epicIdentifier ?? "";
            Revision = revision;
            Submitter = submitter ?? "";
            SubmitTime = submitTime ?? "";
            Claims = claims ?? new Dictionary<string, IReadOnlyList<string>>();
            IgnoredFieldNames = ignoredFieldNames ?? Array.Empty<string>();
            SourceFilePath = sourceFilePath ?? "";
        }

        /// <summary>渠道名。</summary>
        public string Channel { get; }

        /// <summary>专项 id，如「EP-0003」。</summary>
        public string EpicIdentifier { get; }

        /// <summary>下游修订号，非负整数。</summary>
        public int Revision { get; }

        /// <summary>提交人。</summary>
        public string Submitter { get; }

        /// <summary>提交时间，字符串原样保留不解析。</summary>
        public string SubmitTime { get; }

        /// <summary>认领表：职责名 → open_id 列表。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }

        /// <summary>被忽略的多余键名，序数序。</summary>
        public IReadOnlyList<string> IgnoredFieldNames { get; }

        /// <summary>这条信封的来源文件路径。</summary>
        public string SourceFilePath { get; }

        /// <summary>
        /// 从 JSON 文件解析一条专项认领信封；失败时返回 false 并在 failureReason 里写中文原因，不抛异常。
        /// 校验规则：「专项id」缺失或不匹配 EP-\d{4} 判失败；「修订」不是整数判失败；
        /// 「认领」缺失当空字典；除 通道/专项id/修订/提交人/提交时间/认领 之外的键一律忽略并记名。
        /// </summary>
        /// <param name="filePath">信封 JSON 文件路径。</param>
        /// <param name="envelope">解析成功时的信封，失败时为 null。</param>
        /// <param name="failureReason">失败原因，成功时为空串。</param>
        public static bool TryRead(string filePath, out EpicClaimEnvelope envelope, out string failureReason)
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
                if (root.ValueKind != JsonValueKind.Object)
                {
                    failureReason = "信封根必须是 JSON 对象";
                    return false;
                }

                if (!TryGetString(root, "专项id", out var epicIdentifier) || !EpicIdentifierPattern.IsMatch(epicIdentifier))
                {
                    failureReason = "字段「专项id」缺失或不是 EP-后跟四位数字的格式";
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

                var claims = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                if (root.TryGetProperty("认领", out var claimsElement) && claimsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in claimsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        var identifiers = new List<string>();
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                identifiers.Add(item.GetString() ?? "");
                            }
                        }

                        claims[property.Name] = identifiers;
                    }
                }

                var ignored = new List<string>();
                foreach (var property in root.EnumerateObject())
                {
                    if (!IsKnownField(property.Name))
                    {
                        ignored.Add(property.Name);
                    }
                }

                ignored.Sort(StringComparer.Ordinal);

                envelope = new EpicClaimEnvelope(
                    GetStringOrEmpty(root, "通道"),
                    epicIdentifier,
                    revision,
                    GetStringOrEmpty(root, "提交人"),
                    GetStringOrEmpty(root, "提交时间"),
                    claims,
                    ignored,
                    filePath);
                return true;
            }
        }

        /// <summary>信封认识的六个键；其余一律忽略。</summary>
        private static bool IsKnownField(string fieldName)
        {
            return string.Equals(fieldName, "通道", StringComparison.Ordinal)
                || string.Equals(fieldName, "专项id", StringComparison.Ordinal)
                || string.Equals(fieldName, "修订", StringComparison.Ordinal)
                || string.Equals(fieldName, "提交人", StringComparison.Ordinal)
                || string.Equals(fieldName, "提交时间", StringComparison.Ordinal)
                || string.Equals(fieldName, "认领", StringComparison.Ordinal);
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
    }
}
