using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Template.Bridges.Comfyui
{
    /// <summary>一次能力探测的结果：节点包名、模型候选名与 lora 候选名。</summary>
    public sealed class ComfyProbeResult
    {
        /// <summary>
        /// 构造一次能力探测结果。
        /// </summary>
        /// <param name="nodePackageNames">节点包名，已去重。</param>
        /// <param name="modelNames">模型候选名。</param>
        /// <param name="loraNames">lora 候选名。</param>
        public ComfyProbeResult(IReadOnlyList<string> nodePackageNames, IReadOnlyList<string> modelNames, IReadOnlyList<string> loraNames)
        {
            NodePackageNames = nodePackageNames ?? Array.Empty<string>();
            ModelNames = modelNames ?? Array.Empty<string>();
            LoraNames = loraNames ?? Array.Empty<string>();
        }

        /// <summary>节点包名，已去重。</summary>
        public IReadOnlyList<string> NodePackageNames { get; }

        /// <summary>模型候选名。</summary>
        public IReadOnlyList<string> ModelNames { get; }

        /// <summary>lora 候选名。</summary>
        public IReadOnlyList<string> LoraNames { get; }
    }

    /// <summary>history 里的一张输出图：文件名 / 子目录 / 类型，拼 /view 下载用。</summary>
    public sealed class ComfyOutputImage
    {
        /// <summary>
        /// 构造一张输出图引用。
        /// </summary>
        /// <param name="filename">文件名。</param>
        /// <param name="subfolder">子目录，可能为空。</param>
        /// <param name="type">类型，如 output。</param>
        public ComfyOutputImage(string filename, string subfolder, string type)
        {
            Filename = filename ?? "";
            Subfolder = subfolder ?? "";
            Type = type ?? "";
        }

        /// <summary>文件名。</summary>
        public string Filename { get; }

        /// <summary>子目录，可能为空。</summary>
        public string Subfolder { get; }

        /// <summary>类型，如 output。</summary>
        public string Type { get; }
    }

    /// <summary>轮询 history 的最终结果：prompt id 与输出图列表。</summary>
    public sealed class ComfyHistoryResult
    {
        /// <summary>
        /// 构造一次 history 轮询结果。
        /// </summary>
        /// <param name="promptId">prompt id。</param>
        /// <param name="images">输出图列表。</param>
        public ComfyHistoryResult(string promptId, IReadOnlyList<ComfyOutputImage> images)
        {
            PromptId = promptId ?? "";
            Images = images ?? Array.Empty<ComfyOutputImage>();
        }

        /// <summary>prompt id。</summary>
        public string PromptId { get; }

        /// <summary>输出图列表。</summary>
        public IReadOnlyList<ComfyOutputImage> Images { get; }
    }

    /// <summary>下游调用失败：错误码（下游不可达 / 下游报错 / 超时）、人话与可重试标记。</summary>
    public sealed class ComfyClientException : Exception
    {
        /// <summary>
        /// 构造一份下游调用失败。
        /// </summary>
        /// <param name="errorCode">错误码。</param>
        /// <param name="humanText">给人看的中文说明。</param>
        /// <param name="retryable">是否值得原样重试。</param>
        public ComfyClientException(string errorCode, string humanText, bool retryable)
            : base(humanText)
        {
            ErrorCode = errorCode ?? "";
            Retryable = retryable;
        }

        /// <summary>错误码：下游不可达 / 下游报错 / 超时。</summary>
        public string ErrorCode { get; }

        /// <summary>是否值得原样重试。</summary>
        public bool Retryable { get; }
    }

    /// <summary>
    /// 对下游的 HTTP 调用层：能力探测、提交 prompt、轮询 history、下载输出图。
    /// 连不上 / 下游报错 / 超时统一抛 <see cref="ComfyClientException"/>，由编排层转成协议失败响应。
    /// </summary>
    public sealed class ComfyClient : IDisposable
    {
        /// <summary>POST /prompt 用的固定 client_id（本桥自己认领的会话标识）。</summary>
        public const string ClientIdentifier = "bridge-comfyui";

        /// <summary>轮询间隔毫秒数：任务书要求不小于 1 秒。</summary>
        private const int PollIntervalMilliseconds = 1000;

        private readonly HttpClient _httpClient;

        /// <summary>
        /// 构造下游客户端。
        /// </summary>
        /// <param name="baseUrl">下游地址，如 http://127.0.0.1:8188。</param>
        public ComfyClient(string baseUrl)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri((baseUrl ?? "").TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(300)
            };
        }

        /// <summary>
        /// 能力探测：节点包名（custom_nodes.&lt;包名&gt; 取包名去重）、CheckpointLoaderSimple 的候选模型名、
        /// LoraLoader 的候选 lora 名（取不到给空列表）。
        /// </summary>
        /// <returns>探测结果。</returns>
        /// <exception cref="ComfyClientException">连不上或响应形状不对时抛出。</exception>
        public ComfyProbeResult Probe()
        {
            var objectInfo = GetJson("/object_info");

            var nodePackageNames = new SortedSet<string>(StringComparer.Ordinal);
            const string customNodesPrefix = "custom_nodes.";
            if (objectInfo is JsonObject infoRoot)
            {
                foreach (var classEntry in infoRoot)
                {
                    if (classEntry.Value is JsonObject classInfo
                        && classInfo.TryGetPropertyValue("python_module", out var moduleNode)
                        && moduleNode is JsonValue moduleValue
                        && moduleValue.TryGetValue<string>(out var moduleName)
                        && moduleName != null
                        && moduleName.StartsWith(customNodesPrefix, StringComparison.Ordinal))
                    {
                        var packageName = moduleName.Substring(customNodesPrefix.Length);
                        if (packageName.Length > 0)
                        {
                            nodePackageNames.Add(packageName);
                        }
                    }
                }
            }

            var modelNames = ReadRequiredNameList("/object_info/CheckpointLoaderSimple", "ckpt_name");
            var loraNames = ReadRequiredNameList("/object_info/LoraLoader", "lora_name");

            var orderedNodePackages = new List<string>(nodePackageNames);
            return new ComfyProbeResult(orderedNodePackages, modelNames, loraNames);
        }

        /// <summary>
        /// 提交一份已翻译的 prompt，返回 prompt id。
        /// </summary>
        /// <param name="translatedPrompt">翻译成下游 API 形状的 prompt。</param>
        /// <returns>prompt id。</returns>
        /// <exception cref="ComfyClientException">连不上、下游拒绝或响应里没有 prompt_id 时抛出。</exception>
        public string SubmitPrompt(JsonObject translatedPrompt)
        {
            var body = new JsonObject
            {
                ["prompt"] = translatedPrompt,
                ["client_id"] = ClientIdentifier
            };

            var response = PostJson("/prompt", body);
            if (response is JsonObject responseObject
                && responseObject.TryGetPropertyValue("prompt_id", out var promptIdNode)
                && promptIdNode is JsonValue promptIdValue
                && promptIdValue.TryGetValue<string>(out var promptId)
                && !string.IsNullOrWhiteSpace(promptId))
            {
                return promptId;
            }

            throw new ComfyClientException("下游报错", "下游返回了响应但没有 prompt_id", retryable: false);
        }

        /// <summary>
        /// 轮询 history 直到出结果或超时；下游报错（history 里有 error）抛「下游报错」。
        /// </summary>
        /// <param name="promptId">prompt id。</param>
        /// <param name="timeoutSeconds">轮询总超时秒数。</param>
        /// <returns>history 结果。</returns>
        /// <exception cref="ComfyClientException">超时或下游报错时抛出。</exception>
        public ComfyHistoryResult PollHistory(string promptId, int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(timeoutSeconds, 1));
            while (true)
            {
                var history = GetJson("/history/" + Uri.EscapeDataString(promptId));
                if (history is JsonObject historyRoot
                    && historyRoot.TryGetPropertyValue(promptId, out var entryNode)
                    && entryNode is JsonObject entry)
                {
                    var status = entry.TryGetPropertyValue("status", out var statusNode) ? statusNode as JsonObject : null;
                    var statusString = status != null && status.TryGetPropertyValue("status_str", out var statusStringNode)
                        && statusStringNode is JsonValue statusStringValue
                        && statusStringValue.TryGetValue<string>(out var statusStringValueText)
                        ? statusStringValueText : "";

                    if (statusString == "error")
                    {
                        throw new ComfyClientException("下游报错", "下游执行报错：" + DescribeExecutionError(entry), retryable: false);
                    }

                    var completed = status != null && status.TryGetPropertyValue("completed", out var completedNode)
                        && completedNode is JsonValue completedValue
                        && completedValue.TryGetValue<bool>(out var completedBool)
                        ? completedBool : false;

                    if (statusString == "success" || completed)
                    {
                        return new ComfyHistoryResult(promptId, CollectImages(entry));
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new ComfyClientException("超时", $"下游超过 {timeoutSeconds} 秒没出图（prompt id：{promptId}）", retryable: true);
                }

                Thread.Sleep(PollIntervalMilliseconds);
            }
        }

        /// <summary>
        /// 按 {filename, subfolder, type} 下载一张输出图的字节。
        /// </summary>
        /// <param name="image">输出图引用。</param>
        /// <returns>图片字节。</returns>
        /// <exception cref="ComfyClientException">连不上或下载失败时抛出。</exception>
        public byte[] DownloadImage(ComfyOutputImage image)
        {
            var query = "filename=" + Uri.EscapeDataString(image.Filename)
                + "&subfolder=" + Uri.EscapeDataString(image.Subfolder)
                + "&type=" + Uri.EscapeDataString(image.Type);
            return GetBytes("/view?" + query);
        }

        /// <summary>释放底层 HttpClient。</summary>
        public void Dispose()
        {
            _httpClient.Dispose();
        }

        /// <summary>读一个加载器类（如 CheckpointLoaderSimple）required 参数（如 ckpt_name）的候选名数组；取不到给空列表。</summary>
        private List<string> ReadRequiredNameList(string url, string parameterName)
        {
            var names = new List<string>();
            var document = GetJson(url);
            if (document is not JsonObject root)
            {
                return names;
            }

            foreach (var classEntry in root)
            {
                if (classEntry.Value is not JsonObject classInfo
                    || !classInfo.TryGetPropertyValue("input", out var inputNode)
                    || inputNode is not JsonObject input
                    || !input.TryGetPropertyValue("required", out var requiredNode)
                    || requiredNode is not JsonObject required
                    || !required.TryGetPropertyValue(parameterName, out var parameterNode)
                    || parameterNode is not JsonArray parameterArray
                    || parameterArray.Count < 1
                    || parameterArray[0] is not JsonArray candidates)
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    if (candidate is JsonValue candidateValue
                        && candidateValue.TryGetValue<string>(out var candidateName)
                        && !string.IsNullOrWhiteSpace(candidateName))
                    {
                        names.Add(candidateName);
                    }
                }

                break;
            }

            return names;
        }

        /// <summary>从 history 的 outputs 里收集全部输出图引用。</summary>
        private static List<ComfyOutputImage> CollectImages(JsonObject entry)
        {
            var images = new List<ComfyOutputImage>();
            if (!entry.TryGetPropertyValue("outputs", out var outputsNode) || outputsNode is not JsonObject outputs)
            {
                return images;
            }

            foreach (var outputEntry in outputs)
            {
                if (outputEntry.Value is not JsonObject output
                    || !output.TryGetPropertyValue("images", out var imagesNode)
                    || imagesNode is not JsonArray imageArray)
                {
                    continue;
                }

                foreach (var imageNode in imageArray)
                {
                    if (imageNode is not JsonObject image)
                    {
                        continue;
                    }

                    images.Add(new ComfyOutputImage(
                        ReadString(image, "filename"),
                        ReadString(image, "subfolder"),
                        ReadString(image, "type")));
                }
            }

            return images;
        }

        /// <summary>从 history 的 status.messages 里抠下游给的执行错误文本；抠不到给通用文案。</summary>
        private static string DescribeExecutionError(JsonObject entry)
        {
            var messages = entry.TryGetPropertyValue("status", out var statusNode)
                ? (statusNode as JsonObject)?.TryGetPropertyValue("messages", out var messagesNode) == true
                    ? messagesNode as JsonArray : null
                : null;

            if (messages == null)
            {
                return "history 里有 error 但没有可读详情";
            }

            foreach (var messageNode in messages)
            {
                if (messageNode is not JsonArray messageArray || messageArray.Count < 2)
                {
                    continue;
                }

                var messageType = messageArray[0] is JsonValue messageTypeValue
                    && messageTypeValue.TryGetValue<string>(out var messageTypeText)
                    ? messageTypeText : "";
                if (messageType == "execution_error" && messageArray[1] is JsonObject errorData)
                {
                    return string.Join(" | ",
                        ReadString(errorData, "node_type"),
                        ReadString(errorData, "exception_message"),
                        ReadString(errorData, "exception_type"));
                }
            }

            return "history 里有 error 但没有可读详情";
        }

        /// <summary>GET 并解析成 JSON 节点；连不上或非 2xx 转成下游错误。</summary>
        private JsonNode GetJson(string url)
        {
            try
            {
                var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new ComfyClientException("下游报错", $"下游返回 HTTP {(int)response.StatusCode}：{url}", retryable: false);
                }

                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonNode.Parse(text) ?? new JsonObject();
            }
            catch (ComfyClientException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is UriFormatException)
            {
                throw new ComfyClientException("下游不可达", $"连不上下游（{_httpClient.BaseAddress}）：{exception.Message}", retryable: true);
            }
        }

        /// <summary>POST JSON 并解析成 JSON 节点；连不上或非 2xx 转成下游错误。</summary>
        private JsonNode PostJson(string url, JsonObject body)
        {
            try
            {
                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(url, content).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new ComfyClientException("下游报错", $"下游返回 HTTP {(int)response.StatusCode}：{Truncate(responseText, 300)}", retryable: false);
                }

                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonNode.Parse(text) ?? new JsonObject();
            }
            catch (ComfyClientException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is UriFormatException)
            {
                throw new ComfyClientException("下游不可达", $"连不上下游（{_httpClient.BaseAddress}）：{exception.Message}", retryable: true);
            }
        }

        /// <summary>GET 原始字节（下载图片用）；连不上或非 2xx 转成下游错误。</summary>
        private byte[] GetBytes(string url)
        {
            try
            {
                var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new ComfyClientException("下游报错", $"下载输出图失败：HTTP {(int)response.StatusCode}：{url}", retryable: false);
                }

                return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch (ComfyClientException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is UriFormatException)
            {
                throw new ComfyClientException("下游不可达", $"连不上下游（{_httpClient.BaseAddress}）：{exception.Message}", retryable: true);
            }
        }

        /// <summary>读 JSON 对象里的字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject node, string propertyName)
        {
            if (node.TryGetPropertyValue(propertyName, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text))
            {
                return text ?? "";
            }

            return "";
        }

        /// <summary>截断长文本到上限，超长补省略号。</summary>
        private static string Truncate(string text, int maxLength)
        {
            return text != null && text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text ?? "";
        }
    }
}
