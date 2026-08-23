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
    /// <summary>
    /// 进度回流账：**下游拥有的那几格**在仓库里的副本，落 <c>_Tasks/sync/progress-inbound.json</c>。
    ///
    /// 为什么单独一份而不是读基线：两份内容会有重叠，但意思完全不同。
    /// 基线是同步的记账（「上次两侧商定成什么」），回流账是**产物**
    /// （「人在飞书里填的执行人与进展，现在仓库里也有一份」）。
    /// 合成一份的话，`git diff` 上「人把进展改成了已完成」这件事会混在一堆
    /// 引擎自己的阶段变动里，看不出是谁动的——而这条链存在的全部理由就是让人看见它。
    ///
    /// 工程侧拥有的字段一格都不进这份账：那些值在仓库里本来就有源头
    /// （池子、_Tasks/&lt;需求id&gt;/state.json、门禁报告），再存一份就是两个事实源。
    /// </summary>
    public static class ProgressInboundLedger
    {
        /// <summary>写盘选项：缩进、中文原样。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>回流账文件路径：_Tasks/sync/progress-inbound.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string LedgerFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "_Tasks", "sync", "progress-inbound.json");
        }

        /// <summary>读回流账；文件不在或读不动都给一份空快照（这不是错——第一次同步之前它本来就不存在）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static ProgressSnapshot Load(string repositoryRoot)
        {
            var filePath = LedgerFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new ProgressSnapshot(Array.Empty<ProgressEntry>(), null);
            }

            try
            {
                return ProgressSnapshot.FromJson(JsonNode.Parse(File.ReadAllText(filePath)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new ProgressSnapshot(Array.Empty<ProgressEntry>(), null);
            }
        }

        /// <summary>
        /// 按一轮同步的结果写回流账：只写权威侧是策划端的那几格，值取裁定后的值。
        /// 冲突那几格取**下游当前值**并不算数——冲突未决时下游那一格没被认领，
        /// 所以那一格沿用上一轮账里的值，账上不出现未裁决的东西。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="plan">这一轮的计划。</param>
        /// <param name="schema">权威侧表。</param>
        /// <param name="moment">写账时间，ISO-8601。</param>
        public static ProgressSnapshot Save(string repositoryRoot, ProgressSyncPlan plan, ProgressSyncSchema schema, string moment)
        {
            var previous = Load(repositoryRoot);
            var plannerFields = new HashSet<string>(
                (schema?.PlannerFields() ?? Array.Empty<ProgressSyncField>()).Select(field => field.Name),
                StringComparer.Ordinal);

            var byIdentifier = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var decision in plan?.Decisions ?? Array.Empty<ProgressSyncDecision>())
            {
                if (!plannerFields.Contains(decision.FieldName))
                {
                    continue;
                }

                if (!byIdentifier.TryGetValue(decision.Identifier, out var fields))
                {
                    fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    byIdentifier[decision.Identifier] = fields;
                }

                fields[decision.FieldName] = decision.Direction == ProgressSyncDirection.Conflict
                    ? (previous.Find(decision.Identifier)?.Value(decision.FieldName) ?? "")
                    : decision.DownstreamValue;
            }

            var entries = byIdentifier
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ProgressEntry(pair.Key, pair.Value))
                .ToList();

            var global = new Dictionary<string, string>(StringComparer.Ordinal) { ["回流时间"] = moment ?? "" };
            var snapshot = new ProgressSnapshot(entries, global);

            var filePath = LedgerFile(repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, snapshot.ToJson().ToJsonString(WriteOptions), new UTF8Encoding(false));
            return snapshot;
        }
    }
}
