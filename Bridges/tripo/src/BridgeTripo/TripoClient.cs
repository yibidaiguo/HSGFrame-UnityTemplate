using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Tripo
{
    /// <summary>
    /// 任务状态机的一次判定结果：是否终态、是否成功、状态原样字符串与人话。
    /// 未知状态当失败（决策 42：不许默默继续轮询到超时），原样字符串带出来给人看。
    /// </summary>
    public sealed class TripoTaskStateResult
    {
        /// <summary>
        /// 构造一次判定结果。
        /// </summary>
        /// <param name="isFinal">是否终态。</param>
        /// <param name="succeeded">是否成功。</param>
        /// <param name="statusText">下游返回的状态字符串，原样。</param>
        /// <param name="humanText">给人看的人话。</param>
        public TripoTaskStateResult(bool isFinal, bool succeeded, string statusText, string humanText)
        {
            IsFinal = isFinal;
            Succeeded = succeeded;
            StatusText = statusText ?? "";
            HumanText = humanText ?? "";
        }

        /// <summary>是否终态；终态后不许再轮询。</summary>
        public bool IsFinal { get; }

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; }

        /// <summary>下游返回的状态字符串，原样。</summary>
        public string StatusText { get; }

        /// <summary>给人看的人话。</summary>
        public string HumanText { get; }
    }

    /// <summary>
    /// 纯函数状态机：把下游 GET /tasks/&lt;id&gt; 响应里的 status 字符串判定成「继续轮询 / 终态成功 / 终态失败」。
    /// 状态取值是 tripo 特有知识（换一个模型生成 driver 不成立），所以住桥里不住引擎（决策 93）。
    /// 未知状态一律当失败、原样带出字符串，绝不默默当成还在跑然后一直轮询到超时（决策 42）。
    /// </summary>
    public static class TripoTaskState
    {
        /// <summary>
        /// 判定一个状态字符串。
        /// </summary>
        /// <param name="status">下游返回的 status 原样字符串。</param>
        public static TripoTaskStateResult Classify(string status)
        {
            var text = (status ?? "").Trim();
            switch (text)
            {
                case "queued":
                case "running":
                    return new TripoTaskStateResult(false, false, text, "任务还在跑（" + text + "），继续轮询");
                case "success":
                    return new TripoTaskStateResult(true, true, text, "任务成功");
                case "failed":
                    return new TripoTaskStateResult(true, false, text, "任务失败（下游返回 failed，任务 id 见返回）");
                case "banned":
                    return new TripoTaskStateResult(true, false, text, "任务因违反内容政策被封禁（banned）");
                case "expired":
                    return new TripoTaskStateResult(true, false, text, "任务已过期（expired），可带 task_id 向下游查询");
                case "cancelled":
                    return new TripoTaskStateResult(true, false, text, "任务已取消（cancelled）");
                case "unknown":
                    return new TripoTaskStateResult(true, false, text, "下游返回状态「unknown」，按失败处理");
                default:
                    return new TripoTaskStateResult(
                        true,
                        false,
                        text,
                        string.IsNullOrEmpty(text)
                            ? "下游返回了空的状态字符串，按失败处理"
                            : "下游返回了不认识的终态字符串「" + text + "」，按失败处理（决策 42：不许默默继续轮询）");
            }
        }
    }

    /// <summary>
    /// HTTP 状态码与响应体 → 协议错误码的纯函数映射，脱离网络可测。
    /// 人话要写清「这是积分用完，不是代码坏了」（决策 91 的精神）。
    /// </summary>
    public static class TripoHttpErrorMapper
    {
        /// <summary>
        /// 把一次失败的 HTTP 调用映射成协议错误。
        /// </summary>
        /// <param name="statusCode">HTTP 状态码。</param>
        /// <param name="responseText">响应体文本，可能为空。</param>
        public static BridgeError Map(int statusCode, string responseText)
        {
            // v3 的服务端错误码比 HTTP 状态码分得细：同一个 403 底下有「积分不足」与「密钥无权」两种
            // 完全不同的原因，同一个 400 底下有「参数非法」，同一个 404 底下有「端点不存在」与「任务不存在」。
            // 所以先看响应体里的 code，取不到再退回按 HTTP 状态码分（v3 实测码见 Bridges/tripo/endpoints-verified.md）。
            var serverCode = ReadErrorCode(responseText);
            switch (serverCode)
            {
                case 2010:
                    return new BridgeError("额度不足", "下游 code 2010：账号 API 积分不足，不是代码坏了。注意网页版订阅与 API credits 是两套额度，要在开发者门户单独买", retryable: false);
                case 1005:
                    return new BridgeError("凭据无效", "下游 code 1005：该密钥无权访问此资源", retryable: false);
                case 1004:
                    return new BridgeError("请求不合协议", "下游 code 1004：参数非法——是我们发的请求形状不对，不是账号问题。下游原话：" + ReadServerMessage(responseText), retryable: false);
                case 4001:
                    return new BridgeError("下游报错", "下游 code 4001：端点不存在——base URL 或 API 版本写错了。下游原话：" + ReadServerMessage(responseText), retryable: false);
                case 2001:
                    return new BridgeError("下游报错", "下游 code 2001：任务不存在——task_id 不属于这个账号，或格式不对", retryable: false);
            }

            if (statusCode == 401)
            {
                return new BridgeError("凭据无效", $"下游返回 HTTP {statusCode}，密钥无效或无权访问", retryable: false);
            }

            if (statusCode == 403)
            {
                return new BridgeError("凭据无效", $"下游返回 HTTP {statusCode}，密钥无效或无权访问", retryable: false);
            }

            if (statusCode == 402)
            {
                return new BridgeError("额度不足", "下游返回 HTTP 402：账号积分/配额用完了，不是代码坏了。请到下游控制台确认剩余积分", retryable: false);
            }

            if (statusCode == 429)
            {
                return new BridgeError("限流", "下游返回 HTTP 429，被限流，稍后重试", retryable: true);
            }

            var serverMessage = ReadServerMessage(responseText);
            if (LooksLikeOutOfCredits(serverMessage))
            {
                return new BridgeError("额度不足", "下游说积分/配额不足，不是代码坏了。请到下游控制台确认剩余积分：" + serverMessage, retryable: false);
            }

            var retryable = statusCode >= 500;
            return new BridgeError("下游报错", $"下游返回 HTTP {statusCode}：{serverMessage}", retryable: retryable);
        }

        /// <summary>从错误响应体里抠服务端 code（数字）；取不到给 0。</summary>
        private static int ReadErrorCode(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return 0;
            }

            try
            {
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.Number)
                {
                    return code.GetInt32();
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
            }

            return 0;
        }

        /// <summary>从错误响应体里抠服务端 message：先试 {"message":"…"}，再试 {"error":{"message":"…"}}。</summary>
        private static string ReadServerMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return "（服务端没有返回错误说明）";
            }

            try
            {
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "（服务端返回的不是 JSON 对象）";
                }

                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                if (root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nestedMessage)
                    && nestedMessage.ValueKind == JsonValueKind.String)
                {
                    var text = nestedMessage.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            catch (JsonException)
            {
            }

            return "（服务端返回的不是带 message 的 JSON 错误）";
        }

        /// <summary>message 里有没有「积分/配额/余额」这层意思（大小写不敏感）。</summary>
        private static bool LooksLikeOutOfCredits(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var lower = message.ToLowerInvariant();
            return lower.Contains("credit")
                || lower.Contains("balance")
                || lower.Contains("quota")
                || lower.Contains("insufficient")
                || lower.Contains("积分")
                || lower.Contains("余额");
        }
    }

    /// <summary>下游调用失败：错误码（下游不可达 / 凭据无效 / 额度不足 / 限流 / 超时 / 下游报错）、人话与可重试标记。</summary>
    public sealed class TripoClientException : Exception
    {
        /// <summary>
        /// 构造一份下游调用失败。
        /// </summary>
        /// <param name="errorCode">协议错误码。</param>
        /// <param name="humanText">给人看的中文说明。</param>
        /// <param name="retryable">是否值得原样重试。</param>
        public TripoClientException(string errorCode, string humanText, bool retryable)
            : base(humanText)
        {
            ErrorCode = errorCode ?? "";
            Retryable = retryable;
        }

        /// <summary>协议错误码：下游不可达 / 凭据无效 / 额度不足 / 限流 / 超时 / 下游报错。</summary>
        public string ErrorCode { get; }

        /// <summary>是否值得原样重试。</summary>
        public bool Retryable { get; }
    }

    /// <summary>一次任务查询的结果：状态判定 + 成功时的模型下载 URL（可能没有）。</summary>
    public sealed class TripoTaskQuery
    {
        /// <summary>
        /// 构造一次任务查询结果。
        /// </summary>
        /// <param name="state">状态判定。</param>
        /// <param name="modelUrl">成功时 output 里的模型下载 URL；没有给空串。</param>
        public TripoTaskQuery(TripoTaskStateResult state, string modelUrl)
        {
            State = state;
            ModelUrl = modelUrl ?? "";
        }

        /// <summary>状态判定。</summary>
        public TripoTaskStateResult State { get; }

        /// <summary>成功时 output 里的模型下载 URL；没有给空串。</summary>
        public string ModelUrl { get; }
    }

    /// <summary>
    /// 对 tripo 的 HTTP 调用层：提交 text_to_model 任务、轮询任务状态、下载模型。
    /// 密钥红线（决策 5、78）：密钥只进 Authorization 头——不进日志、不进异常消息、不进返回载荷，
    /// 长度和前缀也不许。错误文案里不允许出现请求头。
    /// </summary>
    public sealed class TripoClient : IDisposable
    {
        /// <summary>轮询间隔毫秒数：线上服务，任务书要求不小于 5 秒。</summary>
        private const int PollIntervalMilliseconds = 5000;

        /// <summary>
        /// 提交时用的缺省模型版本：v3.0（定价表里 text_to_model 无纹理 10 积分，最省的档位）。
        /// 调用方可用配置键「模型版本」覆盖；填「自动」则由调用侧从探测清单里现挑，桥收到的永远是真值。
        /// </summary>
        public const string DefaultModelVersion = "v3.0-20250812";

        /// <summary>
        /// 2026-08-21 实证时服务端报的四个模型版本，由 code 1004 的报错原文记下：
        /// 「invalid model 'tripo-v3.1', allowed values: P1-20260311, v2.5-20250123, v3.0-20250812, v3.1-20260211」。
        ///
        /// **这是一份快照，不是白名单。**当前清单请跑 caps 探（<see cref="ProbeAllowedModelVersions"/>）——
        /// 它问的是服务端此刻怎么说。这份快照只用来在日志里提醒一句「你填的不在上次实证的清单里」，
        /// 不拦任何调用。
        /// </summary>
        public static readonly string[] AllowedModelVersions =
        {
            "P1-20260311",
            "v2.5-20250123",
            "v3.0-20250812",
            "v3.1-20260211"
        };

        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly int _timeoutSeconds;
        private readonly string _modelVersion;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// 构造对下游的 HTTP 客户端。
        /// </summary>
        /// <param name="baseUrl">下游地址，v3 是 https://openapi.tripo3d.ai/v3。</param>
        /// <param name="apiKey">模型生成密钥，只进 Authorization 头。</param>
        /// <param name="timeoutSeconds">单次 HTTP 调用的超时秒数。</param>
        /// <param name="modelVersion">模型版本；空串用缺省值。不在允许列表里当场抛，不发请求。</param>
        public TripoClient(string baseUrl, string apiKey, int timeoutSeconds, string modelVersion = "")
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _apiKey = apiKey ?? "";
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
            _modelVersion = NormalizeModelVersion(modelVersion);
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
        }

        /// <summary>
        /// 归一模型版本：空串给缺省值，其余原样交给服务端判。
        ///
        /// **这里刻意不拦。**从前它拿 <see cref="AllowedModelVersions"/> 当白名单，
        /// 不在里面就当场抛。那份名单是 2026-08-21 实证时的快照，而现在清单是**探出来的**：
        /// 下游哪天多一个模型，探测就会把它列进面板的下拉，人挑了它却被本机的旧快照拦下来——
        /// 那才是真的坏。判官交回服务端：真不合法它回 1004，报错里带着当时的 allowed values，
        /// 比任何快照都新。不合法的值只多花一次注定被拒的调用，那一次不花积分。
        /// </summary>
        /// <param name="modelVersion">调用方给的模型版本。</param>
        public static string NormalizeModelVersion(string modelVersion)
        {
            var text = (modelVersion ?? "").Trim();
            if (text.Length == 0)
            {
                return DefaultModelVersion;
            }

            if (Array.IndexOf(AllowedModelVersions, text) < 0)
            {
                Console.Error.WriteLine(
                    "BridgeTripo 模型版本「" + text + "」不在上次实证的清单里（" + string.Join("、", AllowedModelVersions)
                    + "）——照发，由服务端判。想看当前清单跑 bridge.catalog --Driver tripo --Refresh true");
            }

            return text;
        }

        /// <summary>
        /// 探清单用的哨兵模型值。它唯一的作用是**被拒**——必须明显不可能是任何真模型名。
        /// </summary>
        private const string CatalogProbeSentinel = "__catalog_probe__";

        /// <summary>
        /// 探下游允许的模型版本清单。tripo v3 **没有 list-models 接口**，
        /// 但它会在参数校验阶段把允许值原样报出来：拿一个明显非法的哨兵值提交，
        /// 服务端回 400 / code 1004「invalid model '…', allowed values: …」，清单就在这句里。
        /// 参数关在积分关**之前**（见 endpoints-verified.md），所以这一次探测不花积分、不产模型。
        ///
        /// 解析不出来时抛异常，**绝不返回空清单**：空清单会被上层读成
        /// 「探过了，这个下游就是没有模型」，那是另一件事。
        /// </summary>
        /// <exception cref="TripoClientException">哨兵没被拒、回的不是 1004、或那句报错里读不出清单时抛。</exception>
        public IReadOnlyList<string> ProbeAllowedModelVersions()
        {
            var url = _baseUrl + "/generation/text-to-model";
            var body = "{\"prompt\":\"catalog probe\",\"model\":" + JsonSerializer.Serialize(CatalogProbeSentinel) + ",\"texture\":false,\"pbr\":false,\"face_limit\":3000}";
            Console.Error.WriteLine("BridgeTripo 探模型清单：POST " + url + " body=" + body + "（哨兵值必被拒，不花积分）");

            var call = Send(HttpMethod.Post, url, body, includeAuthorization: true);
            if (call.Succeeded)
            {
                // 哨兵被当成了合法模型——那就意味着我们**可能真提交了一个任务**，这是要花钱的。
                throw new TripoClientException(
                    "下游报错",
                    "探清单用的哨兵值没被下游拒绝（回了 2xx），可能真提交了一个任务——去 tripo 控制台看一眼，并把桥里的哨兵值换成一个更不可能合法的值",
                    retryable: false);
            }

            var responseText = call.ResponseText ?? "";
            var code = ReadResponseCode(responseText);
            if (code != 1004)
            {
                // 不是参数关的错（比如密钥无效的 1005、积分不足的 2010）：照它本来的错误码报，
                // 别把「密钥错」说成「清单读不出来」。
                throw new TripoClientException(call.Error.Code, call.Error.HumanText, call.Error.Retryable);
            }

            var message = ReadResponseMessage(responseText);
            var versions = ParseAllowedValues(message);
            if (versions.Count == 0)
            {
                throw new TripoClientException(
                    "下游报错",
                    "服务端回了 1004，但那句报错里读不出 allowed values：" + SafePreview(responseText),
                    retryable: false);
            }

            Console.Error.WriteLine("BridgeTripo 探到 " + versions.Count + " 个模型版本");
            return versions;
        }

        /// <summary>
        /// 从 1004 的报错里解析允许值清单：取「allowed values:」之后那一段，按逗号切、逐段去空白。
        /// 措辞与分隔符以真回包为准；读不出来时返回空列表，由调用方决定怎么报。
        /// </summary>
        /// <param name="message">服务端 message 原文。</param>
        public static IReadOnlyList<string> ParseAllowedValues(string message)
        {
            var values = new List<string>();
            var text = message ?? "";
            const string marker = "allowed values:";
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return values;
            }

            var tail = text.Substring(index + marker.Length).Trim();
            foreach (var part in tail.Split(','))
            {
                var value = part.Trim().Trim('\'', '"', '。', '.', ']', '[');
                if (value.Length > 0 && !values.Contains(value))
                {
                    values.Add(value);
                }
            }

            values.Sort(StringComparer.Ordinal);
            return values;
        }

        /// <summary>读回包顶层的 code；读不到给 0。</summary>
        /// <param name="responseText">回包原文。</param>
        private static int ReadResponseCode(string responseText)
        {
            try
            {
                using var document = JsonDocument.Parse(responseText);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.Number)
                {
                    return code.GetInt32();
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
            }

            return 0;
        }

        /// <summary>读回包顶层的 message；读不到给空串。</summary>
        /// <param name="responseText">回包原文。</param>
        private static string ReadResponseMessage(string responseText)
        {
            try
            {
                using var document = JsonDocument.Parse(responseText);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
            }

            return "";
        }

        /// <summary>下游账号余额：balance 是可用积分，frozen 是冻结中的。</summary>
        public sealed class BalanceReading
        {
            /// <summary>构造一份余额读数。</summary>
            /// <param name="balance">可用积分。</param>
            /// <param name="frozen">冻结中的积分。</param>
            public BalanceReading(double balance, double frozen)
            {
                Balance = balance;
                Frozen = frozen;
            }

            /// <summary>可用积分。</summary>
            public double Balance { get; }

            /// <summary>冻结中的积分。</summary>
            public double Frozen { get; }
        }

        /// <summary>
        /// 查账号余额：GET {base}/account/balance。
        /// 注意（决策 91）：余额**不是**就绪判据——余额非零也可能因别的原因提交失败，
        /// 这个读数只用来诊断「2010 到底是不是真没钱了」。
        /// </summary>
        public BalanceReading QueryBalance()
        {
            var url = _baseUrl + "/account/balance";
            var call = Send(HttpMethod.Get, url, null, includeAuthorization: true);
            if (!call.Succeeded)
            {
                throw new TripoClientException(call.Error.Code, call.Error.HumanText, call.Error.Retryable);
            }

            if (!TryExtractBalance(call.ResponseText, out var balance, out var frozen, out var reason))
            {
                throw new TripoClientException("下游报错", "余额响应解析不了：" + reason + "，响应原文：" + SafePreview(call.ResponseText), retryable: false);
            }

            return new BalanceReading(balance, frozen);
        }

        /// <summary>
        /// 提交 text_to_model 任务，返回 task_id。v3 端点：POST {base}/generation/text-to-model。
        /// </summary>
        /// <param name="prompt">提示词。</param>
        public string SubmitTask(string prompt)
        {
            var url = _baseUrl + "/generation/text-to-model";
            var body = BuildSubmitBody(prompt);
            Console.Error.WriteLine("BridgeTripo 将提交任务：POST " + url + " body=" + body + "（密钥只进 Authorization 头）");

            var call = Send(HttpMethod.Post, url, body, includeAuthorization: true);
            if (!call.Succeeded)
            {
                throw new TripoClientException(call.Error.Code, call.Error.HumanText, call.Error.Retryable);
            }

            if (!TryExtractTaskId(call.ResponseText, out var taskId, out var reason))
            {
                throw new TripoClientException("下游报错", "提交任务的响应里找不到 task_id：" + reason + "，响应原文：" + SafePreview(call.ResponseText), retryable: false);
            }

            Console.Error.WriteLine("BridgeTripo 已提交任务，task_id=" + taskId);
            return taskId;
        }

        /// <summary>
        /// 提交 image_to_model 任务，返回 task_id。v3 端点：POST {base}/generation/image-to-model。
        /// 参考图走「下游自己去取的 URL」这一路：实证过 file={"type":…,"url":…} 能过参数校验
        /// （拿不可达的 URL 试会回 1004「input image is not accessible」，拿真 URL 试直接进 2010 积分关）。
        /// v3 没有 /upload 与 /upload/sts 端点（两个都实证回 4001），所以本地图片要先有个可公开访问的地址。
        /// </summary>
        /// <param name="imageUrl">参考图地址，下游要能直接 GET 到。</param>
        /// <param name="imageType">图片类型，如 png / jpg。</param>
        public string SubmitImageTask(string imageUrl, string imageType)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new TripoClientException("请求不合协议", "参考图地址是空的", retryable: false);
            }

            var url = _baseUrl + "/generation/image-to-model";
            var body = BuildImageSubmitBody(imageUrl, imageType);
            Console.Error.WriteLine("BridgeTripo 将提交图生模型任务：POST " + url + " body=" + body + "（密钥只进 Authorization 头）");

            var call = Send(HttpMethod.Post, url, body, includeAuthorization: true);
            if (!call.Succeeded)
            {
                throw new TripoClientException(call.Error.Code, call.Error.HumanText, call.Error.Retryable);
            }

            if (!TryExtractTaskId(call.ResponseText, out var taskId, out var reason))
            {
                throw new TripoClientException("下游报错", "提交任务的响应里找不到 task_id：" + reason + "，响应原文：" + SafePreview(call.ResponseText), retryable: false);
            }

            Console.Error.WriteLine("BridgeTripo 已提交图生模型任务，task_id=" + taskId);
            return taskId;
        }

        /// <summary>
        /// 轮询任务直到终态或总超时；返回终态判定与模型 URL。
        /// 未知状态当失败原样带出（TripoTaskState），不默默继续轮询（决策 42）。
        /// </summary>
        /// <param name="taskId">task_id。</param>
        public TripoTaskQuery PollUntilFinal(string taskId)
        {
            var stopwatch = Stopwatch.StartNew();
            var queryCount = 0;
            while (true)
            {
                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds >= _timeoutSeconds)
                {
                    throw new TripoClientException("超时", $"轮询任务超过 {_timeoutSeconds} 秒仍未到终态，已放弃（最后一次状态见 stderr）", retryable: true);
                }

                var query = QueryTask(taskId);
                queryCount++;
                Console.Error.WriteLine($"BridgeTripo 第 {queryCount} 次轮询：status={query.State.StatusText}（已用时 {elapsedSeconds:F0} 秒）");
                if (query.State.IsFinal)
                {
                    return query;
                }

                System.Threading.Thread.Sleep(PollIntervalMilliseconds);
            }
        }

        /// <summary>查一次任务状态：GET {base}/tasks/&lt;task_id&gt;（v3 端点），解析状态与模型 URL。</summary>
        public TripoTaskQuery QueryTask(string taskId)
        {
            var url = _baseUrl + "/tasks/" + Uri.EscapeDataString(taskId);
            var call = Send(HttpMethod.Get, url, null, includeAuthorization: true);
            if (!call.Succeeded)
            {
                throw new TripoClientException(call.Error.Code, call.Error.HumanText, call.Error.Retryable);
            }

            if (!TryExtractStatus(call.ResponseText, out var status, out var statusReason))
            {
                throw new TripoClientException("下游报错", "任务查询响应里找不到 status：" + statusReason + "，响应原文：" + SafePreview(call.ResponseText), retryable: false);
            }

            var state = TripoTaskState.Classify(status);
            var modelUrl = "";
            if (state.Succeeded)
            {
                TryExtractModelUrl(call.ResponseText, out modelUrl, out _);
            }

            return new TripoTaskQuery(state, modelUrl);
        }

        /// <summary>下载模型字节；URL 是下游签发的，5 分钟过期，直接 GET（不带鉴权头）。</summary>
        public byte[] DownloadModel(string modelUrl)
        {
            if (string.IsNullOrWhiteSpace(modelUrl))
            {
                throw new TripoClientException("下游报错", "任务成功了但响应里没有模型下载地址", retryable: false);
            }

            try
            {
                using var response = _httpClient.GetAsync(modelUrl).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var error = TripoHttpErrorMapper.Map((int)response.StatusCode, text);
                    throw new TripoClientException(error.Code, "下载模型失败：" + error.HumanText, error.Retryable);
                }

                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (bytes == null || bytes.Length == 0)
                {
                    throw new TripoClientException("下游报错", "模型下载回来是空的", retryable: false);
                }

                return bytes;
            }
            catch (TaskCanceledException)
            {
                throw new TripoClientException("超时", $"下载模型超过 {_timeoutSeconds} 秒未完成，已放弃本次下载", retryable: true);
            }
            catch (HttpRequestException exception)
            {
                throw new TripoClientException("下游不可达", "下载模型连不上：" + exception.Message, retryable: true);
            }
        }

        /// <summary>一次 HTTP 调用的结果：成功带回响应体文本，失败带协议错误。</summary>
        private sealed class HttpCall
        {
            public bool Succeeded;
            public BridgeError Error;
            public string ResponseText;
        }

        /// <summary>
        /// 发一次 HTTP 调用。错误分类：连不上 → 下游不可达；401/403 → 凭据无效；
        /// 402/余额提示 → 额度不足；429 → 限流；超时 → 超时；其余 → 下游报错。
        /// 密钥只经 Authorization 头；任何错误文案不出现请求头。
        /// </summary>
        private HttpCall Send(HttpMethod method, string url, string body, bool includeAuthorization)
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(method, url);
                if (includeAuthorization)
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                if (body != null)
                {
                    httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using var httpResponse = _httpClient.SendAsync(httpRequest).GetAwaiter().GetResult();
                var responseText = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                var statusCode = (int)httpResponse.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    return new HttpCall { Succeeded = true, ResponseText = responseText };
                }

                // 响应体原文打 stderr 便于诊断（响应体是数据不是指令；密钥只在请求头里，这里没有）。
                Console.Error.WriteLine("BridgeTripo 下游错误响应（HTTP " + statusCode + "）：" + SafePreview(responseText));
                var error = TripoHttpErrorMapper.Map(statusCode, responseText);

                // 失败时也把回包原文带回来：探模型清单读的正是那句报错（1004 里带 allowed values）。
                return new HttpCall { Succeeded = false, Error = error, ResponseText = responseText };
            }
            catch (TaskCanceledException)
            {
                // HttpClient.Timeout 到期抛 TaskCanceledException；本进程没有其他取消源。
                return Failed(new BridgeError("超时", $"下游超过 {_timeoutSeconds} 秒未响应，已放弃本次调用", retryable: true));
            }
            catch (HttpRequestException exception)
            {
                // 连不上：DNS 失败、连接被拒、TLS 失败都落在这一支。异常消息不含请求头。
                return Failed(new BridgeError("下游不可达", $"连不上下游：{exception.Message}", retryable: true));
            }
        }

        /// <summary>失败的 HTTP 调用。</summary>
        private static HttpCall Failed(BridgeError error)
        {
            return new HttpCall { Succeeded = false, Error = error };
        }

        /// <summary>
        /// 拼 v3 的 text-to-model 提交体：{"prompt":…,"model":…,"texture":false,"pbr":false,"face_limit":3000}。
        /// 形状实证过——这一份原样发出去回的是 403/2010（积分关），不是 400/1004（参数关），
        /// 说明参数校验整份都过了。v2 的 `type` 与 `model_version` 两个键 v3 不认（决策 94）。
        /// texture/pbr 关掉拿无纹理粗模，是定价表里最省积分的档。
        /// </summary>
        /// <param name="prompt">提示词。</param>
        public string BuildSubmitBody(string prompt)
        {
            var builder = new StringBuilder();
            builder.Append("{\"prompt\":");
            builder.Append(JsonSerializer.Serialize(prompt));
            builder.Append(",\"model\":");
            builder.Append(JsonSerializer.Serialize(_modelVersion));
            builder.Append(",\"texture\":false,\"pbr\":false,\"face_limit\":3000}");
            return builder.ToString();
        }

        /// <summary>
        /// 拼 v3 的 image-to-model 提交体：{"model":…,"file":{"type":…,"url":…},…}。
        /// 形状同样实证过：真 URL 回 403/2010，不可达 URL 回 400/1004「input image is not accessible」。
        /// </summary>
        /// <param name="imageUrl">参考图地址。</param>
        /// <param name="imageType">图片类型，如 png / jpg；空串按 png。</param>
        public string BuildImageSubmitBody(string imageUrl, string imageType)
        {
            var type = string.IsNullOrWhiteSpace(imageType) ? "png" : imageType.Trim().TrimStart('.').ToLowerInvariant();
            var builder = new StringBuilder();
            builder.Append("{\"model\":");
            builder.Append(JsonSerializer.Serialize(_modelVersion));
            builder.Append(",\"file\":{\"type\":");
            builder.Append(JsonSerializer.Serialize(type));
            builder.Append(",\"url\":");
            builder.Append(JsonSerializer.Serialize(imageUrl));
            builder.Append("},\"texture\":false,\"pbr\":false,\"face_limit\":3000}");
            return builder.ToString();
        }

        /// <summary>从余额响应里取 balance / frozen：先试 data.balance，再试顶层 balance。</summary>
        private static bool TryExtractBalance(string responseText, out double balance, out double frozen, out string reason)
        {
            balance = 0;
            frozen = 0;
            using var document = ParseOrFail(responseText, "余额响应", out reason);
            if (document == null)
            {
                return false;
            }

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "余额响应顶层不是对象";
                return false;
            }

            var scope = root;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                scope = data;
            }

            if (!scope.TryGetProperty("balance", out var balanceElement) || balanceElement.ValueKind != JsonValueKind.Number)
            {
                reason = "既没有 data.balance 也没有顶层 balance";
                return false;
            }

            balance = balanceElement.GetDouble();
            if (scope.TryGetProperty("frozen", out var frozenElement) && frozenElement.ValueKind == JsonValueKind.Number)
            {
                frozen = frozenElement.GetDouble();
            }

            return true;
        }

        /// <summary>从提交响应里取 task_id：先试 data.task_id，再试顶层 task_id。</summary>
        private static bool TryExtractTaskId(string responseText, out string taskId, out string reason)
        {
            taskId = "";
            reason = "";
            using var document = ParseOrFail(responseText, "提交响应", out reason);
            if (document == null)
            {
                return false;
            }

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "提交响应顶层不是对象";
                return false;
            }

            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("task_id", out var nestedTaskId)
                && nestedTaskId.ValueKind == JsonValueKind.String)
            {
                taskId = nestedTaskId.GetString() ?? "";
                return taskId.Length > 0;
            }

            if (root.TryGetProperty("task_id", out var topTaskId) && topTaskId.ValueKind == JsonValueKind.String)
            {
                taskId = topTaskId.GetString() ?? "";
                return taskId.Length > 0;
            }

            reason = "既没有 data.task_id 也没有顶层 task_id";
            return false;
        }

        /// <summary>从任务查询响应里取 status：先试 data.status，再试顶层 status。</summary>
        private static bool TryExtractStatus(string responseText, out string status, out string reason)
        {
            status = "";
            reason = "";
            using var document = ParseOrFail(responseText, "任务查询响应", out reason);
            if (document == null)
            {
                return false;
            }

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "任务查询响应顶层不是对象";
                return false;
            }

            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("status", out var nestedStatus)
                && nestedStatus.ValueKind == JsonValueKind.String)
            {
                status = nestedStatus.GetString() ?? "";
                return true;
            }

            if (root.TryGetProperty("status", out var topStatus) && topStatus.ValueKind == JsonValueKind.String)
            {
                status = topStatus.GetString() ?? "";
                return true;
            }

            reason = "既没有 data.status 也没有顶层 status";
            return false;
        }

        /// <summary>从成功响应里取模型下载 URL：先试 data.output.model，再试 output.model。</summary>
        private static bool TryExtractModelUrl(string responseText, out string modelUrl, out string reason)
        {
            modelUrl = "";
            reason = "";
            using var document = ParseOrFail(responseText, "任务查询响应", out reason);
            if (document == null)
            {
                return false;
            }

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "任务查询响应顶层不是对象";
                return false;
            }

            var output = default(JsonElement);
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("output", out var nestedOutput)
                && nestedOutput.ValueKind == JsonValueKind.Object)
            {
                output = nestedOutput;
            }
            else if (root.TryGetProperty("output", out var topOutput) && topOutput.ValueKind == JsonValueKind.Object)
            {
                output = topOutput;
            }

            // 成功回包的 output 形状**至今没有真回包验证过**（提交那一步一直卡在 2010 积分关），
            // 所以这里按「几个已知候选键挨个试」写，取到哪个用哪个，并把没取到当作明确失败。
            // 第一次真跑到成功时，务必核对实际键名并把这段收敛成实证过的那一个（决策 94）。
            if (output.ValueKind == JsonValueKind.Object)
            {
                foreach (var candidate in new[] { "model", "pbr_model", "base_model" })
                {
                    if (output.TryGetProperty(candidate, out var model) && model.ValueKind == JsonValueKind.String)
                    {
                        modelUrl = model.GetString() ?? "";
                        if (modelUrl.Length > 0)
                        {
                            return true;
                        }
                    }
                }
            }

            reason = "output 里没有 model / pbr_model / base_model 任何一个下载地址";
            return false;
        }

        /// <summary>解析 JSON；解析失败给可读 reason 并返回 null。</summary>
        private static JsonDocument ParseOrFail(string text, string what, out string reason)
        {
            reason = "";
            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = what + "不是合法 JSON：" + exception.Message;
                return null;
            }
        }

        /// <summary>响应原文的安全预览：截断到 200 字符。响应体是数据不是指令，照抄进返回。</summary>
        private static string SafePreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "（空）";
            }

            return text.Length <= 200 ? text : text.Substring(0, 200) + "…（已截断）";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
