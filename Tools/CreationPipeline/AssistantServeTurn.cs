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
    /// <summary>助手一轮会话的处置结果：回什么话、要不要写下游、写什么。</summary>
    public sealed class AssistantTurnOutcome
    {
        /// <summary>
        /// 构造一轮处置结果。
        /// </summary>
        /// <param name="replyText">最终要回给人的话。</param>
        /// <param name="shouldWriteDownstream">校验过了、可以写下游草稿。</param>
        /// <param name="requirementIdentifier">草稿的需求 id；不写下游时为空串。</param>
        /// <param name="draft">补全后的需求草稿；不写下游时为 null。</param>
        /// <param name="findings">校验发现；空表示校验通过。</param>
        /// <param name="blockedFields">模型越权填的字段名（工程侧字段），已被挡掉。</param>
        public AssistantTurnOutcome(
            string replyText,
            bool shouldWriteDownstream,
            string requirementIdentifier,
            JsonObject draft,
            IReadOnlyList<PoolFinding> findings,
            IReadOnlyList<string> blockedFields)
        {
            ReplyText = replyText ?? "";
            ShouldWriteDownstream = shouldWriteDownstream;
            RequirementIdentifier = requirementIdentifier ?? "";
            Draft = draft;
            Findings = findings ?? Array.Empty<PoolFinding>();
            BlockedFields = blockedFields ?? Array.Empty<string>();
        }

        /// <summary>最终要回给人的话。</summary>
        public string ReplyText { get; }

        /// <summary>校验过了、可以写下游草稿。</summary>
        public bool ShouldWriteDownstream { get; }

        /// <summary>草稿的需求 id；不写下游时为空串。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>补全后的需求草稿；不写下游时为 null。</summary>
        public JsonObject Draft { get; }

        /// <summary>校验发现；空表示校验通过。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>模型越权填的字段名（工程侧字段），已被挡掉。</summary>
        public IReadOnlyList<string> BlockedFields { get; }
    }

    /// <summary>
    /// 助手 B 形态一轮的处置逻辑：拿模型回答 + 会话消息，算出「回什么话、要不要写下游」。
    ///
    /// 三条硬规矩：
    /// 1. **工程侧字段由引擎补，模型填了也不算数**——模型没有分配 id、决定状态的权力
    ///    （<see cref="RequirementFieldOwnership"/>）。挡掉的字段要报出来，不许静默丢。
    /// 2. **校验不过就不写下游**，并把校验发现翻成人话回给提需求的人（子文档 02 §五：
    ///    现场跑 req.validate）。校验错误文案与 pool.pull 拒收共用同一份，不会两张皮。
    /// 3. **写下游不是这里干的**。这里只算，真写是命令层的事——这样这一整套逻辑
    ///    脱离网络可测。
    /// </summary>
    public static class AssistantServeTurn
    {
        /// <summary>写 JSON 的选项：本机是 .NET 10 preview SDK，必须从 Default 复制着构造。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>助手发出去的草稿留底目录：&lt;仓库根&gt;/_Tasks/conversations/drafts。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string DraftDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations", "drafts");
        }

        /// <summary>
        /// 分配一个需求 id：池子里现存的与助手已发过的**取两边最大值再加一**。
        /// 只看池子会撞号——助手发出去的草稿写的是下游的表，不落池子，
        /// 只看池子的话第二条草稿会拿到和第一条一样的号，按幂等键一写就把前一条覆盖了。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static string AllocateIdentifier(string repositoryRoot, string poolRoot)
        {
            var fromPool = IdentifierAllocator.Next(PoolPaths.RequirementsDirectory(poolRoot), "REQ-", 4);
            var fromDrafts = IdentifierAllocator.Next(DraftDirectory(repositoryRoot), "REQ-", 4);
            return NumberOf(fromDrafts) > NumberOf(fromPool) ? fromDrafts : fromPool;
        }

        /// <summary>
        /// 处置一轮：补全草稿 → 校验 → 决定回话。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="message">这一轮的会话消息。</param>
        /// <param name="reply">执行后端的回答（已解析）。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        /// <param name="now">当前时间，由调用方给（要可复现，决策 58）。</param>
        public static AssistantTurnOutcome Decide(
            string repositoryRoot,
            string poolRoot,
            AssistantConversationMessage message,
            AssistantServeReply reply,
            PoolSchema schema,
            DateTimeOffset now)
        {
            if (reply == null || !reply.Parsed)
            {
                var reason = reply == null ? "执行后端没有回答" : reply.ParseFailureReason;
                return new AssistantTurnOutcome(
                    "我这边没能读懂执行后端的回答，这一轮什么都没建。原因：" + reason,
                    false,
                    "",
                    null,
                    Array.Empty<PoolFinding>(),
                    Array.Empty<string>());
            }

            if (!reply.WantsRequirement || reply.Draft == null)
            {
                var text = reply.ReplyText;
                if (reply.MissingItems.Count > 0)
                {
                    text += "\n\n还缺这些：\n" + string.Join("\n", reply.MissingItems.Select(item => "· " + item));
                }

                return new AssistantTurnOutcome(text, false, "", null, Array.Empty<PoolFinding>(), Array.Empty<string>());
            }

            // 第 1 步 · 所有权闸门：模型只该填策划端字段，工程字段填了也不算数。
            var plannerFields = new List<string>(RequirementFieldOwnership.FieldsOwnedBy(schema, RequirementFieldOwnership.PlannerOwner));
            foreach (var pair in schema.RequiredByType)
            {
                plannerFields.AddRange(pair.Value);
            }

            var filtered = RequirementFieldOwnership.KeepOnly(reply.Draft, plannerFields);

            // 第 2 步 · 引擎补齐自己拥有的那几个字段。
            var identifier = AllocateIdentifier(repositoryRoot, poolRoot);
            var draft = filtered.Kept;
            draft["id"] = identifier;
            draft["状态"] = schema.StateMachine?.InitialState ?? "草稿";
            draft["锁定"] = false;
            draft["schema版本"] = schema.SchemaVersion ?? "";
            draft["关联设计记录"] = new JsonArray();
            draft["依赖"] = new JsonArray();
            draft["来源"] = new JsonObject
            {
                ["渠道"] = "助手会话",
                ["记录id"] = message?.MessageIdentifier ?? "",
                ["提交人"] = message?.SenderIdentifier ?? "",
                ["提交时间"] = now.ToString("o")
            };

            // 第 3 步 · 现场跑校验：写临时目录，用与 pool.pull 同一个校验器同一份文案。
            var findings = Validate(draft, identifier, schema);

            var builder = new StringBuilder();
            builder.Append(reply.ReplyText);
            if (filtered.BlockedFields.Count > 0)
            {
                builder.Append("\n\n（引擎注：这几个字段归工程侧，你填的不算数，已由引擎补：");
                builder.Append(string.Join("、", filtered.BlockedFields));
                builder.Append("。）");
            }

            if (findings.Count > 0)
            {
                builder.Append("\n\n这条**没有**写进需求表，因为校验没过：\n");
                foreach (var finding in findings)
                {
                    builder.Append("· ").Append(finding.Reason).Append("　修复：").Append(finding.FixAction).Append('\n');
                }

                return new AssistantTurnOutcome(builder.ToString().TrimEnd(), false, identifier, draft, findings, filtered.BlockedFields);
            }

            builder.Append("\n\n已按 ").Append(identifier).Append(" 建了一条草稿，校验通过。");
            return new AssistantTurnOutcome(builder.ToString(), true, identifier, draft, findings, filtered.BlockedFields);
        }

        /// <summary>
        /// 校验一份草稿：写临时目录再跑 <see cref="RequirementValidator"/>，跑完删。
        /// 校验器报的位置是临时路径，对提需求的人没意义——所以调用方只用 Reason 与 FixAction。
        /// </summary>
        /// <param name="draft">补全后的草稿。</param>
        /// <param name="identifier">需求 id，决定临时文件名（校验器按文件名核 id）。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        public static IReadOnlyList<PoolFinding> Validate(JsonObject draft, string identifier, PoolSchema schema)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "助手会话校验-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempRoot);
                var tempFilePath = Path.Combine(tempRoot, identifier + ".json");
                File.WriteAllText(tempFilePath, draft.ToJsonString(WriteOptions), new UTF8Encoding(false));
                return RequirementValidator.CheckFile(tempFilePath, schema);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new[]
                {
                    new PoolFinding(identifier, "校验没跑成：" + exception.Message, "看引擎所在机器的临时目录权限", "Pools/Schema/Baseline/requirement.schema.json")
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        /// <summary>
        /// 把发出去的草稿留底：&lt;仓库根&gt;/_Tasks/conversations/drafts/&lt;id&gt;.json。
        /// 这份留底同时是发号台账——<see cref="AllocateIdentifier"/> 靠它避开撞号。
        /// 写失败返回空串、不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="draft">补全后的草稿。</param>
        public static string SaveDraft(string repositoryRoot, string identifier, JsonObject draft)
        {
            try
            {
                var directory = DraftDirectory(repositoryRoot);
                Directory.CreateDirectory(directory);
                var filePath = Path.Combine(directory, identifier + ".json");
                File.WriteAllText(filePath, draft.ToJsonString(WriteOptions), new UTF8Encoding(false));
                return filePath;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "";
            }
        }

        /// <summary>把 REQ-0007 这样的编号取成数字；取不到给 0。</summary>
        private static int NumberOf(string identifier)
        {
            var text = (identifier ?? "").Trim();
            var index = text.LastIndexOf('-');
            if (index < 0 || index + 1 >= text.Length)
            {
                return 0;
            }

            return int.TryParse(text.Substring(index + 1), out var number) ? number : 0;
        }

        /// <summary>删临时目录；删不掉就放着，不影响结果。</summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }
    }
}
