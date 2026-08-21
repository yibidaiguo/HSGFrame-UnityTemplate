using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>重规划落地的结果：做了什么、拒了没有、为什么拒。</summary>
    public sealed class ReplanLandingResult
    {
        /// <summary>
        /// 构造一次重规划落地结果。
        /// </summary>
        /// <param name="applied">真落地了没有。</param>
        /// <param name="refusalReason">拒绝落地的原因；落地了就是空串。</param>
        /// <param name="snapshotVersion">写出的快照版次；没落地是 0。</param>
        /// <param name="markedDirty">真被标成「标脏」的工作项 id，序数序。</param>
        /// <param name="keptClean">原样保留、一个字都没改的工作项 id，序数序。</param>
        /// <param name="previousStage">落地前的阶段。</param>
        /// <param name="previousSubState">落地前的子状态。</param>
        /// <param name="findings">过程中的中文文案。</param>
        internal ReplanLandingResult(
            bool applied,
            string refusalReason,
            int snapshotVersion,
            IReadOnlyList<string> markedDirty,
            IReadOnlyList<string> keptClean,
            string previousStage,
            string previousSubState,
            IReadOnlyList<string> findings)
        {
            Applied = applied;
            RefusalReason = refusalReason ?? "";
            SnapshotVersion = snapshotVersion;
            MarkedDirty = markedDirty ?? Array.Empty<string>();
            KeptClean = keptClean ?? Array.Empty<string>();
            PreviousStage = previousStage ?? "";
            PreviousSubState = previousSubState ?? "";
            Findings = findings ?? Array.Empty<string>();
        }

        /// <summary>真落地了没有。</summary>
        public bool Applied { get; }

        /// <summary>拒绝落地的原因；落地了就是空串。</summary>
        public string RefusalReason { get; }

        /// <summary>写出的快照版次；没落地是 0。</summary>
        public int SnapshotVersion { get; }

        /// <summary>真被标成「标脏」的工作项 id，序数序。</summary>
        public IReadOnlyList<string> MarkedDirty { get; }

        /// <summary>原样保留、一个字都没改的工作项 id，序数序。</summary>
        public IReadOnlyList<string> KeptClean { get; }

        /// <summary>落地前的阶段。</summary>
        public string PreviousStage { get; }

        /// <summary>落地前的子状态。</summary>
        public string PreviousSubState { get; }

        /// <summary>过程中的中文文案。</summary>
        public IReadOnlyList<string> Findings { get; }
    }

    /// <summary>
    /// 重规划落地器：先快照、后改状态；有人改权威文件或有执行中工作项时一个字都不写。
    /// 吃 ReplanPlanner 算出的计划，把它真的落成：需求快照成新基准、脏项标脏、净项保留、回方案关。
    /// </summary>
    public static class ReplanLanding
    {
        /// <summary>工作项状态枚举值：标脏。</summary>
        private const string DirtyState = "标脏";

        /// <summary>工作项状态枚举值：挂起。</summary>
        private const string SuspendedState = "挂起";

        /// <summary>工作项状态枚举值：执行中。</summary>
        private const string RunningState = "执行中";

        /// <summary>
        /// 把一次重规划计划真落地。执行顺序写死：先三道拒绝闸（命中就一个字都不写盘），
        /// 再写证据（快照 + 变更影响文档），最后改状态（脏项标脏、净项一字不动、回方案关）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="plan">重规划计划，来自 ReplanPlanner。</param>
        /// <param name="graph">工作项依赖图，用于取标题与状态。</param>
        /// <param name="requirementJsonText">需求原文，逐字节原样进快照。</param>
        /// <param name="humanConfirmed">人改权威文件时人的确认标志。</param>
        public static ReplanLandingResult Apply(
            string repositoryRoot,
            string requirementIdentifier,
            ReplanResult plan,
            WorkItemGraph graph,
            string requirementJsonText,
            bool humanConfirmed)
        {
            // 第 ① 步 · 三道拒绝闸：任何一道命中就一个字都不写盘（含快照、影响文档、工作项、状态文件）。
            var refusal = RefuseIfAny(repositoryRoot, plan, graph, humanConfirmed);
            if (refusal != null)
            {
                return refusal;
            }

            var findings = new List<string>();
            if (plan.MustAskHuman && humanConfirmed)
            {
                // 确认过也要留痕：人看过、点头了，脏集内 N 个人改权威文件将被重跑覆盖。
                findings.Add($"人已确认：脏集内 {plan.AuthoritativeFilesInDirtySet.Count} 个人改权威文件将被重跑覆盖");
            }

            // 第 ② 步 · 先写证据（快照 + 变更影响文档）。
            // 为什么是「先证据、后状态」：快照与影响文档写成了而状态没改，是「再跑一次就好」；
            // 状态先跳到「停在关卡·方案」而快照没写，是回了方案关却没有新基准可审——
            // 人打开面板看到要审方案，却不知道审的是哪一版需求。与决策 63 同一条道理。
            var snapshot = RequirementSnapshotStore.Capture(repositoryRoot, requirementIdentifier, requirementJsonText);
            WriteChangeImpactDocument(repositoryRoot, requirementIdentifier, plan, graph, snapshot.Version);

            // 第 ③ 步 · 后改状态（脏项标脏、净项一字不动、回方案关）。
            var markedDirty = new List<string>();
            foreach (var workItemIdentifier in plan.PropagatedDirty)
            {
                var outcome = MarkDirtyIfNeeded(repositoryRoot, requirementIdentifier, workItemIdentifier, findings);
                if (outcome != MarkOutcome.UntouchedBecauseFileUnreadable)
                {
                    markedDirty.Add(workItemIdentifier);
                }
            }

            // 净项一个文件都不许打开写。「保留」的定义就是一个字都不改——给净项写一个「净」状态，
            // 等于在没有任何变化的东西上留下一次写入，下次人改检测（子文档 03 §三）会把它误判成人改。
            var keptClean = new List<string>(plan.Clean);

            string previousStage = "";
            string previousSubState = "";
            if (TaskState.TryLoad(repositoryRoot, requirementIdentifier, out var state, out var stateFailureReason))
            {
                previousStage = state.Stage;
                previousSubState = state.SubState;

                // 预算与产物哈希一字不动——重规划不清账。
                var resetState = new TaskState(
                    "方案",
                    "停在关卡",
                    "",
                    "方案",
                    state.Budget,
                    state.ArtifactHashes);
                TaskState.Save(repositoryRoot, requirementIdentifier, resetState);
            }
            else
            {
                // 状态.json 读不出来（文件不存在或坏）：不凭空造一份，快照与标脏照常算数，落地仍成立。
                findings.Add($"状态文件读不出来，阶段与关卡没改：{stateFailureReason}");
            }

            return new ReplanLandingResult(
                true,
                "",
                snapshot.Version,
                markedDirty,
                keptClean,
                previousStage,
                previousSubState,
                findings);
        }

        /// <summary>三道拒绝闸：命中返回对应的拒绝结果，全过返回 null。</summary>
        private static ReplanLandingResult RefuseIfAny(
            string repositoryRoot,
            ReplanResult plan,
            WorkItemGraph graph,
            bool humanConfirmed)
        {
            if (plan == null || plan.PropagatedDirty.Count == 0)
            {
                return Rejected("零脏项，不需要落地");
            }

            // 子文档 03 §四第 1 步是「当前原子步骤跑完即停」：还在跑就落地，
            // 会把正在写的产物和标脏撞在一起，所以任何执行中的工作项都拦下。
            var runningIdentifiers = FindRunningIdentifiers(graph);
            if (runningIdentifiers.Count > 0)
            {
                return Rejected($"工作项 {string.Join("、", runningIdentifiers)} 还在执行中；重规划要等当前原子步骤跑完才能落地");
            }

            if (plan.MustAskHuman && !humanConfirmed)
            {
                return Rejected(
                    $"脏集里有人改权威文件：{string.Join("、", plan.AuthoritativeFilesInDirtySet)}；要落地请人确认后重跑并带上确认标志");
            }

            return null;
        }

        /// <summary>造一份拒绝结果：没落地、没快照、没有标脏，原因是给的那句。</summary>
        private static ReplanLandingResult Rejected(string reason)
        {
            return new ReplanLandingResult(
                false,
                reason,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "",
                "",
                Array.Empty<string>());
        }

        /// <summary>状态是「执行中」的工作项 id（查全部工作项，不只脏集——当前原子步骤跑完即停是全局前提）。</summary>
        private static List<string> FindRunningIdentifiers(WorkItemGraph graph)
        {
            var running = new List<string>();
            if (graph == null)
            {
                return running;
            }

            foreach (var node in graph.Nodes)
            {
                if (string.Equals(node.State, RunningState, StringComparison.Ordinal))
                {
                    running.Add(node.Identifier);
                }
            }

            running.Sort(StringComparer.Ordinal);
            return running;
        }

        /// <summary>标脏一个工作项的结果：正常标了 / 已是标脏跳过 / 文件读不出来跳过。</summary>
        private enum MarkOutcome
        {
            /// <summary>已标脏（含原本挂起照常标脏）。</summary>
            Marked,

            /// <summary>状态本来就是标脏，跳过、不重写文件，仍算标脏项。</summary>
            AlreadyDirty,

            /// <summary>工作项文件读不出来，跳过且不计入标脏。</summary>
            UntouchedBecauseFileUnreadable
        }

        /// <summary>
        /// 把一个工作项标成「标脏」：读它的 JSON，只把「状态」这一个键改成「标脏」，
        /// 其余键与键序一字不动，整体写回。状态已是「标脏」的不重写文件；「挂起」的照常标脏。
        /// </summary>
        private static MarkOutcome MarkDirtyIfNeeded(
            string repositoryRoot,
            string requirementIdentifier,
            string workItemIdentifier,
            List<string> findings)
        {
            var workItemPath = WorkItemFilePath(repositoryRoot, requirementIdentifier, workItemIdentifier);

            JsonObject root;
            try
            {
                var parsed = JsonNode.Parse(File.ReadAllText(workItemPath));
                if (parsed is not JsonObject parsedObject)
                {
                    findings.Add($"{workItemIdentifier} 工作项文件顶层不是对象，跳过标脏：{workItemPath}");
                    return MarkOutcome.UntouchedBecauseFileUnreadable;
                }

                root = parsedObject;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                findings.Add($"{workItemIdentifier} 工作项文件读不出来，跳过标脏：{exception.Message}");
                return MarkOutcome.UntouchedBecauseFileUnreadable;
            }

            var currentState = root["状态"] is JsonValue stateValue && stateValue.GetValueKind() == JsonValueKind.String
                ? stateValue.GetValue<string>()
                : "";

            if (string.Equals(currentState, DirtyState, StringComparison.Ordinal))
            {
                // 已经是标脏：不重写文件，避免在没变化的东西上留下一次写入（人改检测会误判）。
                findings.Add($"{workItemIdentifier} 已经是标脏，跳过");
                return MarkOutcome.AlreadyDirty;
            }

            var wasSuspended = string.Equals(currentState, SuspendedState, StringComparison.Ordinal);
            root["状态"] = DirtyState;
            File.WriteAllText(workItemPath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));

            if (wasSuspended)
            {
                findings.Add($"{workItemIdentifier} 原本挂起，已标脏；driver 恢复后按脏项重跑");
            }

            return MarkOutcome.Marked;
        }

        /// <summary>工作项文件路径：_Tasks/&lt;需求id&gt;/20-work-items/&lt;id&gt;.json。</summary>
        private static string WorkItemFilePath(string repositoryRoot, string requirementIdentifier, string workItemIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "20-work-items", workItemIdentifier + ".json");
        }

        /// <summary>
        /// 写变更影响文档：_Tasks/&lt;需求id&gt;/05-change-impact.md，每次落地整份重写。
        /// 七个小节标题固定，没有内容就写「- 无」，不许省掉整节。
        /// </summary>
        private static void WriteChangeImpactDocument(
            string repositoryRoot,
            string requirementIdentifier,
            ReplanResult plan,
            WorkItemGraph graph,
            int snapshotVersion)
        {
            var builder = new StringBuilder();
            builder.Append($"# 变更影响 · {requirementIdentifier} · 基准 v{snapshotVersion}{Environment.NewLine}");
            builder.Append(Environment.NewLine);

            AppendSection(builder, "直接脏（字段 diff 直接命中）", plan.DirectlyDirty, graph);
            AppendSection(builder, "传播脏（依赖上游脏项）", PropagatedOnly(plan), graph);
            AppendSection(builder, "净项（原样保留，一个字未改）", plan.Clean, graph);
            AppendSection(builder, "要执行后端评估一轮的", plan.NeedsBackendEvaluation, graph);
            AppendSection(builder, "人改权威文件", plan.AuthoritativeFilesInDirtySet, graph);

            builder.Append($"## 过程发现{Environment.NewLine}");
            if (plan.Findings.Count == 0)
            {
                builder.Append($"- 无{Environment.NewLine}");
            }
            else
            {
                foreach (var finding in plan.Findings)
                {
                    builder.Append($"- {finding}{Environment.NewLine}");
                }
            }

            var filePath = PipelinePaths.ChangeImpactFile(repositoryRoot, requirementIdentifier);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
        }

        /// <summary>传播脏节只列「依赖上游脏项」传进来的：PropagatedDirty 减掉 DirectlyDirty。</summary>
        private static List<string> PropagatedOnly(ReplanResult plan)
        {
            var directSet = new HashSet<string>(plan.DirectlyDirty, StringComparer.Ordinal);
            return plan.PropagatedDirty.Where(identifier => !directSet.Contains(identifier)).ToList();
        }

        /// <summary>写一个小节：标题 + 逐条「- id 标题」，没有内容写「- 无」，末尾统一空行。</summary>
        private static void AppendSection(
            StringBuilder builder,
            string heading,
            IReadOnlyList<string> identifiers,
            WorkItemGraph graph)
        {
            builder.Append($"## {heading}{Environment.NewLine}");
            if (identifiers.Count == 0)
            {
                builder.Append($"- 无{Environment.NewLine}");
                builder.Append(Environment.NewLine);
                return;
            }

            foreach (var identifier in identifiers)
            {
                var title = TitleOf(graph, identifier);
                builder.Append(title.Length == 0 ? $"- {identifier}{Environment.NewLine}" : $"- {identifier} {title}{Environment.NewLine}");
            }

            builder.Append(Environment.NewLine);
        }

        /// <summary>工作项标题从图里取；取不到写空串（后面就只有 id）。</summary>
        private static string TitleOf(WorkItemGraph graph, string identifier)
        {
            if (graph == null)
            {
                return "";
            }

            foreach (var node in graph.Nodes)
            {
                if (string.Equals(node.Identifier, identifier, StringComparison.Ordinal))
                {
                    return node.Title;
                }
            }

            return "";
        }

        /// <summary>写盘选项：缩进 + 不转义中文；以 Default 为基类带上 TypeInfoResolver（.NET 10 下裸构造会抛）。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
