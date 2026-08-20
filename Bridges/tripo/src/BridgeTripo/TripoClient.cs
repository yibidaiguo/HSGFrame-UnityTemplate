using System;
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
    /// 纯函数状态机：把下游 GET /task/&lt;id&gt; 响应里的 status 字符串判定成「继续轮询 / 终态成功 / 终态失败」。
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
            if (statusCode == 401)
            {
                return new BridgeError("凭据无效", $"下游返回 HTTP {statusCode}，密钥无效或无权访问", retryable: false);
            }

            // tripo 的 403 有两种：code 1005（无权限）与 code 2010（积分不足）。
            // 响应体里有 code 时按 code 分，没 body 才退化成凭据无效。
            if (statusCode == 403)
            {
                var code = ReadErrorCode(responseText);
                if (code == 2010)
                {
                    return new BridgeError("额度不足", "下游返回 HTTP 403：账号积分/配额不足，不是代码坏了。请到下游控制台确认剩余积分", retryable: false);
                }

                if (code == 1005)
                {
                    return new BridgeError("凭据无效", "下游返回 HTTP 403：该密钥无权访问此资源", retryable: false);
                }

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

        /// <summary>提交时用的模型版本：H3 v3.0（定价表里 text_to_model 无纹理 10 积分，最省的档位）。</summary>
        private const string SubmitModelVersion = "v3.0-20250812";

        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly int _timeoutSeconds;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// 构造对下游的 HTTP 客户端。
        /// </summary>
        /// <param name="baseUrl">下游地址，如 https://api.tripo3d.ai/v2/openapi。</param>
        /// <param name="apiKey">模型生成密钥，只进 Authorization 头。</param>
        /// <param name="timeoutSeconds">单次 HTTP 调用的超时秒数。</param>
        public TripoClient(string baseUrl, string apiKey, int timeoutSeconds)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _apiKey = apiKey ?? "";
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
        }

        /// <summary>
        /// 提交 text_to_model 任务，返回 task_id。
        /// </summary>
        /// <param name="prompt">提示词。</param>
        public string SubmitTask(string prompt)
        {
            var url = _baseUrl + "/task";
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

        /// <summary>查一次任务状态：GET /task/&lt;task_id&gt;，解析状态与模型 URL。</summary>
        public TripoTaskQuery QueryTask(string taskId)
        {
            var url = _baseUrl + "/task/" + Uri.EscapeDataString(taskId);
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
                return new HttpCall { Succeeded = false, Error = error };
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

        /// <summary>拼 text_to_model 提交体。字段名来自 tripo 官方文档；texture/pbr 关掉拿无纹理粗模（最省积分的档）。</summary>
        private static string BuildSubmitBody(string prompt)
        {
            var builder = new StringBuilder();
            builder.Append("{\"type\":\"text_to_model\",\"prompt\":");
            builder.Append(JsonSerializer.Serialize(prompt));
            builder.Append(",\"model_version\":");
            builder.Append(JsonSerializer.Serialize(SubmitModelVersion));
            builder.Append(",\"texture\":false,\"pbr\":false,\"face_limit\":3000}");
            return builder.ToString();
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

            if (output.ValueKind == JsonValueKind.Object
                && output.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.String)
            {
                modelUrl = model.GetString() ?? "";
                return modelUrl.Length > 0;
            }

            reason = "output 里没有 model 下载地址";
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
