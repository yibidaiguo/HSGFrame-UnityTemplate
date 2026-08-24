using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>进度同步命令 sync.progress 的参数。</summary>
    public sealed class ProgressSyncArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>同步方向：出站 / 入站 / 双向。</summary>
        [Summary("同步方向：出站 / 入站 / 双向")]
        [DefaultValue("双向")]
        public string Direction { get; set; }

        /// <summary>干跑：算出要同步什么但不真发、不落账。</summary>
        [Summary("干跑：算出要同步什么但不真发、不落账")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>顺带把进度文档推成知识库节点。</summary>
        [Summary("顺带把进度文档推成知识库节点")]
        [DefaultValue(false)]
        public bool PushDocument { get; set; }

        /// <summary>项目名，进进度文档的标题；留空取仓库目录名。</summary>
        [Summary("项目名，进进度文档的标题；留空取仓库目录名")]
        [DefaultValue("")]
        public string ProjectName { get; set; }

        /// <summary>单次下游调用超时秒数。</summary>
        [Summary("单次下游调用超时秒数")]
        [DefaultValue(60)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>进度视图命令 sync.progress.view 的参数。</summary>
    public sealed class ProgressViewArguments
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

    /// <summary>
    /// 项目进度在**仓库 ↔ 飞书 ↔ 本地面板**之间的同步。
    ///
    /// 三处的分工不对称，说清楚才不会指望错东西：
    /// - **仓库**是工程侧那几格的事实源（阶段、门禁、产出全是算出来的）；
    /// - **飞书**是策划端那几格的事实源（执行人、进展、预计完成、进展记录全是人填的）；
    /// - **面板**读的就是仓库，所以它天然与仓库同步，是**视图不是第三方**——
    ///   把面板也做成能写的一侧，等于给同一份数据开第三个入口，而三方冲突没人算得清。
    ///
    /// 双向的落法：每一格按 <see cref="ProgressSyncSchema"/> 的权威侧单向复制；
    /// 两侧相对上次同步都动过的那几格**一个都不许覆盖**，落成冲突条目
    /// （发现阶段「进度同步」）交给既有的 conflict.list / conflict.resolve / gate.conflict。
    /// </summary>
    public static class ProgressSyncCommands
    {
        /// <summary>任务表住在哪个 port 下。</summary>
        private const string TablePortName = "需求编辑端";

        /// <summary>同步方向的三个取值。</summary>
        private static readonly string[] AllowedDirections = { "出站", "入站", "双向" };

        /// <summary>任务表里存需求 id 的列名。</summary>
        private const string IdentifierColumn = "需求id";

        /// <summary>
        /// 跑一轮进度同步。
        /// </summary>
        /// <param name="arguments">进度同步命令参数。</param>
        [EditorCommand("sync.progress")]
        [Summary("项目进度在仓库、飞书与面板之间双向同步：按权威侧单向复制，两侧都改过的落冲突")]
        public static CommandResult Execute(ProgressSyncArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var direction = string.IsNullOrWhiteSpace(arguments.Direction) ? "双向" : arguments.Direction.Trim();
            if (!AllowedDirections.Contains(direction, StringComparer.Ordinal))
            {
                return CommandResult.Failure($"同步方向「{direction}」不合法，只有：{string.Join("、", AllowedDirections)}");
            }

            var repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.PoolRoot) ? "Pools" : arguments.PoolRoot);
            var isDryRun = arguments.DryRun;
            var timeoutSeconds = arguments.TimeoutSeconds <= 0 ? 60 : arguments.TimeoutSeconds;
            var lines = new List<string>();

            var schema = ProgressSyncSchema.Load(repositoryRoot);
            if (schema.LoadFailureReason.Length > 0)
            {
                return CommandResult.Failure("权威侧表有问题：" + schema.LoadFailureReason);
            }

            lines.Add($"权威侧表：工程 {schema.EngineFields().Count} 格，策划端 {schema.PlannerFields().Count} 格");

            // 仓库侧那一份要**带上回流账**：策划端那几格在仓库里的值就住在回流账里。
            // 不带的话工程侧对它们永远是空串，每一轮都会判成「下游单边改过」而反复回流。
            var engineSnapshot = ProgressSnapshot
                .CollectFromRepository(repositoryRoot, poolRoot)
                .MergePlannerFields(
                    ProgressInboundLedger.Load(repositoryRoot),
                    schema.PlannerFields().Select(field => field.Name));
            lines.Add($"仓库侧：{engineSnapshot.Entries.Count} 条需求，门禁 {Global(engineSnapshot, "门禁")}，队列 {Global(engineSnapshot, "队列长度")}");

            if (!TryReadDownstream(repositoryRoot, schema, timeoutSeconds, out var downstreamSnapshot, out var downstreamFailure))
            {
                return CommandResult.Failure("读不回下游任务表：" + downstreamFailure, lines);
            }

            lines.Add($"下游任务表：{downstreamSnapshot.Entries.Count} 行");

            var baseline = ProgressSyncBaseline.Load(repositoryRoot, out var hasBaseline, out var baselineFailure);
            if (baselineFailure.Length > 0)
            {
                // 基线坏了就停：继续跑会把真冲突当成单边改动覆盖掉，而那种覆盖没有痕迹。
                return CommandResult.Failure(baselineFailure + "；修好或删掉它再来（删掉 = 当第一次同步）", lines);
            }

            lines.Add(hasBaseline ? "基线：有" : "基线：没有（这是第一次同步，任何差异都按权威侧走，不判冲突）");

            var plan = ProgressSyncPlanner.Plan(engineSnapshot, downstreamSnapshot, baseline, hasBaseline, schema);
            var outbound = plan.Outbound();
            var inbound = plan.Inbound();
            var conflicts = plan.Conflicts();
            lines.Add($"裁定：出站 {outbound.Count} 格，入站 {inbound.Count} 格，冲突 {conflicts.Count} 格，待建行 {plan.RowsToCreate.Count} 条");

            foreach (var decision in conflicts)
            {
                lines.Add($"　冲突　{decision.Identifier}.{decision.FieldName}：工程「{decision.EngineValue}」/ 下游「{decision.DownstreamValue}」/ 上次「{decision.BaselineValue}」");
            }

            // 冲突先落账再动手：真冲突还没被人看见时，这一轮的覆盖动作一格都不该发生
            // ——但那只针对冲突的那几格，其余格照常同步（把整轮停掉的话，
            // 一格陈年未决冲突能让整条进度链永远停在那里）。
            var conflictNotes = new List<string>();
            if (!isDryRun && conflicts.Count > 0)
            {
                RecordConflicts(poolRoot, conflicts, conflictNotes);
                lines.AddRange(conflictNotes);
            }
            else if (conflicts.Count > 0)
            {
                lines.Add("　（干跑：冲突条目没落账）");
            }

            if (direction != "入站")
            {
                if (!RunOutbound(repositoryRoot, poolRoot, schema, engineSnapshot, plan, isDryRun, timeoutSeconds, lines, out var outboundFailure))
                {
                    return CommandResult.Failure("出站失败：" + outboundFailure, lines);
                }
            }
            else
            {
                lines.Add("出站：方向是入站，跳过");
            }

            ProgressSnapshot inboundSnapshot;
            if (direction != "出站")
            {
                inboundSnapshot = isDryRun
                    ? ProgressInboundLedger.Load(repositoryRoot)
                    : ProgressInboundLedger.Save(repositoryRoot, plan, schema, DateTimeOffset.Now.ToString("o"));
                lines.Add(isDryRun
                    ? $"入站：干跑，{inbound.Count} 格没写进回流账"
                    : $"入站：{inbound.Count} 格写进 {Relative(repositoryRoot, ProgressInboundLedger.LedgerFile(repositoryRoot))}");
            }
            else
            {
                inboundSnapshot = ProgressInboundLedger.Load(repositoryRoot);
                lines.Add("入站：方向是出站，跳过");
            }

            // 进度文档每轮都渲染（它是本地产物，渲染不花钱也不碰下游）；推不推另说。
            var documentPath = ProgressDocumentRenderer.DocumentFile(repositoryRoot);
            var projectName = string.IsNullOrWhiteSpace(arguments.ProjectName)
                ? Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : arguments.ProjectName.Trim();
            var documentText = ProgressDocumentRenderer.Render(
                projectName, engineSnapshot, inboundSnapshot, schema,
                DateTimeOffset.Now.ToString("o"),
                ProgressDocumentRenderer.ReadSyncState(repositoryRoot));
            try
            {
                var directory = Path.GetDirectoryName(documentPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(documentPath, documentText, new UTF8Encoding(false));
                lines.Add("进度文档已渲染：" + Relative(repositoryRoot, documentPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                lines.Add("进度文档写不动：" + exception.Message);
            }

            if (arguments.PushDocument)
            {
                var push = ProgressDocumentPusher.Push(repositoryRoot, isDryRun, isForced: false, timeoutSeconds);
                lines.Add(push.Note);
            }

            // 基线只在**真跑完**之后前进。干跑不动它，出站失败也走不到这里。
            if (!isDryRun)
            {
                ProgressSyncBaseline.Save(repositoryRoot, plan.SettledSnapshot(engineSnapshot));
                lines.Add("基线已更新：" + Relative(repositoryRoot, ProgressSyncBaseline.BaselineFile(repositoryRoot)));
            }

            var head = isDryRun ? "干跑完成" : "同步完成";
            var summary = $"{head}：出站 {outbound.Count} 格 · 入站 {inbound.Count} 格 · 冲突 {conflicts.Count} 格";
            return CommandResult.Success(summary, lines);
        }

        /// <summary>
        /// 只看不动：把这一刻的进度渲染成文本树，命令与面板同源。
        /// </summary>
        /// <param name="arguments">进度视图命令参数。</param>
        [EditorCommand("sync.progress.view")]
        [Summary("看一眼当前进度：工程侧那几格 + 回流回来的那几格，一个字节都不往下游发")]
        public static CommandResult View(ProgressViewArguments arguments)
        {
            var repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments?.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments?.PoolRoot) ? "Pools" : arguments.PoolRoot);

            var schema = ProgressSyncSchema.Load(repositoryRoot);
            if (schema.LoadFailureReason.Length > 0)
            {
                return CommandResult.Failure("权威侧表有问题：" + schema.LoadFailureReason);
            }

            var engineSnapshot = ProgressSnapshot.CollectFromRepository(repositoryRoot, poolRoot);
            var inbound = ProgressInboundLedger.Load(repositoryRoot);

            var lines = new List<string>();
            foreach (var pair in engineSnapshot.Global.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                lines.Add($"{pair.Key}：{pair.Value}");
            }

            lines.Add("");
            foreach (var entry in engineSnapshot.Entries)
            {
                var inboundEntry = inbound.Find(entry.Identifier);
                var cells = schema.Fields.Select(field => field.Name + "=" + (field.IsEngineOwned
                    ? entry.Value(field.Name)
                    : inboundEntry?.Value(field.Name) ?? ""));
                lines.Add($"{entry.Identifier}　{string.Join(" · ", cells)}");
            }

            return CommandResult.Success($"进度：{engineSnapshot.Entries.Count} 条需求", lines);
        }

        /// <summary>读下游任务表并折成快照。</summary>
        private static bool TryReadDownstream(
            string repositoryRoot,
            ProgressSyncSchema schema,
            int timeoutSeconds,
            out ProgressSnapshot snapshot,
            out string failureReason)
        {
            snapshot = new ProgressSnapshot(null, null);
            failureReason = "";

            var call = BridgeInvoker.InvokeByPort(
                repositoryRoot, TablePortName, "task-rows",
                JsonSerializer.SerializeToElement(new JsonObject()), timeoutSeconds);
            if (!call.Result.Succeeded)
            {
                failureReason = call.Result.ErrorCode + "：" + call.Result.HumanText;
                return false;
            }

            var rows = new List<IReadOnlyDictionary<string, string>>();
            if (call.Result.Payload.ValueKind == JsonValueKind.Object
                && call.Result.Payload.TryGetProperty("行", out var rowArray)
                && rowArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowArray.EnumerateArray())
                {
                    if (!row.TryGetProperty("字段", out var fields) || fields.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var property in fields.EnumerateObject())
                    {
                        map[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? ""
                            : property.Value.ToString();
                    }

                    rows.Add(map);
                }
            }

            snapshot = ProgressSnapshot.FromDownstreamRows(rows, schema, IdentifierColumn);
            return true;
        }

        /// <summary>
        /// 出站：先给缺行的需求建行，再把工程侧那几格改上去。
        /// 顺序不能反——先改后建的话，刚建出来的那些行这一轮一格都没写上，
        /// 而基线会记成「已经同步过」，于是要等到下一次某一格变动才补得上。
        /// </summary>
        private static bool RunOutbound(
            string repositoryRoot,
            string poolRoot,
            ProgressSyncSchema schema,
            ProgressSnapshot engineSnapshot,
            ProgressSyncPlan plan,
            bool isDryRun,
            int timeoutSeconds,
            List<string> lines,
            out string failureReason)
        {
            failureReason = "";

            foreach (var identifier in plan.RowsToCreate)
            {
                var entry = engineSnapshot.Find(identifier);
                var payload = new JsonObject
                {
                    ["干跑"] = isDryRun,
                    ["需求id"] = identifier,
                    ["任务描述"] = entry?.Value(ProgressSnapshot.TitleField) ?? identifier
                };

                var link = ReadRequirementDocumentLink(poolRoot, repositoryRoot, identifier);
                if (link.Length > 0)
                {
                    payload["需求文档链接"] = link;
                }

                var call = BridgeInvoker.InvokeByPort(
                    repositoryRoot, TablePortName, "task-row",
                    JsonSerializer.SerializeToElement(payload), timeoutSeconds);
                lines.Add(call.Result.Succeeded
                    ? $"　建行　{identifier}{(isDryRun ? "（干跑）" : "")}"
                    : $"　建行失败　{identifier}：{call.Result.HumanText}");
            }

            var byIdentifier = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var decision in plan.Outbound())
            {
                var field = schema.Find(decision.FieldName);
                if (field == null)
                {
                    continue;
                }

                if (!byIdentifier.TryGetValue(decision.Identifier, out var fields))
                {
                    fields = new JsonObject();
                    byIdentifier[decision.Identifier] = fields;
                }

                fields[field.DownstreamColumn] = decision.EngineValue;
            }

            if (byIdentifier.Count == 0)
            {
                lines.Add("出站：没有要改的格");
                return true;
            }

            var updates = new JsonArray();
            foreach (var pair in byIdentifier.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                updates.Add(new JsonObject { ["需求id"] = pair.Key, ["字段"] = pair.Value.DeepClone() });
            }

            var setCall = BridgeInvoker.InvokeByPort(
                repositoryRoot, TablePortName, "task-row-set",
                JsonSerializer.SerializeToElement(new JsonObject { ["干跑"] = isDryRun, ["更新"] = updates }),
                timeoutSeconds);
            if (!setCall.Result.Succeeded)
            {
                failureReason = setCall.Result.ErrorCode + "：" + setCall.Result.HumanText;
                return false;
            }

            lines.Add($"出站：{byIdentifier.Count} 条需求的格改上去了{(isDryRun ? "（干跑）" : "")}");
            foreach (var note in ReadStringArray(setCall.Result.Payload, "没改的"))
            {
                lines.Add("　没改　" + note);
            }

            return true;
        }

        /// <summary>
        /// 把冲突落进冲突列表。**同一格重复冲突不重复落账**：
        /// 未决的同名条目还在时跳过，否则每跑一轮同步就多一条 CF，
        /// 而冲突页上一屏全是同一件事，人反而看不见新出现的那条。
        /// </summary>
        private static void RecordConflicts(string poolRoot, IReadOnlyList<ProgressSyncDecision> conflicts, List<string> notes)
        {
            ConflictList existing;
            try
            {
                existing = ConflictList.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                notes.Add("　冲突落账失败：读不了冲突列表（" + exception.Message + "）");
                return;
            }

            var pending = new HashSet<string>(
                existing.Entries
                    .Where(entry => string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal))
                    .Select(entry => entry.OldIdentifier + "→" + entry.NewIdentifier),
                StringComparer.Ordinal);

            foreach (var decision in conflicts)
            {
                var downstreamSide = decision.Identifier + "." + decision.FieldName + "@下游";
                var engineSide = decision.Identifier + "." + decision.FieldName + "@工程";
                if (pending.Contains(downstreamSide + "→" + engineSide))
                {
                    notes.Add($"　冲突已在账上，没重复落：{decision.Identifier}.{decision.FieldName}");
                    continue;
                }

                try
                {
                    var entry = ConflictList.Append(poolRoot, downstreamSide, engineSide, "进度同步");
                    pending.Add(downstreamSide + "→" + engineSide);
                    notes.Add($"　冲突已落账：{entry.Identifier}　{decision.Identifier}.{decision.FieldName}");
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    notes.Add($"　冲突落账失败：{decision.Identifier}.{decision.FieldName}（{exception.Message}）");
                }
            }
        }

        /// <summary>读需求文档的链接；读不到给空串。</summary>
        private static string ReadRequirementDocumentLink(string poolRoot, string repositoryRoot, string identifier)
        {
            try
            {
                var documentPath = PoolPaths.RequirementDocument(poolRoot, identifier);
                if (!File.Exists(documentPath))
                {
                    return "";
                }

                var specification = RequirementDocumentSpec.Load(repositoryRoot);
                if (!RequirementDocument.TryParse(File.ReadAllText(documentPath), specification, out var parsed, out _))
                {
                    return "";
                }

                return RequirementDocumentSyncState.Read(parsed).Link;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                return "";
            }
        }

        /// <summary>读快照的全局格；缺失给「—」。</summary>
        private static string Global(ProgressSnapshot snapshot, string key)
        {
            return snapshot.Global.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : "—";
        }

        /// <summary>把绝对路径压成仓库相对路径，给人看。</summary>
        private static string Relative(string repositoryRoot, string filePath)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return filePath;
            }
        }

        /// <summary>从载荷里取字符串数组；没有或类型不对给空表。</summary>
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
    }
}
