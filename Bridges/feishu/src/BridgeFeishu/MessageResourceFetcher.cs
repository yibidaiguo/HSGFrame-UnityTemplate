using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 把人在聊天里发的图片/文件取回本地（fetch）。
    ///
    /// 为什么要单独一支：人发一张参考图配一句「再出一张，照这个风格」，
    /// 那张图**就是这句话的一半意思**。取不回来的话，助手只能回一句「我只认文字」，
    /// 而人明明已经把要说的说完了。
    ///
    /// 归一那一步（旁路）只留下「有哪几个 key」——下载要调飞书接口，属于下游知识，
    /// 归桥管（决策 93）。引擎拿到的始终是一个本地文件路径，换个消息下游一个字都不用改。
    ///
    /// 资源按**消息**取，不是按 key 全局取：同一个 key 换一条消息就取不到，
    /// 所以载荷里消息标识与资源 key 缺一不可。
    /// </summary>
    public static class MessageResourceFetcher
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>
        /// 执行 fetch 动作：干跑只回「打算取什么、存哪」；真跑把文件下回来。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒，
        /// 载荷含 干跑（缺省 true）、消息标识、资源key、资源类型、存到。</param>
        public static BridgeResponse Fetch(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);

            var messageIdentifier = ReadPayloadString(request, "消息标识");
            var resourceKey = ReadPayloadString(request, "资源key");
            var resourceType = ReadPayloadString(request, "资源类型");
            var destinationPath = ReadPayloadString(request, "存到");

            if (appId.Length == 0 || secretKey.Length == 0)
            {
                return Failure("凭据无效", "应用标识或飞书应用密钥未配置", retryable: false);
            }

            if (messageIdentifier.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「消息标识」：资源按消息取，没有它取不到", retryable: false);
            }

            if (resourceKey.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「资源key」：不知道要取哪个资源", retryable: false);
            }

            // 类型不许猜：image 与 file 是两种取法，填错了飞书回「资源不存在」，
            // 那句话会把人引到「是不是图没发出去」上，而真因是这里填错了字。
            if (!string.Equals(resourceType, "image", StringComparison.Ordinal)
                && !string.Equals(resourceType, "file", StringComparison.Ordinal))
            {
                return Failure(
                    "请求不合协议",
                    $"「资源类型」得是 image 或 file，收到的是「{(resourceType.Length == 0 ? "空" : resourceType)}」",
                    retryable: false);
            }

            if (destinationPath.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「存到」：不知道要把资源放哪", retryable: false);
            }

            var url = FeishuClient.MessageResourceUrl(messageIdentifier, resourceKey, resourceType);

            if (isDryRun)
            {
                return Success(new JsonObject
                {
                    ["干跑"] = true,
                    ["资源类型"] = resourceType,
                    ["存到"] = destinationPath
                });
            }

            var call = FeishuClient.DownloadToFile(url, destinationPath, appId, secretKey, timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            var size = 0L;
            try
            {
                size = new FileInfo(destinationPath).Length;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 大小取不到不影响这次调用成不成——文件已经落地了，只是这条附言少一个数。
                size = 0L;
            }

            return Success(new JsonObject
            {
                ["干跑"] = false,
                ["资源类型"] = resourceType,
                ["存到"] = destinationPath,
                ["字节数"] = size
            });
        }

        /// <summary>成功响应。</summary>
        private static BridgeResponse Success(JsonObject payload)
        {
            return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }

        /// <summary>读请求配置里的字符串键；缺失给空串。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读请求配置里的整数键；缺失、类型不对给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var number))
            {
                return number;
            }

            return fallback;
        }

        /// <summary>读载荷里的字符串键；缺失给空串。</summary>
        private static string ReadPayloadString(BridgeRequest request, string key)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读载荷里的布尔键；缺失给缺省值。</summary>
        private static bool ReadPayloadBool(BridgeRequest request, string key, bool defaultValue)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element))
            {
                if (element.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return defaultValue;
        }
    }
}
