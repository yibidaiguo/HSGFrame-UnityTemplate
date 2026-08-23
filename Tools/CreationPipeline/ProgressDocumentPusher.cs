using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>推一次进度文档的结果。</summary>
    /// <param name="Pushed">真推了（或干跑算过了）没有。</param>
    /// <param name="Skipped">跳过了没有——正文与上次推上去的一致。</param>
    /// <param name="Link">文档链接；干跑或没推时为空串。</param>
    /// <param name="FailureReason">失败原因；没失败为空串。</param>
    /// <param name="Note">这一趟发生了什么，一句话。</param>
    public sealed record ProgressDocumentPushOutcome(
        bool Pushed, bool Skipped, string Link, string FailureReason, string Note);

    /// <summary>
    /// 把项目进度文档推成知识库里的一份文档。
    ///
    /// 与需求文档、模块策划案共用同一条通道（<c>doc</c> 动作 + frontmatter 同步账），
    /// 只有两处不同：
    /// - **只有一份**，所以同步账写在这份文档自己的 frontmatter 里，没有别的地方要记；
    /// - **默认挂成一级节点**。台账里配了「进度文档父节点」就挂到那儿；没配时桥会把它
    ///   建成一级节点，那正是想要的——整个项目的进度是门面，埋在几十条需求下面没人找得到。
    /// </summary>
    public static class ProgressDocumentPusher
    {
        /// <summary>进度文档挂在知识库的哪个 port 下——与需求文档同一个 port。</summary>
        public const string DocumentPortName = "需求文档端";

        /// <summary>台账里进度文档父节点那一格的键名；台账里没有这一格时挂成一级节点。</summary>
        public const string ParentKeyName = "进度文档父节点";

        /// <summary>
        /// 推一次进度文档。文档还没渲染出来时直接报——**不在这里顺手渲染**：
        /// 渲染要拿快照与权威侧表，那是调用方手里的东西，在这里再取一遍就有两处会算出不同的正文。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="isDryRun">干跑：算出要推什么但不真发。</param>
        /// <param name="isForced">强推：正文没变也推一次。</param>
        /// <param name="timeoutSeconds">下游调用超时秒数。</param>
        public static ProgressDocumentPushOutcome Push(
            string repositoryRoot,
            bool isDryRun,
            bool isForced,
            int timeoutSeconds)
        {
            var documentPath = ProgressDocumentRenderer.DocumentFile(repositoryRoot);
            if (!File.Exists(documentPath))
            {
                return new ProgressDocumentPushOutcome(
                    false, true, "", "", "跳过：还没有进度文档，先跑一次 sync.progress 让它渲染出来");
            }

            string documentText;
            try
            {
                documentText = File.ReadAllText(documentPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ProgressDocumentPushOutcome(false, false, "", exception.Message, "进度文档读不动：" + exception.Message);
            }

            if (!RequirementDocument.TryParse(
                documentText,
                ProgressDocumentRenderer.GeneratedRegionBegin,
                ProgressDocumentRenderer.GeneratedRegionEnd,
                out var parsed,
                out var parseReason))
            {
                return new ProgressDocumentPushOutcome(false, false, "", parseReason, "进度文档解析不了：" + parseReason);
            }

            var syncState = RequirementDocumentSyncState.Read(parsed);
            var bodyHash = RequirementDocumentSyncState.HashBody(documentText);
            if (!isForced && !syncState.NeedsPush(bodyHash))
            {
                return new ProgressDocumentPushOutcome(
                    false, true, syncState.Link, "", "跳过：正文与上次推上去的一致（" + bodyHash + "）");
            }

            var blocks = RequirementDocumentOutline.Build(documentText);
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = isDryRun,
                ["标题"] = parsed.FrontMatter.Scalar("标题") is { Length: > 0 } title ? title : "项目进度",
                ["节点token"] = syncState.NodeToken,
                ["父节点键"] = ParentKeyName,
                ["块"] = RequirementDocumentOutline.ToJsonArray(blocks),
                ["媒体根目录"] = Path.GetDirectoryName(documentPath) ?? ""
            });

            var call = BridgeInvoker.InvokeByPort(repositoryRoot, DocumentPortName, "doc", payload, timeoutSeconds);
            if (!call.Result.Succeeded)
            {
                return new ProgressDocumentPushOutcome(
                    false, false, "", call.Result.HumanText,
                    "进度文档推送失败（" + call.Result.ErrorCode + "）：" + call.Result.HumanText);
            }

            if (isDryRun)
            {
                return new ProgressDocumentPushOutcome(
                    true, false, "", "",
                    "干跑：" + ReadString(call.Result.Payload, "动作") + "，" + blocks.Count + " 块（" + bodyHash + "）");
            }

            var nodeToken = ReadString(call.Result.Payload, "节点token");
            var link = ReadString(call.Result.Payload, "链接");
            if (link.Length == 0)
            {
                link = syncState.Link;
            }

            try
            {
                File.WriteAllText(
                    documentPath,
                    RequirementDocumentSyncState.Write(
                        documentText,
                        new RequirementDocumentSyncState(nodeToken, link, bodyHash, DateTimeOffset.Now.ToString("o"))),
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 推上去了但同步账写不回来：下次会重推一遍（哈希还是旧的）。比谎称成功强。
                return new ProgressDocumentPushOutcome(
                    true, false, link, exception.Message,
                    "进度文档推上去了，但同步账写不回来（下次会重推）：" + exception.Message);
            }

            return new ProgressDocumentPushOutcome(
                true, false, link, "",
                "进度文档推上去了：" + (link.Length > 0 ? link : nodeToken));
        }

        /// <summary>读桥回来的字符串字段；缺失给空串。</summary>
        private static string ReadString(JsonElement payload, string key)
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : "";
        }
    }
}
