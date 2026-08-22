using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 推需求文档动作（doc）：把一条需求的 index.md（已经拆成中性块）推成知识库里的一个 docx 节点。
    ///
    /// 幂等靠**节点 token**，不靠标题：token 由调用方从文档 frontmatter 的「同步」块里带过来，
    /// 带了就刷新那一份，没带才在父节点下新建一个。按标题找的话，
    /// 人在飞书那边改一次标题就会被这里当成「没推过」，同一条需求立刻多出第二份文档。
    ///
    /// 刷新的做法是**整篇换掉**：先删光原有的子块，再按顺序写新的。
    /// docx 没有「按段落对齐着改」的接口，而逐块 diff 要维护一张仓库块与飞书块的对照表——
    /// 那张表一旦对不上，改动就会落到错误的段落上，比重写一遍危险得多。
    /// 代价是飞书那边的改动历史每次一整版，这一点由「权威侧」那个字段兜着（规范第一节）。
    /// </summary>
    public static class WikiDocumentWriter
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>一次写子块最多带多少个：飞书对 children 的批量上限是 50。</summary>
        private const int ChildrenBatchSize = 50;

        /// <summary>
        /// 执行 doc 动作：干跑返回将建还是将刷新、块数与要写的 children JSON；真跑建/刷新那份文档。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识/飞书应用密钥/知识空间标识/需求文档父节点/超时秒，
        /// 载荷含 干跑（缺省 true）、标题、节点token（可空）、块（中性块数组）。</param>
        public static BridgeResponse PushDocument(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var spaceId = ReadConfigurationString(request, "知识空间标识", "");
            var parentNode = ReadConfigurationString(request, "需求文档父节点", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);

            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            var title = ReadPayloadString(request, "标题");
            var nodeToken = ReadPayloadString(request, "节点token");

            if (title.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「标题」：知识库节点必须有名字", retryable: false);
            }

            if (!request.Payload.TryGetProperty("块", out var blocksElement) || blocksElement.ValueKind != JsonValueKind.Array)
            {
                return Failure("请求不合协议", "载荷缺「块」数组：要推的正文是空的", retryable: false);
            }

            var blocks = RequirementDocumentOutline.FromJsonArray(blocksElement);
            var children = FeishuBlockCodec.ToChildren(blocks);
            var willCreate = nodeToken.Length == 0;

            if (isDryRun)
            {
                var preview = new JsonObject
                {
                    ["干跑"] = true,
                    ["动作"] = willCreate ? "将新建" : "将刷新",
                    ["块数"] = children.Count,
                    ["节点token"] = nodeToken,
                    ["要写的块JSON"] = children.ToJsonString()
                };
                return Success(JsonSerializer.SerializeToElement(preview));
            }

            if (appId.Length == 0)
            {
                return Failure("凭据无效", "应用标识未配置（配置键「应用标识」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "飞书应用密钥未配置（配置键「飞书应用密钥」为空）", retryable: false);
            }

            string documentId;
            string link;
            string effectiveSpace = spaceId;

            if (willCreate)
            {
                if (spaceId.Length == 0)
                {
                    return Failure("凭据无效", "知识空间标识未配置（配置键「知识空间标识」为空）：新建节点必须点名建在哪个空间", retryable: false);
                }

                var created = CreateNode(spaceId, parentNode, title, appId, secretKey, timeoutSeconds);
                if (!created.Succeeded)
                {
                    return created.Response;
                }

                nodeToken = ReadString(created.ResponseBody, "data", "node", "node_token");
                documentId = ReadString(created.ResponseBody, "data", "node", "obj_token");
                link = ReadString(created.ResponseBody, "data", "node", "url");
                if (documentId.Length == 0)
                {
                    return Failure("下游报错", "飞书建节点的响应里没有 obj_token，拿不到要写正文的那份文档", retryable: false);
                }
            }
            else
            {
                var node = GetNode(nodeToken, appId, secretKey, timeoutSeconds);
                if (!node.Succeeded)
                {
                    return node.Response;
                }

                documentId = ReadString(node.ResponseBody, "data", "node", "obj_token");
                link = ReadString(node.ResponseBody, "data", "node", "url");
                if (effectiveSpace.Length == 0)
                {
                    effectiveSpace = ReadString(node.ResponseBody, "data", "node", "space_id");
                }

                if (documentId.Length == 0)
                {
                    return Failure("下游报错", $"节点 {nodeToken} 读回来没有 obj_token：它可能不是一份文档", retryable: false);
                }

                var cleared = ClearChildren(documentId, appId, secretKey, timeoutSeconds);
                if (cleared != null)
                {
                    return cleared;
                }

                // 标题跟着仓库走：这一侧改了名，下游那份也该改过来，否则两边对不上号。
                if (effectiveSpace.Length > 0)
                {
                    var renamed = UpdateTitle(effectiveSpace, nodeToken, title, appId, secretKey, timeoutSeconds);
                    if (!renamed.Succeeded)
                    {
                        return renamed.Response;
                    }
                }
            }

            var written = WriteChildren(documentId, children, appId, secretKey, timeoutSeconds);
            if (written != null)
            {
                return written;
            }

            var result = new JsonObject
            {
                ["节点token"] = nodeToken,
                ["链接"] = link,
                ["文档id"] = documentId,
                ["块数"] = children.Count,
                ["动作"] = willCreate ? "已新建" : "已刷新"
            };
            return Success(JsonSerializer.SerializeToElement(result));
        }

        /// <summary>在父节点下建一个 docx 节点；父节点为空表示挂成一级节点。</summary>
        private static FeishuClient.HttpCall CreateNode(
            string spaceId, string parentNode, string title, string appId, string secretKey, int timeoutSeconds)
        {
            var body = new JsonObject
            {
                ["obj_type"] = "docx",
                ["node_type"] = "origin",
                ["title"] = title
            };
            if (parentNode.Length > 0)
            {
                body["parent_node_token"] = parentNode;
            }

            return FeishuClient.Send(
                "POST", FeishuClient.WikiNodesUrl(spaceId), body.ToJsonString(), appId, secretKey, timeoutSeconds);
        }

        /// <summary>按节点 token 读回节点：要它的 obj_token（真正的文档 id）、链接与所属空间。</summary>
        private static FeishuClient.HttpCall GetNode(string nodeToken, string appId, string secretKey, int timeoutSeconds)
        {
            return FeishuClient.Send(
                "GET", FeishuClient.WikiGetNodeUrl(nodeToken), null, appId, secretKey, timeoutSeconds);
        }

        /// <summary>改节点标题。</summary>
        private static FeishuClient.HttpCall UpdateTitle(
            string spaceId, string nodeToken, string title, string appId, string secretKey, int timeoutSeconds)
        {
            var body = new JsonObject { ["title"] = title };
            return FeishuClient.Send(
                "POST", FeishuClient.WikiUpdateTitleUrl(spaceId, nodeToken), body.ToJsonString(), appId, secretKey, timeoutSeconds);
        }

        /// <summary>
        /// 删光文档现有的子块；文档本来就是空的时候什么都不做。
        /// 返回 null 表示成功，非 null 是要原样回给调用方的失败响应。
        /// </summary>
        private static BridgeResponse ClearChildren(string documentId, string appId, string secretKey, int timeoutSeconds)
        {
            var existing = FeishuClient.Send(
                "GET", FeishuClient.DocxChildrenUrl(documentId, documentId), null, appId, secretKey, timeoutSeconds);
            if (!existing.Succeeded)
            {
                return existing.Response;
            }

            var count = 0;
            if (existing.ResponseBody.TryGetProperty("data", out var data)
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                count = items.GetArrayLength();
            }

            if (count == 0)
            {
                return null;
            }

            var body = new JsonObject
            {
                ["start_index"] = 0,
                ["end_index"] = count
            };
            var deleted = FeishuClient.Send(
                "DELETE", FeishuClient.DocxBatchDeleteUrl(documentId, documentId), body.ToJsonString(), appId, secretKey, timeoutSeconds);
            return deleted.Succeeded ? null : deleted.Response;
        }

        /// <summary>
        /// 按顺序写子块，每批最多 <see cref="ChildrenBatchSize"/> 个。
        /// index 累加着往后走——不累加的话第二批会插到第一批前面，整篇顺序翻过来。
        /// 返回 null 表示成功。
        /// </summary>
        private static BridgeResponse WriteChildren(
            string documentId, JsonArray children, string appId, string secretKey, int timeoutSeconds)
        {
            var index = 0;
            while (index < children.Count)
            {
                var batch = new JsonArray();
                for (var offset = 0; offset < ChildrenBatchSize && index + offset < children.Count; offset++)
                {
                    // JsonNode 只能挂在一棵树上，塞进新数组前先深拷一份。
                    batch.Add(JsonNode.Parse(children[index + offset].ToJsonString()));
                }

                var body = new JsonObject
                {
                    ["index"] = index,
                    ["children"] = batch
                };
                var call = FeishuClient.Send(
                    "POST", FeishuClient.DocxChildrenUrl(documentId, documentId), body.ToJsonString(), appId, secretKey, timeoutSeconds);
                if (!call.Succeeded)
                {
                    return call.Response;
                }

                index += batch.Count;
            }

            return null;
        }

        private static BridgeResponse Success(JsonElement payload)
        {
            return BridgeResponse.Success(ContractVersion, payload);
        }

        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }

        private static string ReadConfigurationString(BridgeRequest request, string name, string defaultValue)
        {
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? defaultValue
                : defaultValue;
        }

        private static int ReadConfigurationInt(BridgeRequest request, string name, int defaultValue)
        {
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var parsed)
                ? parsed
                : defaultValue;
        }

        private static string ReadPayloadString(BridgeRequest request, string name)
        {
            return request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static bool ReadPayloadBool(BridgeRequest request, string name, bool defaultValue)
        {
            if (request.Payload.ValueKind != JsonValueKind.Object || !request.Payload.TryGetProperty(name, out var value))
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }

        /// <summary>按路径逐层取字符串；中间任何一层不是对象或缺键都返回空串。</summary>
        private static string ReadString(JsonElement element, params string[] path)
        {
            var current = element;
            foreach (var name in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                {
                    return "";
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
        }
    }
}
