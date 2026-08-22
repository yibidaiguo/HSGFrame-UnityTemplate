using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Template.Bridges.Oaiimage
{
    /// <summary>
    /// 下游线上生图服务报的错：带协议错误码与「值不值得重试」，直接翻译成 BridgeResponse。
    /// 消息里**永远不许出现密钥**——长度和前缀也不许（决策 5、78）。
    /// </summary>
    public sealed class ImageBridgeException : Exception
    {
        /// <summary>
        /// 构造一份下游错误。
        /// </summary>
        /// <param name="errorCode">协议错误码，如「下游不可达」「凭据无效」。</param>
        /// <param name="message">给人看的中文说明；不许带密钥。</param>
        /// <param name="retryable">这次失败是否值得原样重试。</param>
        public ImageBridgeException(string errorCode, string message, bool retryable)
            : base(message)
        {
            ErrorCode = errorCode ?? "";
            Retryable = retryable;
        }

        /// <summary>协议错误码。</summary>
        public string ErrorCode { get; }

        /// <summary>这次失败是否值得原样重试。</summary>
        public bool Retryable { get; }
    }

    /// <summary>下游回来的一张图：字节内容与它是从哪种字段取到的（b64_json 还是 url）。</summary>
    public sealed class GeneratedImage
    {
        /// <summary>
        /// 构造一张回传的图。
        /// </summary>
        /// <param name="bytes">图片字节。</param>
        /// <param name="sourceFieldName">取自哪个字段：b64_json 或 url。</param>
        public GeneratedImage(byte[] bytes, string sourceFieldName)
        {
            Bytes = bytes ?? Array.Empty<byte>();
            SourceFieldName = sourceFieldName ?? "";
        }

        /// <summary>图片字节。</summary>
        public byte[] Bytes { get; }

        /// <summary>取自哪个字段：b64_json 或 url。写进溯源边车的机检结果，出问题时能对上是哪一路。</summary>
        public string SourceFieldName { get; }
    }

    /// <summary>
    /// OpenAI 兼容图像接口的客户端：GET /models、POST /images/generations、POST /images/edits。
    ///
    /// 密钥红线（决策 5、78）：密钥只出现在 Authorization 头里——不进日志、不进异常消息、
    /// 不进返回载荷，长度和前缀也不许。下载 url 那一路用**另一个不带任何请求头的 HttpClient**：
    /// 图片 URL 常常指向对象存储的另一个域，把 Authorization 带过去等于把密钥发给第三方。
    ///
    /// **response_format 一个字都不发**：gpt-image-1 不认这个参数（传了报未知参数），
    /// 它恒回 b64_json；dall-e-3 认它、且默认回 url。中转背后挂什么模型不由我们决定，
    /// 所以请求侧不表态，解析侧两种都吃。
    /// </summary>
    public sealed class ImageClient : IDisposable
    {
        /// <summary>发给下游的 HttpClient，请求逐条带 Authorization 头。</summary>
        private readonly HttpClient _client;

        /// <summary>下载图片 URL 用的 HttpClient，**永远不带任何认证头**。</summary>
        private readonly HttpClient _downloadClient;

        /// <summary>下游地址，已去掉结尾的斜杠。</summary>
        private readonly string _baseUrl;

        /// <summary>密钥；只往 Authorization 头里塞，别的地方一律不碰。</summary>
        private readonly string _secretKey;

        /// <summary>超时秒数，报错人话里要说清等了多久。</summary>
        private readonly int _timeoutSeconds;

        /// <summary>
        /// 构造一个客户端。
        /// </summary>
        /// <param name="baseUrl">下游地址，形如 https://host/v1。</param>
        /// <param name="secretKey">密钥，只进 Authorization 头。</param>
        /// <param name="timeoutSeconds">超时秒数，小于 1 时按 1 算。</param>
        public ImageClient(string baseUrl, string secretKey, int timeoutSeconds)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _secretKey = secretKey ?? "";
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(_timeoutSeconds) };
            _downloadClient = new HttpClient { Timeout = TimeSpan.FromSeconds(_timeoutSeconds) };
        }

        /// <summary>
        /// GET /models：把下游列出来的模型 id 拿回来。这是能力探测那一路，不产图、不花钱。
        /// </summary>
        public IReadOnlyList<string> ListModelNames()
        {
            var responseText = SendAndReadText(HttpMethod.Get, _baseUrl + "/models", null);
            var names = new List<string>();

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseText);
            }
            catch (JsonException exception)
            {
                throw new ImageBridgeException("下游报错", $"/models 回来的不是合法 JSON：{exception.Message}", retryable: false);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    throw new ImageBridgeException("下游报错", "/models 回来的 JSON 里没有「data」数组", retryable: false);
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var identifier)
                        && identifier.ValueKind == JsonValueKind.String)
                    {
                        names.Add(identifier.GetString() ?? "");
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// POST /images/generations：文生图。
        /// </summary>
        /// <param name="modelName">模型名。</param>
        /// <param name="prompt">提示词。</param>
        /// <param name="variantCount">要几张。</param>
        /// <param name="size">尺寸，形如 1024x1024；空串表示不发这个参数，由下游取默认。</param>
        public IReadOnlyList<GeneratedImage> Generate(string modelName, string prompt, int variantCount, string size)
        {
            var body = new StringBuilder();
            body.Append("{\"model\":");
            body.Append(JsonSerializer.Serialize(modelName ?? ""));
            body.Append(",\"prompt\":");
            body.Append(JsonSerializer.Serialize(prompt ?? ""));
            body.Append(",\"n\":");
            body.Append(Math.Max(1, variantCount).ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(size))
            {
                body.Append(",\"size\":");
                body.Append(JsonSerializer.Serialize(size));
            }

            body.Append('}');

            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            var responseText = SendAndReadText(HttpMethod.Post, _baseUrl + "/images/generations", content);
            return ParseImages(responseText, "/images/generations");
        }

        /// <summary>
        /// POST /images/edits：图生图，走 multipart/form-data，字段 image + prompt + model + n + size。
        /// </summary>
        /// <param name="modelName">模型名。</param>
        /// <param name="prompt">提示词。</param>
        /// <param name="variantCount">要几张。</param>
        /// <param name="size">尺寸，形如 1024x1024；空串表示不发这个参数。</param>
        /// <param name="referenceImagePath">参考图的本地路径。</param>
        public IReadOnlyList<GeneratedImage> Edit(string modelName, string prompt, int variantCount, string size, string referenceImagePath)
        {
            byte[] imageBytes;
            try
            {
                imageBytes = File.ReadAllBytes(referenceImagePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new ImageBridgeException("请求不合协议", $"读参考图失败：{referenceImagePath}：{exception.Message}", retryable: false);
            }

            var form = new MultipartFormDataContent();

            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMediaType(imageBytes));
            AddFormPart(form, imageContent, "image", SanitizeUploadFileName(Path.GetFileName(referenceImagePath), imageBytes));
            AddFormPart(form, new StringContent(prompt ?? "", Encoding.UTF8), "prompt", null);
            AddFormPart(form, new StringContent(modelName ?? "", Encoding.UTF8), "model", null);
            AddFormPart(form, new StringContent(Math.Max(1, variantCount).ToString(CultureInfo.InvariantCulture), Encoding.UTF8), "n", null);
            if (!string.IsNullOrEmpty(size))
            {
                AddFormPart(form, new StringContent(size, Encoding.UTF8), "size", null);
            }

            var responseText = SendAndReadText(HttpMethod.Post, _baseUrl + "/images/edits", form);
            return ParseImages(responseText, "/images/edits");
        }

        /// <summary>
        /// 往 multipart 表单里加一段，<b>字段名与文件名一律自己加引号</b>。
        ///
        /// 为什么不用 <c>form.Add(content, name)</c> 那个重载：它把字段名交给
        /// ContentDispositionHeaderValue，而「image」这种合法 token 会被原样写成 <c>name=image</c>，
        /// 不带引号。RFC 7578 要求带引号，宽松的解析器无所谓，严格的当场把整个表单判成没有 image 字段——
        /// 报回来的是下游那句「image is a required parameter」，指不到「引号」这两个字上。
        /// 中转背后挂的是谁家的解析器不由我们决定，所以按最严的那一档写。
        /// </summary>
        /// <param name="form">目标表单。</param>
        /// <param name="content">这一段的内容。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="fileName">文件名；null 表示这一段不是文件。</param>
        private static void AddFormPart(MultipartFormDataContent form, HttpContent content, string fieldName, string fileName)
        {
            var disposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"" + fieldName + "\""
            };

            if (fileName != null)
            {
                disposition.FileName = "\"" + fileName + "\"";
            }

            content.Headers.ContentDisposition = disposition;
            form.Add(content);
        }

        /// <summary>
        /// 发一次请求并读回文本；非 2xx 一律翻成 <see cref="ImageBridgeException"/>。
        /// 错误分类与 oaicompat 的 SendChatCompletion 对齐：
        /// 连不上 → 下游不可达（可重试）；401/403 → 凭据无效；429 → 限流（可重试）；
        /// 5xx → 下游报错（可重试）；超时 → 超时（可重试）。
        /// 请求头（含密钥）绝不进任何错误文案。
        /// </summary>
        private string SendAndReadText(HttpMethod method, string url, HttpContent content)
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(method, url);

                // 密钥唯一被允许出现的地方。
                // 这一句必须单独兜住 FormatException：密钥里混进换行或不可见字符时
                // （从网页复制粘贴最常见），AuthenticationHeaderValue 抛的异常消息里
                // **带着那个值原文**——照着往上抛就是把密钥打进 stderr，那是决策 5 的红线。
                try
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
                }
                catch (FormatException)
                {
                    throw new ImageBridgeException(
                        "凭据无效",
                        "密钥不是合法的 HTTP 头取值（多半是复制时带进了换行或不可见字符）。这里不回显它的任何内容，请重新复制一遍再填",
                        retryable: false);
                }

                httpRequest.Content = content;

                using var httpResponse = _client.SendAsync(httpRequest).GetAwaiter().GetResult();
                var responseText = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                var statusCode = (int)httpResponse.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    return responseText;
                }

                if (statusCode == 401 || statusCode == 403)
                {
                    throw new ImageBridgeException("凭据无效", $"下游返回 HTTP {statusCode}，密钥无效或无权访问图像接口", retryable: false);
                }

                if (statusCode == 429)
                {
                    throw new ImageBridgeException("限流", "下游返回 HTTP 429，被限流，稍后重试", retryable: true);
                }

                var serverMessage = ReadErrorMessage(responseText);
                throw new ImageBridgeException("下游报错", $"下游返回 HTTP {statusCode}：{serverMessage}", retryable: statusCode >= 500);
            }
            catch (TaskCanceledException)
            {
                // HttpClient.Timeout 到期抛 TaskCanceledException；本进程没有其他取消源。
                throw new ImageBridgeException("超时", $"下游超过 {_timeoutSeconds} 秒未响应，已放弃本次调用", retryable: true);
            }
            catch (HttpRequestException exception)
            {
                // 连不上：DNS 失败、连接被拒、TLS 失败都落在这一支。异常消息不含请求头。
                throw new ImageBridgeException("下游不可达", $"连不上下游生图服务：{exception.Message}", retryable: true);
            }
        }

        /// <summary>
        /// 解析图像接口的回包：data 数组里逐项取图，**b64_json 与 url 两种都吃**。
        /// 只写一种迟早炸：gpt-image-1 恒回 b64_json，dall-e-3 默认回 url，
        /// 中转背后挂哪个模型不由我们决定。
        /// </summary>
        private IReadOnlyList<GeneratedImage> ParseImages(string responseText, string endpointName)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseText);
            }
            catch (JsonException exception)
            {
                throw new ImageBridgeException("下游报错", $"{endpointName} 回来的不是合法 JSON：{exception.Message}", retryable: false);
            }

            var images = new List<GeneratedImage>();
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    throw new ImageBridgeException("下游报错", $"{endpointName} 回来的 JSON 里没有「data」数组", retryable: false);
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (item.TryGetProperty("b64_json", out var base64Element) && base64Element.ValueKind == JsonValueKind.String)
                    {
                        var base64Text = base64Element.GetString() ?? "";
                        byte[] bytes;
                        try
                        {
                            bytes = Convert.FromBase64String(base64Text);
                        }
                        catch (FormatException exception)
                        {
                            throw new ImageBridgeException("下游报错", $"{endpointName} 回来的 b64_json 不是合法 base64：{exception.Message}", retryable: false);
                        }

                        images.Add(new GeneratedImage(bytes, "b64_json"));
                        continue;
                    }

                    if (item.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                    {
                        images.Add(new GeneratedImage(DownloadImage(urlElement.GetString() ?? ""), "url"));
                    }
                }
            }

            if (images.Count == 0)
            {
                throw new ImageBridgeException("下游报错", $"{endpointName} 跑完了但 data 里没有一项带 b64_json 或 url", retryable: false);
            }

            return images;
        }

        /// <summary>
        /// 按 URL 取图。走**不带任何认证头**的那个 HttpClient：
        /// 图片 URL 常常指向对象存储的另一个域，把 Authorization 带过去等于把密钥发给第三方。
        /// </summary>
        private byte[] DownloadImage(string url)
        {
            try
            {
                using var httpResponse = _downloadClient.GetAsync(url).GetAwaiter().GetResult();
                if (!httpResponse.IsSuccessStatusCode)
                {
                    throw new ImageBridgeException("下游报错", $"按 url 取图失败，HTTP {(int)httpResponse.StatusCode}", retryable: true);
                }

                var bytes = httpResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (bytes == null || bytes.Length == 0)
                {
                    throw new ImageBridgeException("下游报错", "按 url 取回来的图是空的", retryable: false);
                }

                return bytes;
            }
            catch (TaskCanceledException)
            {
                throw new ImageBridgeException("超时", $"按 url 取图超过 {_timeoutSeconds} 秒未完成，已放弃", retryable: true);
            }
            catch (HttpRequestException exception)
            {
                throw new ImageBridgeException("下游不可达", $"按 url 取图连不上：{exception.Message}", retryable: true);
            }
        }

        /// <summary>从错误响应体里抠服务端 message：先试 {"error":{"message":"…"}}，取不到给占位。</summary>
        private static string ReadErrorMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return "（服务端没有返回错误说明）";
            }

            try
            {
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    var text = message.GetString();
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

        /// <summary>按魔数猜图片的 MIME 类型；认不出按 image/png 发（下游只看得懂常见几种）。</summary>
        public static string GuessMediaType(byte[] bytes)
        {
            var extension = GuessExtension(bytes);
            switch (extension)
            {
                case ".jpg":
                    return "image/jpeg";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
        }

        /// <summary>
        /// 按魔数猜文件扩展名：PNG / JPEG / WEBP 三种，认不出按 .png 算。
        /// 落盘的扩展名不许照抄下游给的名字——中转回来的可能压根没有名字。
        /// </summary>
        /// <param name="bytes">图片字节。</param>
        public static string GuessExtension(byte[] bytes)
        {
            if (bytes != null && bytes.Length >= 12)
            {
                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    return ".png";
                }

                if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                {
                    return ".jpg";
                }

                if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                    && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                {
                    return ".webp";
                }
            }

            return ".png";
        }

        /// <summary>
        /// multipart 里 image 字段的文件名：钉成 ASCII，只留 [A-Za-z0-9._-]，
        /// 扩展名按真实魔数补。中文文件名进 multipart 头会被不同实现按不同方式编码，
        /// 有的中转会当场 400，而报出来的错跟「文件名」三个字毫无关系。
        /// </summary>
        private static string SanitizeUploadFileName(string fileName, byte[] bytes)
        {
            var stem = AsciiFileNaming.ToAsciiStem(Path.GetFileNameWithoutExtension(fileName ?? ""));
            return stem + GuessExtension(bytes);
        }

        /// <summary>放掉两个 HttpClient。</summary>
        public void Dispose()
        {
            _client.Dispose();
            _downloadClient.Dispose();
        }
    }
}
