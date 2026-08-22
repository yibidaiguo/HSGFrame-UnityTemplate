using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 飞书桥的 HTTP 底座：拿 tenant_access_token（进程内缓存到过期前 5 分钟）、发带鉴权的请求、
    /// 按飞书返回的 code 做错误映射。
    /// 密钥红线（决策 5、78）：token 与 app_secret 只许出现在请求体 / Authorization 头里——
    /// 不进日志、不进异常消息、不进返回载荷，长度和前缀也不许。打印 HTTP 错误前先确认
    /// 请求头没有被带进文案（HttpClient 的异常消息不含请求头，但自己拼错误文案时不许拼进去）。
    /// 错误映射：连不上 → 下游不可达；code=99991672 → 凭据无效（带飞书回的那句原文）；
    /// code 非 0 的其余 → 下游报错，带飞书的 msg 与 log_id（工单要用的）。
    /// </summary>
    public static class FeishuClient
    {
        /// <summary>取 tenant_access_token 的端点。</summary>
        private const string TokenEndpoint = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";

        /// <summary>多维表格的 app 前缀：/open-apis/bitable/v1/apps/&lt;app_token&gt;/…。</summary>
        private const string BitableAppsPrefix = "https://open.feishu.cn/open-apis/bitable/v1/apps/";

        /// <summary>发消息的端点（receive_id_type=open_id）。</summary>
        private const string ImMessagesEndpoint = "https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=open_id";

        /// <summary>上传图片的端点（image_type=message）。</summary>
        private const string ImImagesEndpoint = "https://open.feishu.cn/open-apis/im/v1/images";

        /// <summary>知识库的空间前缀：/open-apis/wiki/v2/spaces/…。</summary>
        private const string WikiSpacesPrefix = "https://open.feishu.cn/open-apis/wiki/v2/spaces";

        /// <summary>文档块的前缀：/open-apis/docx/v1/documents/…。</summary>
        private const string DocxDocumentsPrefix = "https://open.feishu.cn/open-apis/docx/v1/documents/";

        /// <summary>飞书对取 token 有频率限制，token 缓存在进程内、过期前 5 分钟视为过期。</summary>
        private static readonly TimeSpan TokenRefreshAhead = TimeSpan.FromMinutes(5);

        /// <summary>进程内缓存的 token 值。密钥：绝不进日志、异常、返回。</summary>
        private static string _cachedToken = "";

        /// <summary>缓存 token 的过期时刻；已过期（含提前量）时视为没有缓存。</summary>
        private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

        /// <summary>一次带鉴权请求的结果：成功时带解析好的响应体，失败时带协议响应与飞书业务码。</summary>
        public sealed class HttpCall
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public JsonElement ResponseBody;

            /// <summary>
            /// 飞书自己的业务码（响应体里的 code），失败时才有意义，拿不到给 0。
            /// **调用方要靠它分「不存在」与「没权限」**：这两支的处置完全相反——
            /// 不存在该重新建一个，没权限该停下来让人去授权。
            /// 合并成一句「调用失败」的话，一个没权限的对象会被当成不存在，
            /// 于是每跑一次就在下游多建一个，越建越多。
            /// </summary>
            public int BusinessCode;
        }

        /// <summary>拼多维表格 app 下的子路径 URL：BitableAppsPrefix + appToken + "/" + 相对路径。</summary>
        public static string BitableUrl(string appToken, string relativePath)
        {
            return BitableAppsPrefix + Uri.EscapeDataString(appToken) + "/" + relativePath;
        }

        /// <summary>发消息的 URL（receive_id_type=open_id）。</summary>
        public static string ImMessagesUrl()
        {
            return ImMessagesEndpoint;
        }

        /// <summary>知识空间集合的 URL：POST 建一个新空间，GET 列已有的。</summary>
        public static string WikiSpacesUrl()
        {
            return WikiSpacesPrefix;
        }

        /// <summary>按 space_id 读一个知识空间的 URL，用来验它还在不在。</summary>
        /// <param name="spaceId">知识空间 space_id。</param>
        public static string WikiSpaceUrl(string spaceId)
        {
            return WikiSpacesPrefix + "/" + Uri.EscapeDataString(spaceId);
        }

        /// <summary>云空间元信息批量查询的 URL：按 doc_token 问「这份文档的地址是什么」。</summary>
        public static string DriveMetasUrl()
        {
            return "https://open.feishu.cn/open-apis/drive/v1/metas/batch_query";
        }

        /// <summary>某个知识空间下建节点的 URL。</summary>
        /// <param name="spaceId">知识空间 space_id。</param>
        public static string WikiNodesUrl(string spaceId)
        {
            return WikiSpacesPrefix + "/" + Uri.EscapeDataString(spaceId) + "/nodes";
        }

        /// <summary>
        /// 按节点 token 读节点的 URL。**这一支不带 space_id**：
        /// 知道 token 就够了，而调用方常常只有 token（它记在文档的 frontmatter 里）。
        /// </summary>
        /// <param name="nodeToken">节点 token。</param>
        public static string WikiGetNodeUrl(string nodeToken)
        {
            return WikiSpacesPrefix + "/get_node?token=" + Uri.EscapeDataString(nodeToken);
        }

        /// <summary>改节点标题的 URL。</summary>
        /// <param name="spaceId">知识空间 space_id。</param>
        /// <param name="nodeToken">节点 token。</param>
        public static string WikiUpdateTitleUrl(string spaceId, string nodeToken)
        {
            return WikiSpacesPrefix + "/" + Uri.EscapeDataString(spaceId)
                + "/nodes/" + Uri.EscapeDataString(nodeToken) + "/update_title";
        }

        /// <summary>某个块的子块 URL（列出与新增同一个地址，GET 与 POST 分别对应）。</summary>
        /// <param name="documentId">文档 id，也就是知识库节点的 obj_token。</param>
        /// <param name="blockId">父块 id；写文档最外层时与文档 id 相同。</param>
        public static string DocxChildrenUrl(string documentId, string blockId)
        {
            return DocxDocumentsPrefix + Uri.EscapeDataString(documentId)
                + "/blocks/" + Uri.EscapeDataString(blockId) + "/children";
        }

        /// <summary>按下标区间批量删子块的 URL。</summary>
        /// <param name="documentId">文档 id。</param>
        /// <param name="blockId">父块 id。</param>
        public static string DocxBatchDeleteUrl(string documentId, string blockId)
        {
            return DocxChildrenUrl(documentId, blockId) + "/batch_delete";
        }

        /// <summary>
        /// 发一次带鉴权的飞书请求：先拿（或复用）token，再按 method 发请求，读响应体并按 code 映射错误。
        /// 成功时 ResponseBody 是响应体的 JSON 对象（已 clone）；失败时 Response 是失败协议响应。
        /// </summary>
        /// <param name="method">HTTP 方法：GET / POST / DELETE。</param>
        /// <param name="url">完整请求 URL。</param>
        /// <param name="bodyJson">请求体 JSON 文本；GET 时传 null。</param>
        /// <param name="appId">飞书应用标识。</param>
        /// <param name="appSecret">飞书应用密钥，只进 token 请求体，绝不出现在任何文案里。</param>
        /// <param name="timeoutSeconds">单次 HTTP 超时秒数。</param>
        public static HttpCall Send(string method, string url, string bodyJson, string appId, string appSecret, int timeoutSeconds)
        {
            if (!TryGetToken(appId, appSecret, timeoutSeconds, out var token, out var tokenError))
            {
                return new HttpCall { Succeeded = false, Response = tokenError };
            }

            return SendWithToken(method, url, bodyJson, token, timeoutSeconds);
        }

        /// <summary>云空间素材上传端点：文档里的图片与文件都走它。</summary>
        private const string DriveMediasUploadEndpoint = "https://open.feishu.cn/open-apis/drive/v1/medias/upload_all";

        /// <summary>
        /// 把一个本地文件上传成文档块的素材，拿回 file_token。
        ///
        /// 飞书这条链是**两步的、有先后**：先在文档里建一个空的图片/文件块拿到 block_id，
        /// 再把素材传上去、用 parent_node 指向那个块——**没有「先传素材再建块」的路**。
        /// 所以调用方必须先写块、再拿着块 id 回来调这里。
        /// </summary>
        /// <param name="filePath">本地文件路径。</param>
        /// <param name="parentType">素材挂在什么上：docx_image（图片块）/ docx_file（文件块）。</param>
        /// <param name="parentNode">那个块的 block_id。</param>
        /// <param name="appId">飞书应用标识。</param>
        /// <param name="appSecret">飞书应用密钥，只进 token 请求体，绝不出现在任何文案里。</param>
        /// <param name="timeoutSeconds">单次 HTTP 超时秒数。</param>
        public static HttpCall UploadMedia(
            string filePath,
            string parentType,
            string parentNode,
            string appId,
            string appSecret,
            int timeoutSeconds)
        {
            if (!File.Exists(filePath))
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "请求不合协议", $"素材文件不存在：{filePath}", retryable: false)
                };
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "请求不合协议", $"素材读不出来：{filePath}（{exception.Message}）", retryable: false)
                };
            }

            if (!TryGetToken(appId, appSecret, timeoutSeconds, out var token, out var tokenError))
            {
                return new HttpCall { Succeeded = false, Response = tokenError };
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var request = new HttpRequestMessage(HttpMethod.Post, DriveMediasUploadEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(Path.GetFileName(filePath)), "file_name");
                content.Add(new StringContent(parentType), "parent_type");
                content.Add(new StringContent(parentNode), "parent_node");
                content.Add(new StringContent(bytes.Length.ToString(CultureInfo.InvariantCulture)), "size");
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                request.Content = content;

                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var logId = ReadLogIdFromHeaders(response);
                var statusCode = (int)response.StatusCode;

                if (statusCode < 200 || statusCode >= 300)
                {
                    return new HttpCall
                    {
                        Succeeded = false,
                        BusinessCode = ReadCodeFromText(responseText),
                        Response = MapHttpError(statusCode, responseText, logId, "POST", DriveMediasUploadEndpoint)
                    };
                }

                if (!TryParseBody(responseText, out var body))
                {
                    return new HttpCall
                    {
                        Succeeded = false,
                        Response = BridgeResponse.Failure("1.0.0", "下游报错", "素材上传的响应体不是合法 JSON", retryable: false)
                    };
                }

                var code = ReadCode(body);
                if (code != 0)
                {
                    return new HttpCall { Succeeded = false, Response = MapCodeError(body, code, logId), BusinessCode = code };
                }

                return new HttpCall { Succeeded = true, ResponseBody = body.Clone() };
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException)
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "下游不可达", $"素材上传发不出去：{exception.Message}", retryable: true)
                };
            }
        }

        /// <summary>
        /// 上传一张本地 PNG 给飞书（image_type=message），拿到 data.image_key 供卡片 img 元素引用。
        /// 文件不存在直接失败（请求不合协议）；其余沿用 SendWithToken 那套错误映射（连不上→下游不可达、
        /// 99991672→凭据无效、code 非 0→下游报错带 msg 与 log_id）。密钥红线照旧：token 只进 Authorization 头。
        /// </summary>
        /// <param name="filePath">本地 PNG 文件路径。</param>
        /// <param name="appId">飞书应用标识。</param>
        /// <param name="appSecret">飞书应用密钥，只进 token 请求体，绝不出现在任何文案里。</param>
        /// <param name="timeoutSeconds">单次 HTTP 超时秒数。</param>
        public static HttpCall UploadImage(string filePath, string appId, string appSecret, int timeoutSeconds)
        {
            if (!File.Exists(filePath))
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "请求不合协议", $"图片文件不存在：{filePath}", retryable: false)
                };
            }

            byte[] imageBytes;
            try
            {
                imageBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "请求不合协议", $"图片读不出来：{filePath}（{exception.Message}）", retryable: false)
                };
            }

            if (!TryGetToken(appId, appSecret, timeoutSeconds, out var token, out var tokenError))
            {
                return new HttpCall { Succeeded = false, Response = tokenError };
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var request = new HttpRequestMessage(HttpMethod.Post, ImImagesEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var content = new MultipartFormDataContent();
                content.Add(new StringContent("message"), "image_type");
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", Path.GetFileName(filePath));
                request.Content = content;

                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var logId = ReadLogIdFromHeaders(response);

                var statusCode = (int)response.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    if (!TryParseBody(responseText, out var body))
                    {
                        return new HttpCall
                        {
                            Succeeded = false,
                            Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书返回的响应体不是合法 JSON", retryable: false)
                        };
                    }

                    if (body.TryGetProperty("code", out var codeElement))
                    {
                        if (codeElement.ValueKind != JsonValueKind.Number || !TryParseCode(codeElement, out var code))
                        {
                            return new HttpCall
                            {
                                Succeeded = false,
                                Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书响应的 code 不是合法整数", retryable: false)
                            };
                        }

                        if (code != 0)
                        {
                            return new HttpCall { Succeeded = false, Response = MapCodeError(body, code, logId), BusinessCode = code };
                        }
                    }

                    return new HttpCall { Succeeded = true, ResponseBody = body.Clone() };
                }

                // 调试日志走 stderr：只打方法、URL 与状态码，绝不含请求头（Authorization 里有 token）。
                Console.Error.WriteLine($"BridgeFeishu HTTP {statusCode}：POST {ImImagesEndpoint}");
                return new HttpCall
                {
                    Succeeded = false,
                    BusinessCode = ReadCodeFromText(responseText),
                Response = MapHttpError(statusCode, responseText, logId, "POST", ImImagesEndpoint)
                };
            }
            catch (TaskCanceledException)
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "超时", $"飞书超过 {timeoutSeconds} 秒未响应，已放弃本次调用", retryable: true)
                };
            }
            catch (HttpRequestException)
            {
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "下游不可达", "连不上飞书，请检查网络", retryable: true)
                };
            }
        }

        /// <summary>
        /// 拿 tenant_access_token。进程内缓存未到「过期前 5 分钟」直接复用；
        /// 否则 POST 换取新的。token 值只经 out 参数出去，绝不出现在日志、异常与返回文案里。
        /// </summary>
        private static bool TryGetToken(string appId, string appSecret, int timeoutSeconds, out string token, out BridgeResponse error)
        {
            token = "";
            error = null;

            if (_cachedToken.Length > 0 && DateTimeOffset.UtcNow < _tokenExpiresAt - TokenRefreshAhead)
            {
                token = _cachedToken;
                return true;
            }

            var requestBody = "{\"app_id\":" + JsonSerializer.Serialize(appId)
                + ",\"app_secret\":" + JsonSerializer.Serialize(appSecret) + "}";

            HttpCall call;
            try
            {
                call = PostJson(TokenEndpoint, requestBody, timeoutSeconds);
            }
            catch (Exception exception) when (exception is TaskCanceledException)
            {
                error = BridgeResponse.Failure("1.0.0", "超时", $"飞书取 token 超过 {timeoutSeconds} 秒未响应", retryable: true);
                return false;
            }
            catch (Exception exception) when (exception is HttpRequestException)
            {
                error = BridgeResponse.Failure("1.0.0", "下游不可达", "连不上飞书：无法建立连接（取 token 阶段）", retryable: true);
                return false;
            }

            if (!call.Succeeded)
            {
                error = call.Response;
                return false;
            }

            if (!TryReadTokenResponse(call.ResponseBody, out var newToken, out var reason))
            {
                error = BridgeResponse.Failure("1.0.0", "下游报错", "飞书取 token 的响应里没有 token：" + reason, retryable: false);
                return false;
            }

            _cachedToken = newToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(TokenLifetimeSeconds);
            token = newToken;
            return true;
        }

        /// <summary>token 响应里的有效期秒数；解析不到时按 7200 兜底（过期了会再换，不影响正确性）。</summary>
        private static int TokenLifetimeSeconds = 7200;

        /// <summary>解析取 token 的响应体：code 必须为 0，token 取 tenant_access_token 字符串。</summary>
        private static bool TryReadTokenResponse(JsonElement body, out string token, out string reason)
        {
            token = "";
            reason = "";
            if (body.ValueKind != JsonValueKind.Object)
            {
                reason = "响应顶层不是对象";
                return false;
            }

            if (!body.TryGetProperty("tenant_access_token", out var tokenElement) || tokenElement.ValueKind != JsonValueKind.String)
            {
                reason = "响应缺 tenant_access_token 字段";
                return false;
            }

            if (body.TryGetProperty("expire", out var expireElement) && expireElement.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    TokenLifetimeSeconds = expireElement.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                    TokenLifetimeSeconds = 7200;
                }
            }

            token = tokenElement.GetString() ?? "";
            return token.Length > 0;
        }

        /// <summary>带 token 发一次请求并解析响应体；token 只进 Authorization 头。</summary>
        private static HttpCall SendWithToken(string method, string url, string bodyJson, string token, int timeoutSeconds)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (bodyJson != null)
                {
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                }

                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var logId = ReadLogIdFromHeaders(response);

                var statusCode = (int)response.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    if (!TryParseBody(responseText, out var body))
                    {
                        return new HttpCall
                        {
                            Succeeded = false,
                            Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书返回的响应体不是合法 JSON", retryable: false)
                        };
                    }

                    if (body.TryGetProperty("code", out var codeElement))
                    {
                        if (codeElement.ValueKind != JsonValueKind.Number || !TryParseCode(codeElement, out var code))
                        {
                            return new HttpCall
                            {
                                Succeeded = false,
                                Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书响应的 code 不是合法整数", retryable: false)
                            };
                        }

                        if (code != 0)
                        {
                            return new HttpCall { Succeeded = false, Response = MapCodeError(body, code, logId), BusinessCode = code };
                        }
                    }

                    return new HttpCall { Succeeded = true, ResponseBody = body.Clone() };
                }

                // 调试日志走 stderr：只打方法、URL 与状态码，绝不含请求头（Authorization 里有 token）。
                Console.Error.WriteLine($"BridgeFeishu HTTP {statusCode}：{method} {url}");
                return new HttpCall
                {
                    Succeeded = false,
                    BusinessCode = ReadCodeFromText(responseText),
                Response = MapHttpError(statusCode, responseText, logId, method, url)
                };
            }
            catch (TaskCanceledException)
            {
                // HttpClient.Timeout 到期抛 TaskCanceledException；本进程没有其他取消源。
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "超时", $"飞书超过 {timeoutSeconds} 秒未响应，已放弃本次调用", retryable: true)
                };
            }
            catch (HttpRequestException)
            {
                // 连不上：DNS 失败、连接被拒、TLS 失败都落在这一支。异常消息不含请求头，也不含 token。
                return new HttpCall
                {
                    Succeeded = false,
                    Response = BridgeResponse.Failure("1.0.0", "下游不可达", "连不上飞书，请检查网络", retryable: true)
                };
            }
        }

        /// <summary>POST JSON 到端点，返回原始 HttpCall（不解析飞书业务 code，取 token 专用）。</summary>
        private static HttpCall PostJson(string url, string bodyJson, int timeoutSeconds)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var logId = ReadLogIdFromHeaders(response);

            var statusCode = (int)response.StatusCode;
            if (statusCode >= 200 && statusCode < 300)
            {
                if (!TryParseBody(responseText, out var body))
                {
                    return new HttpCall
                    {
                        Succeeded = false,
                        Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书返回的响应体不是合法 JSON", retryable: false)
                    };
                }

                if (body.TryGetProperty("code", out var codeElement))
                {
                    if (codeElement.ValueKind != JsonValueKind.Number || !TryParseCode(codeElement, out var code))
                    {
                        return new HttpCall
                        {
                            Succeeded = false,
                            Response = BridgeResponse.Failure("1.0.0", "下游报错", "飞书响应的 code 不是合法整数", retryable: false)
                        };
                    }

                    if (code != 0)
                    {
                        return new HttpCall { Succeeded = false, Response = MapCodeError(body, code, logId), BusinessCode = code };
                    }
                }

                return new HttpCall { Succeeded = true, ResponseBody = body.Clone() };
            }

            // 调试日志走 stderr：只打方法、URL 与状态码，绝不含请求体（里面有 app_secret）。
            Console.Error.WriteLine($"BridgeFeishu HTTP {statusCode}：POST {url}");
            return new HttpCall
            {
                Succeeded = false,
                BusinessCode = ReadCodeFromText(responseText),
                Response = MapHttpError(statusCode, responseText, logId, "POST", url)
            };
        }

        /// <summary>
        /// 从响应体文本里取飞书业务码；解析不了给 0。
        /// **非 2xx 那几支也要取**：飞书把「没权限」放在 HTTP 400 + code 131006 里，
        /// 只看 HTTP 状态的话，调用方分不出「没权限」与「不存在」——
        /// 而这两支的处置正好相反（前者该停下来授权，后者该重新建一个）。
        /// </summary>
        /// <param name="responseText">响应体原文。</param>
        private static int ReadCodeFromText(string responseText)
        {
            return TryParseBody(responseText, out var body) ? ReadCode(body) : 0;
        }

        /// <summary>这个业务码有没有一句「该去点哪里」可说；只有说得出的才值得盖掉 HTTP 状态那句话。</summary>
        /// <param name="code">飞书业务码。</param>
        private static bool HasGuidance(int code)
        {
            return code == 131006 || code == 99991672;
        }

        /// <summary>读响应体里的 code；不是数字或没有时给 0。</summary>
        /// <param name="body">响应体。</param>
        private static int ReadCode(JsonElement body)
        {
            return body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("code", out var element)
                && element.ValueKind == JsonValueKind.Number
                && TryParseCode(element, out var code)
                ? code
                : 0;
        }

        /// <summary>按飞书业务 code 映射错误：131006 → 凭据无效（带该去点哪里）；99991672 → 凭据无效（带原文）；其余 → 下游报错（带 msg 与 log_id）。</summary>
        private static BridgeResponse MapCodeError(JsonElement body, int code, string logId)
        {
            var msg = ReadString(body, "msg");
            var idPart = string.IsNullOrWhiteSpace(logId) ? "（响应头与响应体都没有 log_id）" : "log_id=" + logId;

            if (code == 131006)
            {
                // 131006 有两种长相，差别很大，人话里必须分清楚：
                // 「node permission denied」= 应用够得着这个空间，但对那个节点只有读、没有写；
                // 「wiki space permission denied」= 连空间本身都没份。
                // 两种都不是「开个权限点」能解决的——要有人在飞书那边把应用加成协作者。
                var isNodeLevel = msg != null && msg.Contains("node permission", StringComparison.OrdinalIgnoreCase);
                var howTo = isNodeLevel
                    ? "打开那个父节点 →「···」→ 添加文档协作者 → 搜应用名 → 给「可编辑」"
                    : "打开知识空间设置 → 成员 → 把这个应用加进来并给编辑权";
                return BridgeResponse.Failure(
                    "1.0.0",
                    "凭据无效",
                    $"飞书返回 code=131006：{(string.IsNullOrWhiteSpace(msg) ? "知识库权限不足" : msg)}。{howTo}（{idPart}）",
                    retryable: false);
            }

            if (code == 99991672)
            {
                // 人话里必须把飞书回的那句原文带出来——它直接告诉人去点哪个权限。
                var permissionText = string.IsNullOrWhiteSpace(msg) ? "应用尚未开通所需权限" : msg;
                return BridgeResponse.Failure(
                    "1.0.0",
                    "凭据无效",
                    $"飞书返回 code=99991672：{permissionText}（{idPart}）",
                    retryable: false);
            }

            var messagePart = string.IsNullOrWhiteSpace(msg) ? "（飞书没有给出 msg）" : msg;
            return BridgeResponse.Failure(
                "1.0.0",
                "下游报错",
                $"飞书返回 code={code}：{messagePart}（{idPart}）",
                retryable: false);
        }

        /// <summary>非 2xx 的 HTTP 错误：尝试从响应体抠 msg/log_id，抠不出给状态码占位。</summary>
        private static BridgeResponse MapHttpError(int statusCode, string responseText, string logId, string method, string url)
        {
            var msg = "";
            var bodyLogId = "";
            var body = default(JsonElement);
            if (TryParseBody(responseText, out var parsedBody))
            {
                body = parsedBody;
                msg = ReadString(body, "msg");
                bodyLogId = ReadString(body, "log_id");
            }

            var effectiveLogId = string.IsNullOrWhiteSpace(bodyLogId) ? logId : bodyLogId;
            var idPart = string.IsNullOrWhiteSpace(effectiveLogId) ? "（响应里没有 log_id）" : "log_id=" + effectiveLogId;
            var messagePart = string.IsNullOrWhiteSpace(msg) ? $"飞书返回 HTTP {statusCode}" : msg;
            var urlPart = string.IsNullOrWhiteSpace(url) ? "" : $"（请求：{method} {url}）";

            // 业务码有话说的时候以业务码为准：飞书的权限类失败常常是「HTTP 400 + 体里一个 code」，
            // 而「HTTP 400」这三个字对人毫无用处，「把应用加成那个节点的协作者」才有用。
            // 只在真认识那个码时才改口，认不出来仍旧报 HTTP 状态——瞎猜比说不知道更坏。
            var bodyCode = ReadCode(body);
            if (bodyCode != 0 && HasGuidance(bodyCode))
            {
                return MapCodeError(body, bodyCode, effectiveLogId);
            }

            return BridgeResponse.Failure(
                "1.0.0",
                "下游报错",
                $"飞书返回 HTTP {statusCode}：{messagePart}{urlPart}（{idPart}）",
                retryable: statusCode >= 500);
        }

        /// <summary>从响应头里读 log_id（飞书工单排查要用的），读不到给空串。</summary>
        private static string ReadLogIdFromHeaders(HttpResponseMessage response)
        {
            foreach (var headerName in new[] { "X-Tt-Logid", "X-Tt-LogId", "x-tt-logid" })
            {
                if (response.Headers.TryGetValues(headerName, out var values))
                {
                    foreach (var value in values)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }

            return "";
        }

        /// <summary>解析 JSON 文本成对象；失败返回 false。响应体不含密钥，可以进错误文案。</summary>
        private static bool TryParseBody(string text, out JsonElement body)
        {
            body = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                body = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>读 JSON 对象里的字符串键；缺失或类型不对给空串。</summary>
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

        /// <summary>读 JSON 对象里的整数键；缺失或类型不对给 0。</summary>
        private static bool TryParseCode(JsonElement element, out int code)
        {
            code = 0;
            try
            {
                code = element.GetInt32();
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
                return false;
            }
        }
    }
}
