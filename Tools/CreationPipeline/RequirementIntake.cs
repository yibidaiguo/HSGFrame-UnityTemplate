using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>需求入站主流程：扫收件箱、判幂等、锁定分流、合成候选需求、校验后入池或拒收。</summary>
    public static class RequirementIntake
    {
        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>工程字段发现的参考示例固定指向需求基线 schema。</summary>
        private const string ReferenceSchemaPath = "Pools/Schema/基线/需求.schema.json";

        /// <summary>取号前缀与位数：REQ- 后跟四位编号。</summary>
        private const string RequirementPrefix = "REQ-";

        private const int IdentifierDigits = 4;

        /// <summary>
        /// 跑一轮入站：按 Inbox 扫描顺序逐条处理信封，返回每条的处理结果。
        /// 整轮开始时建一次「来源 → 需求」索引，成功入池/更新后同步更新，保证同轮同源记录不重复取号。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，_Tasks/_Generated 从这里展开。</param>
        /// <param name="poolRoot">池子根目录，Inbox/Requirements 从这里展开。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        /// <param name="moment">本轮入站时刻，写入同步与拒收单。</param>
        public static IReadOnlyList<IntakeOutcome> Run(string repositoryRoot, string poolRoot, PoolSchema schema, DateTimeOffset moment)
        {
            var outcomes = new List<IntakeOutcome>();
            var index = BuildIndex(poolRoot);

            IReadOnlyList<InboxScanEntry> entries;
            try
            {
                entries = InboxScanner.Scan(PoolPaths.InboxDirectory(poolRoot));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new[]
                {
                    new IntakeOutcome(IntakeDecision.Unreadable, PoolPaths.InboxDirectory(poolRoot), "", $"扫描收件箱失败：{exception.Message}")
                };
            }

            foreach (var entry in entries)
            {
                outcomes.Add(ProcessEntry(repositoryRoot, poolRoot, schema, moment, index, entry));
            }

            return outcomes;
        }

        /// <summary>处理单条扫描结果，把文件读写异常转成 Unreadable，不让异常穿出 Run。</summary>
        private static IntakeOutcome ProcessEntry(
            string repositoryRoot,
            string poolRoot,
            PoolSchema schema,
            DateTimeOffset moment,
            Dictionary<(string, string), IndexEntry> index,
            InboxScanEntry entry)
        {
            try
            {
                return ProcessEntryCore(repositoryRoot, poolRoot, schema, moment, index, entry);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new IntakeOutcome(IntakeDecision.Unreadable, entry.FilePath, "", $"处理信封失败：{exception.Message}");
            }
        }

        /// <summary>单条信封的实际处理：幂等、锁定分流、合成候选、校验与判定。</summary>
        private static IntakeOutcome ProcessEntryCore(
            string repositoryRoot,
            string poolRoot,
            PoolSchema schema,
            DateTimeOffset moment,
            Dictionary<(string, string), IndexEntry> index,
            InboxScanEntry entry)
        {
            if (entry.Envelope == null)
            {
                return new IntakeOutcome(IntakeDecision.Unreadable, entry.FilePath, "", $"信封无法解析：{entry.FailureReason}");
            }

            var envelope = entry.Envelope;
            var key = (envelope.Channel, envelope.RecordIdentifier);
            index.TryGetValue(key, out var existing);

            // 第 2 步 · 幂等：修订不新于已入池修订，跳过。
            if (existing != null && envelope.Revision <= existing.PooledRevision)
            {
                return new IntakeOutcome(
                    IntakeDecision.Skipped,
                    entry.FilePath,
                    existing.RequirementIdentifier,
                    $"记录 {envelope.RecordIdentifier} 修订 {envelope.Revision} 已入池（当前 {existing.PooledRevision}），跳过");
            }

            // 第 3 步 · 已锁定需求的下游改动：不入池，落变更请求。
            if (existing != null && envelope.Revision > existing.PooledRevision && existing.IsLocked)
            {
                var diff = ComputeFieldDiff(existing.FilePath, envelope);
                if (diff.Count == 0)
                {
                    return new IntakeOutcome(IntakeDecision.Skipped, entry.FilePath, existing.RequirementIdentifier, "已锁定但字段无变化");
                }

                ChangeRequestJournal.Record(repositoryRoot, existing.RequirementIdentifier, envelope, diff, moment);
                return new IntakeOutcome(IntakeDecision.Diverted, entry.FilePath, existing.RequirementIdentifier, "已锁定，改动已转为变更请求");
            }

            // 第 4 步 · 合成候选需求：先分离工程字段并记发现，再拼内容字段与工程字段。
            var findings = new List<PoolFinding>();
            var contentFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            // 归工程的字段名单只算一次，且与助手草稿那条路共用同一份实现
            // （RequirementFieldOwnership）——同一条规矩抄两遍迟早分叉。
            var engineOwnedFields = new HashSet<string>(
                RequirementFieldOwnership.FieldsOwnedBy(schema, RequirementFieldOwnership.EngineOwner),
                StringComparer.Ordinal);
            foreach (var pair in envelope.Fields)
            {
                if (engineOwnedFields.Contains(pair.Key))
                {
                    findings.Add(new PoolFinding(
                        envelope.SourceFilePath,
                        $"字段「{pair.Key}」归工程所有，下游不许写入",
                        "从下游记录里删掉该字段",
                        ReferenceSchemaPath));
                    continue;
                }

                contentFields[pair.Key] = pair.Value;
            }

            var isNew = existing == null;
            string requirementIdentifier;
            JsonObject candidate;
            if (isNew)
            {
                requirementIdentifier = IdentifierAllocator.Next(PoolPaths.RequirementsDirectory(poolRoot), RequirementPrefix, IdentifierDigits);
                candidate = new JsonObject();
                foreach (var pair in contentFields)
                {
                    candidate[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
                }

                candidate["状态"] = ResolveInitialState(schema);
                candidate["锁定"] = false;
                candidate["关联设计记录"] = new JsonArray();
                candidate["依赖"] = new JsonArray();
                candidate["父需求"] = null;
                candidate["冲突"] = new JsonArray();
                candidate["schema版本"] = schema.SchemaVersion;
            }
            else
            {
                requirementIdentifier = existing.RequirementIdentifier;
                var draft = JsonNode.Parse(File.ReadAllText(existing.FilePath)) as JsonObject;
                if (draft == null)
                {
                    return new IntakeOutcome(IntakeDecision.Unreadable, entry.FilePath, "", $"既有需求 {requirementIdentifier} 不是 JSON 对象，无法更新");
                }

                candidate = draft;
                foreach (var pair in contentFields)
                {
                    candidate[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
                }
            }

            candidate["id"] = requirementIdentifier;
            candidate["来源"] = BuildSourceObject(envelope);
            candidate["同步"] = BuildSyncObject(envelope, moment);

            // 第 5 步 · 校验与判定：候选写临时目录校验，通过才入池。
            var tempRoot = Path.Combine(Path.GetTempPath(), "创作管线校验-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var tempFilePath = Path.Combine(tempRoot, requirementIdentifier + ".json");
                File.WriteAllText(tempFilePath, candidate.ToJsonString(WriteOptions), new UTF8Encoding(false));

                // 校验器报的位置是那份临时候选文件，但拒收单要回贴给策划看——
                // 指到一个用完即删的临时路径对他毫无意义，改指回下游记录本身。
                foreach (var finding in RequirementValidator.CheckFile(tempFilePath, schema))
                {
                    findings.Add(new PoolFinding(
                        entry.FilePath,
                        finding.Reason,
                        finding.FixAction,
                        finding.ReferenceExamplePath));
                }

                if (findings.Count > 0)
                {
                    var noticePath = RejectionNotice.Write(repositoryRoot, envelope, findings, moment);
                    return new IntakeOutcome(
                        IntakeDecision.Rejected,
                        entry.FilePath,
                        "",
                        $"记录 {envelope.RecordIdentifier} 拒收，问题 {findings.Count} 条，拒收单：{noticePath}",
                        findings);
                }

                var requirementsDirectory = PoolPaths.RequirementsDirectory(poolRoot);
                Directory.CreateDirectory(requirementsDirectory);
                var targetPath = Path.Combine(requirementsDirectory, requirementIdentifier + ".json");
                File.WriteAllText(targetPath, candidate.ToJsonString(WriteOptions), new UTF8Encoding(false));

                index[key] = new IndexEntry
                {
                    RequirementIdentifier = requirementIdentifier,
                    PooledRevision = envelope.Revision,
                    IsLocked = existing != null && existing.IsLocked,
                    FilePath = targetPath
                };

                var decision = isNew ? IntakeDecision.Accepted : IntakeDecision.Updated;
                var message = isNew
                    ? $"记录 {envelope.RecordIdentifier} 入池为 {requirementIdentifier}"
                    : $"{requirementIdentifier} 已按修订 {envelope.Revision} 更新";
                return new IntakeOutcome(decision, entry.FilePath, requirementIdentifier, message);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        /// <summary>建「(渠道, 记录id) → 已入池需求」索引；读不出来源的需求文件跳过不报错。</summary>
        private static Dictionary<(string, string), IndexEntry> BuildIndex(string poolRoot)
        {
            var index = new Dictionary<(string, string), IndexEntry>();
            var requirementsDirectory = PoolPaths.RequirementsDirectory(poolRoot);
            if (!Directory.Exists(requirementsDirectory))
            {
                return index;
            }

            foreach (var filePath in Directory.EnumerateFiles(requirementsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(File.ReadAllText(filePath));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("来源", out var source) || source.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!source.TryGetProperty("渠道", out var channelElement) || channelElement.ValueKind != JsonValueKind.String
                        || !source.TryGetProperty("记录id", out var recordElement) || recordElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var channel = channelElement.GetString() ?? "";
                    var recordIdentifier = recordElement.GetString() ?? "";
                    var revision = source.TryGetProperty("修订", out var revisionElement)
                        && revisionElement.ValueKind == JsonValueKind.Number
                        && revisionElement.TryGetInt32(out var parsedRevision)
                        ? parsedRevision
                        : 0;
                    var isLocked = root.TryGetProperty("锁定", out var lockElement) && lockElement.ValueKind == JsonValueKind.True;
                    var requirementIdentifier = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString() ?? Path.GetFileNameWithoutExtension(filePath)
                        : Path.GetFileNameWithoutExtension(filePath);

                    index[(channel, recordIdentifier)] = new IndexEntry
                    {
                        RequirementIdentifier = requirementIdentifier,
                        PooledRevision = revision,
                        IsLocked = isLocked,
                        FilePath = filePath
                    };
                }
            }

            return index;
        }

        /// <summary>对已入池需求文件与信封字段算字段级 diff：字段名 → 「旧值 → 新值」描述。</summary>
        private static Dictionary<string, string> ComputeFieldDiff(string requirementFilePath, InboxEnvelope envelope)
        {
            var diff = new Dictionary<string, string>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(requirementFilePath));
            var root = document.RootElement;

            foreach (var pair in envelope.Fields)
            {
                var oldText = root.TryGetProperty(pair.Key, out var existingElement) ? existingElement.GetRawText() : "（无）";
                var newText = pair.Value.GetRawText();
                if (string.Equals(oldText, newText, StringComparison.Ordinal))
                {
                    continue;
                }

                diff[pair.Key] = $"「{TruncateForDisplay(oldText)}」→「{TruncateForDisplay(newText)}」";
            }

            return diff;
        }

        /// <summary>超 60 字符截前 60 加省略号。</summary>
        private static string TruncateForDisplay(string text)
        {
            return text.Length <= 60 ? text : text.Substring(0, 60) + "…";
        }

        /// <summary>初始状态：状态机给不出就用「草稿」。</summary>
        private static string ResolveInitialState(PoolSchema schema)
        {
            return schema.StateMachine != null && !string.IsNullOrEmpty(schema.StateMachine.InitialState)
                ? schema.StateMachine.InitialState
                : "草稿";
        }

        /// <summary>拼「来源」对象，全部取自信封。</summary>
        private static JsonObject BuildSourceObject(InboxEnvelope envelope)
        {
            return new JsonObject
            {
                ["渠道"] = envelope.Channel,
                ["记录id"] = envelope.RecordIdentifier,
                ["修订"] = envelope.Revision,
                ["提交人"] = envelope.Submitter,
                ["提交时间"] = envelope.SubmitTime
            };
        }

        /// <summary>拼「同步」对象：hash 对信封字段按键序数序排序后取紧凑文本的 SHA256 小写十六进制。</summary>
        private static JsonObject BuildSyncObject(InboxEnvelope envelope, DateTimeOffset moment)
        {
            return new JsonObject
            {
                ["hash"] = ComputeFieldHash(envelope),
                ["时间"] = moment.ToString("o")
            };
        }

        /// <summary>
        /// 计算信封字段的内容 hash：按键的序数序排序后拼成紧凑 JSON 文本，取 SHA256 转小写十六进制。
        /// 测试可用同一算法对同一信封算得稳定值。
        /// </summary>
        private static string ComputeFieldHash(InboxEnvelope envelope)
        {
            var ordered = new JsonObject();
            foreach (var key in envelope.Fields.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                ordered[key] = JsonNode.Parse(envelope.Fields[key].GetRawText());
            }

            var compactJson = ordered.ToJsonString();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(compactJson));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>递归删除临时目录，清理失败不影响结果。</summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        /// <summary>索引条目：需求 id、已入池修订、是否锁定与需求文件路径。</summary>
        private sealed class IndexEntry
        {
            /// <summary>需求 id，如「REQ-0001」。</summary>
            public string RequirementIdentifier { get; set; }

            /// <summary>已入池的修订号。</summary>
            public int PooledRevision { get; set; }

            /// <summary>该需求是否已锁定。</summary>
            public bool IsLocked { get; set; }

            /// <summary>需求文件路径。</summary>
            public string FilePath { get; set; }
        }
    }
}
