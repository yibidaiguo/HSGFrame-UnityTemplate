using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>策划文档渲染命令的参数。</summary>
    public sealed class PlanningDocRenderArguments
    {
        /// <summary>要渲染的需求 id，如「REQ-0042」；留空表示池子里全部需求。</summary>
        [Summary("要渲染的需求 id，如 REQ-0042；留空表示池子里全部需求")]
        public string RequirementIdentifier { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>干跑：算出全文但不写盘。</summary>
        [Summary("干跑：算出全文但不写盘")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>策划文档门禁命令的参数。</summary>
    public sealed class PlanningDocGateArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>策划文档推送命令的参数。</summary>
    public sealed class PlanningDocPushArguments
    {
        /// <summary>要推的需求 id，如「REQ-0042」；留空表示池子里全部需求。</summary>
        [Summary("要推的需求 id，如 REQ-0042；留空表示池子里全部需求")]
        public string RequirementIdentifier { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>干跑：算出要推什么但不真推。真推下游的东西，默认必须是干跑。</summary>
        [Summary("干跑：算出要推什么但不真推")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>正文没变也照推一次。</summary>
        [Summary("正文没变也照推一次")]
        [DefaultValue(false)]
        public bool Force { get; set; }

        /// <summary>单次调用超时秒数。</summary>
        [Summary("单次调用超时秒数")]
        [DefaultValue(60)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>策划文档命令：doc.render 生成/刷新文档，doc.push 推去下游，gate.plandoc 按规范查六条。</summary>
    public static class PlanningDocCommands
    {
        /// <summary>推策划文档走的 port 名；具体落到哪个 driver 由域路由表说了算。</summary>
        private const string DocumentPortName = "策划文档端";

        /// <summary>
        /// 把策划文档推去下游（飞书知识库是当前唯一的落点，但这条命令一个飞书的字都不认识——
        /// 它只认 port，driver 由域路由表挑）。
        ///
        /// **默认干跑**：真推是写别人的工作区，与 bridge.push / bridge.card 同一条规矩。
        /// **只推变了的**：正文哈希与 frontmatter 里记的「最后同步hash」一致就跳过，
        /// 要强推给 --force true。
        /// 推成之后把节点 token、链接、哈希与时间写回 frontmatter 的「同步」块——
        /// 那四样是下一次判定「要不要再推」的全部依据，不写回去等于每次都从零开始。
        /// </summary>
        /// <param name="arguments">推送命令参数。</param>
        [EditorCommand("doc.push")]
        [Summary("把策划文档推成下游的一份文档；默认干跑，--dry-run false 才真推")]
        public static CommandResult Push(PlanningDocPushArguments arguments)
        {
            if (!TryResolveRoots(arguments?.RepositoryRoot, arguments?.PoolRoot, out var repositoryRoot, out var poolRoot, out var failure))
            {
                return failure;
            }

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var identifiers = ResolveIdentifiers(poolRoot, arguments?.RequirementIdentifier);
            if (identifiers.Count == 0)
            {
                return CommandResult.Success("池子里没有需求，没什么可推的");
            }

            var isDryRun = arguments == null || arguments.DryRun;
            var isForced = arguments != null && arguments.Force;
            var timeoutSeconds = arguments == null || arguments.TimeoutSeconds <= 0 ? 60 : arguments.TimeoutSeconds;
            var lines = new List<string>();
            var pushedCount = 0;
            var skippedCount = 0;

            foreach (var identifier in identifiers)
            {
                var documentPath = PoolPaths.PlanningDocument(poolRoot, identifier);
                if (!File.Exists(documentPath))
                {
                    // 没有 index.md 不是违规（规范第五节最后一句），跳过并说清楚。
                    lines.Add($"{identifier}　跳过：还没有 index.md，先跑 doc.render");
                    skippedCount++;
                    continue;
                }

                var documentText = File.ReadAllText(documentPath);
                if (!PlanningDocument.TryParse(documentText, specification, out var parsed, out var parseReason))
                {
                    return CommandResult.Failure($"{identifier} 的 index.md 解析不了：{parseReason}");
                }

                var syncState = PlanningDocumentSyncState.Read(parsed);
                var bodyHash = PlanningDocumentSyncState.HashBody(documentText);
                if (!isForced && !syncState.NeedsPush(bodyHash))
                {
                    lines.Add($"{identifier}　跳过：正文与上次推上去的一致（{bodyHash}）");
                    skippedCount++;
                    continue;
                }

                var blocks = PlanningDocumentOutline.Build(documentText);
                var payload = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = isDryRun,
                    ["标题"] = ComposeTitle(identifier, parsed),
                    ["节点token"] = syncState.NodeToken,
                    ["块"] = PlanningDocumentOutline.ToJsonArray(blocks),

                    // 媒体的相对路径（media/x.png）要有个根才展得开。给需求目录而不是 media 目录：
                    // 正文里写的就是 media/… 这个相对写法，根给深一层就对不上了。
                    ["媒体根目录"] = PoolPaths.RequirementDirectory(poolRoot, identifier)
                });

                var call = BridgeInvoker.InvokeByPort(repositoryRoot, DocumentPortName, "doc", payload, timeoutSeconds);
                if (!call.Result.Succeeded)
                {
                    lines.Add($"{identifier}　失败：{call.Result.HumanText}");
                    return CommandResult.Failure(
                        $"推 {identifier} 失败（错误码：{call.Result.ErrorCode}）", lines);
                }

                if (isDryRun)
                {
                    var action = ReadString(call.Result.Payload, "动作");
                    lines.Add($"{identifier}　干跑：{action}，{blocks.Count} 块（{bodyHash}）");
                    pushedCount++;
                    continue;
                }

                var nodeToken = ReadString(call.Result.Payload, "节点token");

                // 链接只有**新建**那一支回得出来（get_node 的响应里没有 url）。
                // 刷新时拿到空串就照写的话，会把上次那条好端端的链接抹掉——
                // 任务表上挂的正是它。所以空就沿用旧的。
                var link = ReadString(call.Result.Payload, "链接");
                if (link.Length == 0)
                {
                    link = syncState.Link;
                }

                var updated = PlanningDocumentSyncState.Write(
                    documentText,
                    new PlanningDocumentSyncState(
                        nodeToken,
                        link,
                        bodyHash,
                        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
                File.WriteAllText(documentPath, updated);

                lines.Add($"{identifier}　{ReadString(call.Result.Payload, "动作")}：{link}");

                // 素材传没传上去要**逐条报**：正文推成功而图片全掉了，人只看一句「已刷新」
                // 是发现不了的——直到他打开文档看见一个空图框。
                var uploaded = ReadInt(call.Result.Payload, "传上去的素材");
                if (uploaded > 0)
                {
                    lines.Add($"{identifier}　素材传上去 {uploaded} 个");
                }

                foreach (var mediaFailure in ReadStringArray(call.Result.Payload, "没传上去的素材"))
                {
                    lines.Add($"{identifier}　素材没传上去：{mediaFailure}");
                }

                pushedCount++;
            }

            var head = isDryRun ? "干跑完成" : "推送完成";
            return CommandResult.Success($"{head}：推 {pushedCount} 条，跳过 {skippedCount} 条", lines);
        }

        /// <summary>
        /// 下游那个节点叫什么：`REQ-0042 七日签到`。
        /// **id 摆在最前面**——飞书那边一屏全是标题，没有 id 的话认不出哪份对应哪条需求。
        /// </summary>
        private static string ComposeTitle(string identifier, PlanningDocument document)
        {
            var title = document.FrontMatter.Scalar("标题");
            return title.Length == 0 ? identifier : identifier + " " + title;
        }

        /// <summary>从载荷里取一个字符串字段；不是字符串或没有时给空串。</summary>
        private static string ReadString(JsonElement payload, string name)
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        /// <summary>从载荷里取一个整数字段；不是数字或没有时给 0。</summary>
        /// <param name="payload">响应载荷。</param>
        /// <param name="name">字段名。</param>
        private static int ReadInt(JsonElement payload, string name)
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                ? number
                : 0;
        }

        /// <summary>从载荷里取一个字符串数组字段；没有或类型不对给空表。</summary>
        /// <param name="payload">响应载荷。</param>
        /// <param name="name">字段名。</param>
        private static IReadOnlyList<string> ReadStringArray(JsonElement payload, string name)
        {
            var values = new List<string>();
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty(name, out var element)
                || element.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }

        /// <summary>
        /// 按需求骨架生成或刷新 index.md：补工程负责的 frontmatter 键、补缺掉的必填小节、重生成生成区。
        /// </summary>
        /// <param name="arguments">渲染命令参数。</param>
        [EditorCommand("doc.render")]
        [Summary("按需求骨架生成或刷新策划文档 index.md")]
        public static CommandResult Render(PlanningDocRenderArguments arguments)
        {
            if (!TryResolveRoots(arguments?.RepositoryRoot, arguments?.PoolRoot, out var repositoryRoot, out var poolRoot, out var failure))
            {
                return failure;
            }

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var identifiers = ResolveIdentifiers(poolRoot, arguments?.RequirementIdentifier);
            if (identifiers.Count == 0)
            {
                return CommandResult.Success("没有需要渲染的需求");
            }

            var isDryRun = arguments != null && arguments.DryRun;
            var lines = new List<string>();
            var changedCount = 0;

            foreach (var identifier in identifiers)
            {
                PlanningDocumentRenderOutcome outcome;
                try
                {
                    outcome = PlanningDocumentRenderer.Render(repositoryRoot, poolRoot, identifier, specification, isDryRun);
                }
                catch (InvalidOperationException exception)
                {
                    return CommandResult.Failure($"{identifier}：{exception.Message}", lines);
                }

                if (outcome.IsChanged)
                {
                    changedCount++;
                }

                var action = outcome.IsCreated ? "新建" : (outcome.IsChanged ? "刷新" : "无变化");
                var addedText = outcome.AddedSections.Count == 0
                    ? ""
                    : "，补小节：" + string.Join("、", outcome.AddedSections);
                lines.Add($"{identifier}　{action}{addedText}　{RelativeTo(repositoryRoot, outcome.DocumentPath)}");
            }

            var head = isDryRun ? "干跑完成" : "渲染完成";
            return CommandResult.Success($"{head}：共 {identifiers.Count} 条需求，有变化 {changedCount} 条", lines);
        }

        /// <summary>
        /// 按策划文档规范查全部 index.md：frontmatter、id、小节顺序、验收标准、媒体、生成区。
        /// </summary>
        /// <param name="arguments">门禁命令参数。</param>
        [EditorCommand("gate.plandoc")]
        [Summary("策划文档门禁：按基线规范查 index.md 的六条")]
        public static CommandResult Check(PlanningDocGateArguments arguments)
        {
            if (!TryResolveRoots(arguments?.RepositoryRoot, arguments?.PoolRoot, out var repositoryRoot, out var poolRoot, out var failure))
            {
                return failure;
            }

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = PlanningDocumentChecker.CheckAll(poolRoot, specification);
            if (findings.Count == 0)
            {
                return CommandResult.Success("策划文档门禁通过，问题 0 条");
            }

            return CommandResult.Failure(
                $"策划文档门禁失败，问题 {findings.Count} 条",
                findings.Select(finding => finding.ToDisplayText()).ToList());
        }

        private static bool TryResolveRoots(
            string repositoryRootArgument,
            string poolRootArgument,
            out string repositoryRoot,
            out string poolRoot,
            out CommandResult failure)
        {
            repositoryRoot = "";
            poolRoot = "";
            failure = null;

            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(repositoryRootArgument) ? "." : repositoryRootArgument);
                poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(poolRootArgument) ? "Pools" : poolRootArgument);
            }
            catch (Exception exception)
            {
                failure = CommandResult.Failure($"根目录无法解析为绝对路径：{exception.Message}");
                return false;
            }

            if (!Directory.Exists(poolRoot))
            {
                failure = CommandResult.Failure($"池子根目录不存在：{poolRoot}");
                return false;
            }

            return true;
        }

        private static IReadOnlyList<string> ResolveIdentifiers(string poolRoot, string requirementIdentifier)
        {
            if (!string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                return new[] { requirementIdentifier.Trim() };
            }

            return PoolPaths.EnumerateRequirementIdentifiers(poolRoot);
        }

        private static string RelativeTo(string repositoryRoot, string path)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
