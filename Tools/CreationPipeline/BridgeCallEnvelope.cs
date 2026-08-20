using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 下游 driver 调用协议的错误结构：错误码 / 人话 / 可重试。
    /// 错误码是给机器判分支的稳定值，人话是给人看的，可重试标记这次失败值不值得原样重放。
    /// </summary>
    public sealed class BridgeError
    {
        /// <summary>
        /// 构造一份协议错误。
        /// </summary>
        /// <param name="code">错误码，如「超时」「下游不可达」。</param>
        /// <param name="humanText">给人看的中文说明。</param>
        /// <param name="retryable">这次失败是否值得原样重试。</param>
        public BridgeError(string code, string humanText, bool retryable)
        {
            Code = code ?? "";
            HumanText = humanText ?? "";
            Retryable = retryable;
        }

        /// <summary>错误码，如「超时」「下游不可达」。</summary>
        public string Code { get; }

        /// <summary>给人看的中文说明。</summary>
        public string HumanText { get; }

        /// <summary>这次失败是否值得原样重试。</summary>
        public bool Retryable { get; }

        /// <summary>
        /// 序列化成协议 JSON 文本，形如
        /// {"错误码":"…","人话":"…","可重试":true}。
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(BuildNode(), BridgeEnvelopeFormat.WriteOptions);
        }

        /// <summary>
        /// 从 JSON 文本解析一份错误；缺任一字段或类型不对都解析失败。
        /// </summary>
        /// <param name="text">JSON 文本。</param>
        /// <param name="error">解析成功时的错误；失败时为 null。</param>
        /// <param name="reason">解析失败的原因，人能看懂。</param>
        public static bool TryParse(string text, out BridgeError error, out string reason)
        {
            error = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "错误对象为空";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = BridgeEnvelopeFormat.DescribeJsonSyntaxError("错误对象", exception);
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "错误对象的顶层必须是 JSON 对象";
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "错误码", out var code, out reason))
                {
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "人话", out var humanText, out reason))
                {
                    return false;
                }

                if (!root.TryGetProperty("可重试", out var retryableElement) || (retryableElement.ValueKind != JsonValueKind.True && retryableElement.ValueKind != JsonValueKind.False))
                {
                    reason = "错误对象缺「可重试」或它不是布尔值";
                    return false;
                }

                error = new BridgeError(code, humanText, retryableElement.ValueKind == JsonValueKind.True);
                return true;
            }
        }

        /// <summary>把错误序列化成 JSON 对象节点（中文数据键，给响应信封复用）。</summary>
        internal JsonObject BuildNode()
        {
            return new JsonObject
            {
                ["错误码"] = Code,
                ["人话"] = HumanText,
                ["可重试"] = Retryable
            };
        }
    }

    /// <summary>
    /// 下游 driver 调用协议的请求信封：
    /// {"契约版本":"1.0.0","port":"…","动作":"…","配置":{…},"载荷":{…}}。
    /// 「配置」是驱动方的本机配置（可能含密钥），「载荷」是本次调用的业务参数。
    /// 注意：配置里的密钥值只许经协议传输，任何日志、异常消息、ToString 都不许带上它。
    /// </summary>
    public sealed class BridgeRequest
    {
        /// <summary>
        /// 构造一份请求信封。
        /// </summary>
        /// <param name="contractVersion">契约版本，如「1.0.0」。</param>
        /// <param name="port">目标 port，如「模型加工」。</param>
        /// <param name="action">动作，如「caps」「process」。</param>
        /// <param name="configuration">驱动方的本机配置（可能含密钥）。</param>
        /// <param name="payload">本次调用的业务载荷。</param>
        public BridgeRequest(string contractVersion, string port, string action, JsonElement configuration, JsonElement payload)
        {
            ContractVersion = contractVersion ?? "";
            Port = port ?? "";
            Action = action ?? "";
            Configuration = configuration;
            Payload = payload;
        }

        /// <summary>契约版本，如「1.0.0」。</summary>
        public string ContractVersion { get; }

        /// <summary>目标 port，如「模型加工」。</summary>
        public string Port { get; }

        /// <summary>动作，如「caps」「process」。</summary>
        public string Action { get; }

        /// <summary>驱动方的本机配置（可能含密钥）。</summary>
        public JsonElement Configuration { get; }

        /// <summary>本次调用的业务载荷。</summary>
        public JsonElement Payload { get; }

        /// <summary>
        /// 序列化成协议 JSON 文本（单行）。
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(BuildNode(), BridgeEnvelopeFormat.WriteOptions);
        }

        /// <summary>
        /// 从 JSON 文本解析一份请求信封；缺必填键、类型不对或 JSON 语法错都给出人能看懂的 reason。
        /// </summary>
        /// <param name="text">JSON 文本。</param>
        /// <param name="request">解析成功时的请求；失败时为 null。</param>
        /// <param name="reason">解析失败的原因，人能看懂。</param>
        public static bool TryParse(string text, out BridgeRequest request, out string reason)
        {
            request = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "请求为空";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = BridgeEnvelopeFormat.DescribeJsonSyntaxError("请求", exception);
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "请求的顶层必须是 JSON 对象";
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "契约版本", out var contractVersion, out reason))
                {
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "port", out var port, out reason))
                {
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "动作", out var action, out reason))
                {
                    return false;
                }

                if (!root.TryGetProperty("配置", out var configuration) || configuration.ValueKind != JsonValueKind.Object)
                {
                    reason = "请求缺「配置」或它不是对象";
                    return false;
                }

                if (!root.TryGetProperty("载荷", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    reason = "请求缺「载荷」或它不是对象";
                    return false;
                }

                request = new BridgeRequest(contractVersion, port, action, configuration.Clone(), payload.Clone());
                return true;
            }
        }

        private JsonObject BuildNode()
        {
            return new JsonObject
            {
                ["契约版本"] = ContractVersion,
                ["port"] = Port,
                ["动作"] = Action,
                ["配置"] = JsonNode.Parse(Configuration.GetRawText()),
                ["载荷"] = JsonNode.Parse(Payload.GetRawText())
            };
        }
    }

    /// <summary>
    /// 下游 driver 调用协议的响应信封：
    /// 成功 {"契约版本":"1.0.0","成功":true,"载荷":{…}}；
    /// 失败 {"契约版本":"1.0.0","成功":false,"错误":{"错误码":"…","人话":"…","可重试":…}}。
    /// 注意：载荷或错误里出现密钥值即违规——子进程那边把密钥吐回 stdout 就是泄露。
    /// </summary>
    public sealed class BridgeResponse
    {
        /// <summary>
        /// 构造一份响应信封。
        /// </summary>
        /// <param name="contractVersion">契约版本，如「1.0.0」。</param>
        /// <param name="succeeded">是否成功。</param>
        /// <param name="payload">成功时的业务载荷。</param>
        /// <param name="error">失败时的错误结构；成功时为 null。</param>
        public BridgeResponse(string contractVersion, bool succeeded, JsonElement payload, BridgeError error)
        {
            ContractVersion = contractVersion ?? "";
            Succeeded = succeeded;
            Payload = payload;
            Error = error;
        }

        /// <summary>契约版本，如「1.0.0」。</summary>
        public string ContractVersion { get; }

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; }

        /// <summary>成功时的业务载荷。</summary>
        public JsonElement Payload { get; }

        /// <summary>失败时的错误结构；成功时为 null。</summary>
        public BridgeError Error { get; }

        /// <summary>
        /// 构造一份成功响应。
        /// </summary>
        /// <param name="contractVersion">契约版本。</param>
        /// <param name="payload">业务载荷。</param>
        public static BridgeResponse Success(string contractVersion, JsonElement payload)
        {
            return new BridgeResponse(contractVersion, true, payload, null);
        }

        /// <summary>
        /// 构造一份失败响应。
        /// </summary>
        /// <param name="contractVersion">契约版本。</param>
        /// <param name="code">错误码。</param>
        /// <param name="humanText">给人看的中文说明。</param>
        /// <param name="retryable">是否值得重试。</param>
        public static BridgeResponse Failure(string contractVersion, string code, string humanText, bool retryable)
        {
            return new BridgeResponse(contractVersion, false, default, new BridgeError(code, humanText, retryable));
        }

        /// <summary>
        /// 序列化成协议 JSON 文本（单行）。
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(BuildNode(), BridgeEnvelopeFormat.WriteOptions);
        }

        /// <summary>
        /// 从 JSON 文本解析一份响应信封；缺必填键、类型不对或 JSON 语法错都给出人能看懂的 reason。
        /// 失败响应缺「错误」或错误三字段不齐时解析失败。
        /// </summary>
        /// <param name="text">JSON 文本。</param>
        /// <param name="response">解析成功时的响应；失败时为 null。</param>
        /// <param name="reason">解析失败的原因，人能看懂。</param>
        public static bool TryParse(string text, out BridgeResponse response, out string reason)
        {
            response = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "响应为空";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = BridgeEnvelopeFormat.DescribeJsonSyntaxError("响应", exception);
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "响应的顶层必须是 JSON 对象";
                    return false;
                }

                if (!BridgeEnvelopeFormat.TryReadString(root, "契约版本", out var contractVersion, out reason))
                {
                    return false;
                }

                if (!root.TryGetProperty("成功", out var succeededElement) || (succeededElement.ValueKind != JsonValueKind.True && succeededElement.ValueKind != JsonValueKind.False))
                {
                    reason = "响应缺「成功」或它不是布尔值";
                    return false;
                }

                var succeeded = succeededElement.ValueKind == JsonValueKind.True;
                if (succeeded)
                {
                    if (!root.TryGetProperty("载荷", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    {
                        reason = "成功响应缺「载荷」或它不是对象";
                        return false;
                    }

                    response = new BridgeResponse(contractVersion, true, payload.Clone(), null);
                    return true;
                }

                if (!root.TryGetProperty("错误", out var errorElement) || errorElement.ValueKind != JsonValueKind.Object)
                {
                    reason = "失败响应缺「错误」或它不是对象";
                    return false;
                }

                if (!BridgeError.TryParse(errorElement.GetRawText(), out var error, out var errorReason))
                {
                    reason = "失败响应的「错误」不合法：" + errorReason;
                    return false;
                }

                response = new BridgeResponse(contractVersion, false, default, error);
                return true;
            }
        }

        private JsonObject BuildNode()
        {
            return Succeeded
                ? new JsonObject
                {
                    ["契约版本"] = ContractVersion,
                    ["成功"] = true,
                    ["载荷"] = JsonNode.Parse(Payload.GetRawText())
                }
                : new JsonObject
                {
                    ["契约版本"] = ContractVersion,
                    ["成功"] = false,
                    ["错误"] = Error.BuildNode()
                };
        }
    }

    /// <summary>信封的序列化选项与文本辅助：中文原样输出、单行，解析错误给出可读行列。</summary>
    internal static class BridgeEnvelopeFormat
    {
        /// <summary>协议信封的写盘选项。</summary>
        internal static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>从 JSON 对象读字符串键；缺键或类型不对返回 false 并给出可读原因。</summary>
        internal static bool TryReadString(JsonElement root, string propertyName, out string value, out string reason)
        {
            value = "";
            reason = "";
            if (!root.TryGetProperty(propertyName, out var element))
            {
                reason = "缺「" + propertyName + "」";
                return false;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                reason = "「" + propertyName + "」必须是字符串";
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>把 JsonException 转成人能看懂的行列描述。</summary>
        internal static string DescribeJsonSyntaxError(string what, JsonException exception)
        {
            var builder = new StringBuilder();
            builder.Append(what);
            builder.Append("不是合法 JSON");
            if (exception.LineNumber.HasValue || exception.BytePositionInLine.HasValue)
            {
                builder.Append("（第 ");
                builder.Append((exception.LineNumber ?? 0) + 1);
                builder.Append(" 行第 ");
                builder.Append((exception.BytePositionInLine ?? 0) + 1);
                builder.Append(" 列附近）");
            }

            builder.Append("：");
            builder.Append(exception.Message);
            return builder.ToString();
        }
    }
}
