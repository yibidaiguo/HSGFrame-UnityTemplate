using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>推一份模块策划案的结果。</summary>
    /// <param name="Pushed">真推了（或干跑算过了）没有。</param>
    /// <param name="Skipped">跳过了没有——正文没变、或者还没有 index.md。</param>
    /// <param name="Link">推成之后的文档链接；干跑或没推时为空串。</param>
    /// <param name="FailureReason">失败原因；没失败为空串。</param>
    /// <param name="Note">这一趟发生了什么，一句话，进执行流水。</param>
    public sealed record ModulePlanPushOutcome(
        bool Pushed, bool Skipped, string Link, string FailureReason, string Note);

    /// <summary>
    /// 把模块策划案推成知识库节点。
    ///
    /// **只推变了的**：正文哈希与 frontmatter 里记的「最后同步hash」一致就跳过。
    /// 这份文档每条需求验收都会重渲一遍，而重渲九成时候是无变化的——
    /// 不比对就等于每验收一条需求都往知识库写一次全量，白花调用还把节点的修改历史刷满，
    /// 而人翻修改历史正是为了看「这次到底改了什么」。
    ///
    /// 挂的父节点是**模块策划案父节点**，不是需求文档那个：一个模块一份、常驻的正本，
    /// 与几十条做完就归档的需求摆在同一层，人往知识库里找模块正本时会被需求淹掉。
    /// </summary>
    public static class ModulePlanPusher
    {
        /// <summary>模块策划案挂在知识库的哪个 port 下。</summary>
        public const string DocumentPortName = "模块策划案端";

        /// <summary>模块策划案在台账里的父节点键——推的时候告诉桥挂到哪儿。</summary>
        public const string ParentKeyName = "模块策划案父节点";

        /// <summary>
        /// 推一个模块的策划案。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="specification">模块策划案规范（要它的生成区标记来解析）。</param>
        /// <param name="isDryRun">干跑：算出要推什么但不真发。</param>
        /// <param name="isForced">强推：正文没变也推一次。</param>
        /// <param name="timeoutSeconds">下游调用超时秒数。</param>
        public static ModulePlanPushOutcome PushOne(
            string repositoryRoot,
            string poolRoot,
            string moduleName,
            PlanningDocumentSpec specification,
            bool isDryRun,
            bool isForced,
            int timeoutSeconds)
        {
            var documentPath = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
            if (!File.Exists(documentPath))
            {
                return new ModulePlanPushOutcome(
                    false, true, "", "", moduleName + "　跳过：还没有 index.md，先跑 plan.render");
            }

            string documentText;
            try
            {
                documentText = File.ReadAllText(documentPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ModulePlanPushOutcome(
                    false, false, "", exception.Message, moduleName + "　读不动：" + exception.Message);
            }

            if (!RequirementDocument.TryParse(
                documentText,
                specification.GeneratedRegionBegin,
                specification.GeneratedRegionEnd,
                out var parsed,
                out var parseReason))
            {
                return new ModulePlanPushOutcome(
                    false, false, "", parseReason, moduleName + " 的 index.md 解析不了：" + parseReason);
            }

            var syncState = RequirementDocumentSyncState.Read(parsed);
            var bodyHash = RequirementDocumentSyncState.HashBody(documentText);
            if (!isForced && !syncState.NeedsPush(bodyHash))
            {
                return new ModulePlanPushOutcome(
                    false, true, syncState.Link, "",
                    moduleName + "　跳过：正文与上次推上去的一致（" + bodyHash + "）");
            }

            var blocks = RequirementDocumentOutline.Build(documentText);
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = isDryRun,
                ["标题"] = ComposeTitle(moduleName, parsed),
                ["节点token"] = syncState.NodeToken,
                ["父节点键"] = ParentKeyName,
                ["块"] = RequirementDocumentOutline.ToJsonArray(blocks),

                // 媒体的相对路径（media/x.png）要有个根才展得开。给模块目录而不是 media 目录：
                // 正文里写的就是 media/… 这个相对写法，根给深一层就对不上了。
                ["媒体根目录"] = PoolPaths.ModulePlanDirectory(poolRoot, moduleName)
            });

            var call = BridgeInvoker.InvokeByPort(repositoryRoot, DocumentPortName, "doc", payload, timeoutSeconds);
            if (!call.Result.Succeeded)
            {
                return new ModulePlanPushOutcome(
                    false, false, "", call.Result.HumanText,
                    moduleName + "　失败（" + call.Result.ErrorCode + "）：" + call.Result.HumanText);
            }

            if (isDryRun)
            {
                return new ModulePlanPushOutcome(
                    true, false, "", "",
                    moduleName + "　干跑：" + ReadString(call.Result.Payload, "动作")
                        + "，" + blocks.Count + " 块（" + bodyHash + "）");
            }

            var nodeToken = ReadString(call.Result.Payload, "节点token");

            // 链接只有**新建**那一支回得出来（get_node 的响应里没有 url）。
            // 刷新时拿到空串就照写的话，会把上次那条好端端的链接抹掉。
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
                        new RequirementDocumentSyncState(
                            nodeToken, link, bodyHash, DateTimeOffset.Now.ToString("o"))),
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 推上去了但同步账写不回来：**下次会重推一遍**（哈希还是旧的）。
                // 这比谎称成功强——那样节点在、账没记，下次跳过，而人以为是最新的。
                return new ModulePlanPushOutcome(
                    true, false, link, exception.Message,
                    moduleName + "　推上去了，但同步账写不回来（下次会重推）：" + exception.Message);
            }

            return new ModulePlanPushOutcome(
                true, false, link, "",
                moduleName + "　推上去了：" + (link.Length > 0 ? link : nodeToken));
        }

        /// <summary>知识库节点叫什么：frontmatter 的「标题」优先，没有就用模块名。</summary>
        /// <param name="moduleName">模块名。</param>
        /// <param name="parsed">解析好的文档。</param>
        private static string ComposeTitle(string moduleName, RequirementDocument parsed)
        {
            var title = parsed?.FrontMatter?.Scalar("标题") ?? "";
            return title.Trim().Length > 0 ? title.Trim() : moduleName;
        }

        /// <summary>读桥回来的字符串字段；缺失给空串。</summary>
        /// <param name="payload">桥的响应载荷。</param>
        /// <param name="key">键名。</param>
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
