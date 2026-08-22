using System;
using System.Collections.Generic;
using System.IO;
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
            var children = FeishuBlockCodec.ToChildren(blocks, out var pendingMedia);
            var mediaRoot = ReadPayloadString(request, "媒体根目录");
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

                // get_node 的响应里**没有 url**（只有建节点那一支回）。刷新时不补一次的话，
                // 链接就是空的——而任务表上挂的正是它，空一次就把好端端的地址抹掉了。
                link = ReadString(node.ResponseBody, "data", "node", "url");
                if (link.Length == 0)
                {
                    link = ComposeWikiLink(
                        QueryDocumentUrl(documentId, appId, secretKey, timeoutSeconds),
                        nodeToken);
                }
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

            var createdBlockIds = new List<string>();
            var written = WriteChildren(documentId, children, appId, secretKey, timeoutSeconds, createdBlockIds);
            if (written != null)
            {
                return written;
            }

            var mediaOutcome = UploadPendingMedia(
                pendingMedia, createdBlockIds, mediaRoot, appId, secretKey, timeoutSeconds);

            var result = new JsonObject
            {
                ["节点token"] = nodeToken,
                ["链接"] = link,
                ["文档id"] = documentId,
                ["块数"] = children.Count,
                ["动作"] = willCreate ? "已新建" : "已刷新",
                ["传上去的素材"] = mediaOutcome.UploadedCount
            };

            if (mediaOutcome.Failures.Count > 0)
            {
                var failures = new JsonArray();
                foreach (var failure in mediaOutcome.Failures)
                {
                    failures.Add(failure);
                }

                result["没传上去的素材"] = failures;
            }

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
            string documentId, JsonArray children, string appId, string secretKey, int timeoutSeconds,
            List<string> createdBlockIds)
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

                // 记下这一批建出来的块 id，顺序与请求里的 children 一一对应。
                // 图片与文件块的本体要靠它才传得上去——没有块 id 就没有 parent_node。
                CollectBlockIds(call.ResponseBody, createdBlockIds);

                index += batch.Count;
            }

            return null;
        }

        /// <summary>
        /// 按文档 id 查它的地址。走云空间的元信息接口——知识库那一侧只有建节点时才给 url，
        /// 之后再想知道「这份文档的地址是什么」就只能问这里。
        /// 查不到给空串：拿不到地址是缺一条信息，不该让整篇推送失败。
        /// </summary>
        /// <param name="documentId">文档 id（节点的 obj_token）。</param>
        /// <param name="appId">飞书应用标识。</param>
        /// <param name="secretKey">飞书应用密钥。</param>
        /// <param name="timeoutSeconds">单次调用超时秒数。</param>
        private static string QueryDocumentUrl(string documentId, string appId, string secretKey, int timeoutSeconds)
        {
            if (documentId.Length == 0)
            {
                return "";
            }

            var body = new JsonObject
            {
                ["request_docs"] = new JsonArray
                {
                    new JsonObject { ["doc_token"] = documentId, ["doc_type"] = "docx" }
                },
                ["with_url"] = true
            }.ToJsonString();

            var call = FeishuClient.Send("POST", FeishuClient.DriveMetasUrl(), body, appId, secretKey, timeoutSeconds);
            if (!call.Succeeded
                || call.ResponseBody.ValueKind != JsonValueKind.Object
                || !call.ResponseBody.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("metas", out var metas)
                || metas.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            foreach (var meta in metas.EnumerateArray())
            {
                var url = ReadString(meta, "url");
                if (url.Length > 0)
                {
                    return url;
                }
            }

            return "";
        }

        /// <summary>
        /// 把云空间查回来的文档地址换成知识库地址：域名照抄，路径换成 <c>/wiki/&lt;节点&gt;</c>。
        ///
        /// 两条地址都能打开同一份文档，但**这份文档是住在知识库里的**——
        /// 知识库那条带左侧目录，人点进去看得见它挂在「需求」底下；
        /// 云空间那条是一份孤零零的文档，看不出它属于哪儿。
        /// 抠不出域名时原样返回查到的地址：能打开的链接好过没有链接。
        /// </summary>
        /// <param name="documentUrl">云空间查回来的地址。</param>
        /// <param name="nodeToken">知识库节点 token。</param>
        private static string ComposeWikiLink(string documentUrl, string nodeToken)
        {
            if (documentUrl.Length == 0 || nodeToken.Length == 0)
            {
                return documentUrl;
            }

            if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var parsed))
            {
                return documentUrl;
            }

            return parsed.Scheme + "://" + parsed.Host + "/wiki/" + nodeToken;
        }

        /// <summary>把一次写子块响应里的 block_id 按顺序收进清单。</summary>
        /// <param name="body">响应体。</param>
        /// <param name="createdBlockIds">收集到哪里去。</param>
        private static void CollectBlockIds(JsonElement body, List<string> createdBlockIds)
        {
            if (createdBlockIds == null
                || body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("children", out var children)
                || children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var child in children.EnumerateArray())
            {
                createdBlockIds.Add(
                    child.ValueKind == JsonValueKind.Object
                        && child.TryGetProperty("block_id", out var blockId)
                        && blockId.ValueKind == JsonValueKind.String
                        ? blockId.GetString() ?? ""
                        : "");
            }
        }

        /// <summary>一轮素材上传的结果：传上去几个、哪几个没传上去。</summary>
        private sealed class MediaUploadOutcome
        {
            /// <summary>传上去了几个。</summary>
            public int UploadedCount;

            /// <summary>没传上去的，一条一句人话。</summary>
            public readonly List<string> Failures = new List<string>();
        }

        /// <summary>
        /// 把待传素材逐个传上去，挂到刚建出来的那个空块上。
        ///
        /// **单个素材失败不算整篇失败**：正文已经推上去了，少一张图是缺一块内容，
        /// 而整篇判失败会让调用方以为文档没推成、下次又整篇重推一遍。所以这里只记账、不返回失败。
        /// </summary>
        /// <param name="pendingMedia">待传素材。</param>
        /// <param name="createdBlockIds">按顺序建出来的块 id。</param>
        /// <param name="mediaRoot">需求目录，素材的相对路径按它展开。</param>
        /// <param name="appId">飞书应用标识。</param>
        /// <param name="secretKey">飞书应用密钥。</param>
        /// <param name="timeoutSeconds">单次调用超时秒数。</param>
        private static MediaUploadOutcome UploadPendingMedia(
            IReadOnlyList<FeishuBlockCodec.PendingMedia> pendingMedia,
            IReadOnlyList<string> createdBlockIds,
            string mediaRoot,
            string appId,
            string secretKey,
            int timeoutSeconds)
        {
            var outcome = new MediaUploadOutcome();
            if (pendingMedia == null || pendingMedia.Count == 0)
            {
                return outcome;
            }

            if (string.IsNullOrWhiteSpace(mediaRoot))
            {
                outcome.Failures.Add("载荷没给「媒体根目录」，素材的相对路径没法展开成真路径");
                return outcome;
            }

            foreach (var media in pendingMedia)
            {
                if (media.ChildIndex < 0 || media.ChildIndex >= createdBlockIds.Count)
                {
                    outcome.Failures.Add(media.RelativePath + "：对不上块 id（建出来的块比请求里的少）");
                    continue;
                }

                var blockId = createdBlockIds[media.ChildIndex];
                if (blockId.Length == 0)
                {
                    outcome.Failures.Add(media.RelativePath + "：那个块没回 block_id");
                    continue;
                }

                string filePath;
                try
                {
                    filePath = Path.GetFullPath(Path.Combine(mediaRoot, media.RelativePath));
                }
                catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
                {
                    outcome.Failures.Add(media.RelativePath + "：路径拼不出来（" + exception.Message + "）");
                    continue;
                }

                var call = FeishuClient.UploadMedia(filePath, media.ParentType, blockId, appId, secretKey, timeoutSeconds);
                if (call.Succeeded)
                {
                    outcome.UploadedCount++;
                    continue;
                }

                outcome.Failures.Add(media.RelativePath + "：" + (call.Response?.Error?.HumanText ?? "上传失败"));
            }

            return outcome;
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
