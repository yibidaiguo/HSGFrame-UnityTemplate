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
        /// <param name="draftReady">草稿已经立得住、校验也过了，可以摆成确认卡等人点。</param>
        /// <param name="requirementIdentifier">草稿的需求 id；没草稿时为空串。</param>
        /// <param name="draft">补全后的需求草稿；没草稿时为 null。</param>
        /// <param name="findings">校验发现；空表示校验通过。</param>
        /// <param name="blockedFields">模型越权填的字段名（工程侧字段），已被挡掉。</param>
        /// <param name="card">这一轮要发的卡片；每张卡至少带一个「开新话题」按钮。</param>
        /// <param name="imageRequestIdentifier">出图请求的留底 id；不是要图那一支时为空串。</param>
        /// <param name="imageRequest">出图请求；不是要图那一支时为 null。</param>
        public AssistantTurnOutcome(
            string replyText,
            bool draftReady,
            string requirementIdentifier,
            JsonObject draft,
            IReadOnlyList<PoolFinding> findings,
            IReadOnlyList<string> blockedFields,
            AssistantCard card = null,
            string imageRequestIdentifier = "",
            JsonObject imageRequest = null)
        {
            ImageRequestIdentifier = imageRequestIdentifier ?? "";
            ImageRequest = imageRequest;
            ReplyText = replyText ?? "";
            DraftReady = draftReady;
            RequirementIdentifier = requirementIdentifier ?? "";
            Draft = draft;
            Findings = findings ?? Array.Empty<PoolFinding>();
            BlockedFields = blockedFields ?? Array.Empty<string>();
            Card = card;
        }

        /// <summary>最终要回给人的话。</summary>
        public string ReplyText { get; }

        /// <summary>
        /// 草稿已经立得住、校验也过了，可以摆成确认卡等人点。
        /// **它不再等于「可以写下游」**：写不写由人点按钮决定（见 <see cref="AssistantCard.CreateAction"/>）。
        /// </summary>
        public bool DraftReady { get; }

        /// <summary>草稿的需求 id；没草稿时为空串。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>补全后的需求草稿；没草稿时为 null。</summary>
        public JsonObject Draft { get; }

        /// <summary>这一轮要发的卡片；每张卡至少带一个「开新话题」按钮。</summary>
        public AssistantCard Card { get; }

        /// <summary>出图请求的留底 id；不是要图那一支时为空串。</summary>
        public string ImageRequestIdentifier { get; }

        /// <summary>出图请求（资产类型 / 命名 / 描述 / 变体数）；不是要图那一支时为 null。</summary>
        public JsonObject ImageRequest { get; }

        /// <summary>这一轮产出的是不是一份等人点的出图请求。</summary>
        public bool ImageRequestReady
        {
            get { return ImageRequestIdentifier.Length > 0 && ImageRequest != null; }
        }

        /// <summary>校验发现；空表示校验通过。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>模型越权填的字段名（工程侧字段），已被挡掉。</summary>
        public IReadOnlyList<string> BlockedFields { get; }
    }

    /// <summary>
    /// 助手 B 形态一轮的处置逻辑：拿模型回答 + 会话消息，算出「回什么话、摆成什么卡」。
    ///
    /// 四条硬规矩：
    /// 1. **工程侧字段由引擎补，模型填了也不算数**——模型没有分配 id、决定状态的权力
    ///    （<see cref="RequirementFieldOwnership"/>）。挡掉的字段要报出来，不许静默丢。
    /// 2. **校验不过就不进确认卡**，并把校验发现翻成人话回给提需求的人（子文档 02 §五：
    ///    现场跑 req.validate）。校验错误文案与 pool.pull 拒收共用同一份，不会两张皮。
    /// 3. **校验过了也不自己写表**：整理成一张卡，写不写等人点按钮。
    ///    助手替人整理规则，人只做一个决定——这是这条链路的形状，不是可选的礼貌。
    /// 4. **写下游不是这里干的**。这里只算，真写是命令层的事——这样这一整套逻辑
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

        /// <summary>
        /// 写台账一行的选项：**不缩进**。台账是 jsonl，一行一条，缩进过的 JSON 会把一条拆成十几行，
        /// 读的时候按行解析全部失败——而写的时候一点报错都没有。
        /// </summary>
        private static readonly JsonSerializerOptions LedgerWriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
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
            var fromPool = IdentifierAllocator.NextByDirectoryName(PoolPaths.RequirementsDirectory(poolRoot), "REQ-", 4);
            var fromDrafts = IdentifierAllocator.Next(DraftDirectory(repositoryRoot), "REQ-", 4);
            return NumberOf(fromDrafts) > NumberOf(fromPool) ? fromDrafts : fromPool;
        }

        /// <summary>
        /// 给一份出图请求发个号。与需求号分开一套（IMG-）：它们是两种东西，
        /// 混用一套号会让「REQ-0007」这个说法忽而指需求忽而指一张图。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string AllocateImageRequestIdentifier(string repositoryRoot)
        {
            return IdentifierAllocator.Next(DraftDirectory(repositoryRoot), "IMG-", 4);
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
                var failureText = "我这边没能读懂执行后端的回答，这一轮什么都没建。原因：" + reason;
                return new AssistantTurnOutcome(
                    failureText,
                    false,
                    "",
                    null,
                    Array.Empty<PoolFinding>(),
                    Array.Empty<string>(),
                    AssistantCard.ForConversation(failureText, Array.Empty<string>()));
            }

            // 要图那一支**不走需求**：人要的是一张图片文件，不是一条需求。
            // 把它整理成出图请求，等人点「出图」再真去下游生——生图花钱，先给人看一眼画什么。
            if (reply.WantsImage)
            {
                var imageIdentifier = AllocateImageRequestIdentifier(repositoryRoot);
                var imageCard = AssistantCard.ForImageRequest(
                    imageIdentifier,
                    reply.ImageRequest,
                    reply.ReplyText,
                    reply.MissingItems);
                return new AssistantTurnOutcome(
                    imageCard.ToPlainText(),
                    false,
                    "",
                    null,
                    Array.Empty<PoolFinding>(),
                    Array.Empty<string>(),
                    imageCard,
                    imageIdentifier,
                    reply.ImageRequest);
            }

            if (!reply.WantsRequirement || reply.Draft == null)
            {
                // 想问的点进卡片的「待确认」，**不再拼成一串「还缺这些：·字段名」跟在回话后面**。
                // 那种写法把 schema 摆到了人脸上，人看到的是一张表，不是一次对话。
                var card = AssistantCard.ForConversation(reply.ReplyText, reply.MissingItems);
                return new AssistantTurnOutcome(
                    card.ToPlainText(),
                    false,
                    "",
                    null,
                    Array.Empty<PoolFinding>(),
                    Array.Empty<string>(),
                    card);
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
                builder.Append("\n\n这条还立不住，我先没往下走：\n");
                foreach (var finding in findings)
                {
                    builder.Append("· ").Append(finding.Reason).Append("　修复：").Append(finding.FixAction).Append('\n');
                }

                var blockedCard = AssistantCard.ForConversation(builder.ToString().TrimEnd(), reply.MissingItems);
                return new AssistantTurnOutcome(
                    blockedCard.ToPlainText(),
                    false,
                    identifier,
                    draft,
                    findings,
                    filtered.BlockedFields,
                    blockedCard);
            }

            // 校验过了也只是「整理好了」。要不要真建，是人点按钮的事——
            // 回话里绝不许说「已经建了」，说了就等于替人做了决定。
            builder.Append("\n\n我按上面这些整理成了一条需求草稿，你看一眼；对就点「一键建需求」，我来写进需求表并拉进需求池。");
            var readyCard = AssistantCard.ForDraft(identifier, draft, schema, builder.ToString(), reply.MissingItems);
            return new AssistantTurnOutcome(
                readyCard.ToPlainText(),
                true,
                identifier,
                draft,
                findings,
                filtered.BlockedFields,
                readyCard);
        }

        /// <summary>
        /// 校验一份草稿：写临时目录再跑 <see cref="RequirementValidator"/>，跑完删。
        /// 校验器报的位置是临时路径，对提需求的人没意义——所以调用方只用 Reason 与 FixAction。
        /// </summary>
        /// <param name="draft">补全后的草稿。</param>
        /// <param name="identifier">需求 id，决定临时目录名（校验器按**所在目录名**核 id）。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        public static IReadOnlyList<PoolFinding> Validate(JsonObject draft, string identifier, PoolSchema schema)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "助手会话校验-" + Guid.NewGuid().ToString("N"));
            try
            {
                // 临时候选要摆成与池子里一样的形状（<id>/requirement.json）：
                // 校验器判的是所在目录名，摆成平铺文件的话每条草稿都会白报一条「id 与目录名不一致」，
                // 于是助手永远判「校验没过」、永远不写下游——而单测里那份草稿明明是合法的。
                var tempRequirementDirectory = Path.Combine(tempRoot, identifier);
                Directory.CreateDirectory(tempRequirementDirectory);
                var tempFilePath = Path.Combine(tempRequirementDirectory, PoolPaths.RequirementFileName);
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

        /// <summary>
        /// 读回一份待确认的草稿：人点「一键建需求」时，要建的就是当初摆在卡上的那一份。
        /// **不许拿按钮携带的内容重建草稿**——那是从客户端回来的数据，改得动。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="draft">读到的草稿；失败时为 null。</param>
        /// <param name="reason">失败原因，人能看懂。</param>
        public static bool TryLoadDraft(string repositoryRoot, string identifier, out JsonObject draft, out string reason)
        {
            draft = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(identifier))
            {
                reason = "按钮没带需求 id，不知道该建哪一条";
                return false;
            }

            var filePath = Path.Combine(DraftDirectory(repositoryRoot), identifier + ".json");
            if (!File.Exists(filePath))
            {
                reason = "找不到草稿留底（" + identifier + "）——它可能是上一次重装前留下的卡，重新说一遍需求我再整理一次";
                return false;
            }

            try
            {
                draft = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException || exception is UnauthorizedAccessException)
            {
                reason = "草稿留底读不动：" + exception.Message;
                return false;
            }

            if (draft == null)
            {
                reason = "草稿留底不是一个 JSON 对象：" + filePath;
                return false;
            }

            return true;
        }

        /// <summary>已确认台账：&lt;仓库根&gt;/_Tasks/conversations/confirmed.jsonl，一行一条，只追加。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ConfirmedLedgerPath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations", "confirmed.jsonl");
        }

        /// <summary>
        /// 这条草稿是不是已经建过了。卡片会一直挂在聊天记录里，人隔天再点一次是常事——
        /// 没有这道判断，同一条需求就会被建第二遍（幂等键相同虽不至于多出一条，
        /// 但会把下游那条已经推进的记录按草稿覆盖回去）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">需求 id。</param>
        public static bool IsConfirmed(string repositoryRoot, string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var filePath = ConfirmedLedgerPath(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (line.Trim().Length == 0)
                    {
                        continue;
                    }

                    JsonNode node;
                    try
                    {
                        node = JsonNode.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (node is JsonObject record
                        && record.TryGetPropertyValue("需求id", out var value)
                        && value is JsonValue jsonValue
                        && jsonValue.TryGetValue<string>(out var text)
                        && string.Equals(text, identifier, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 台账读不动时判「没建过」：重复建一次的代价，小于该建的没建。
                return false;
            }

            return false;
        }

        /// <summary>记一条「这条真建出去了」。写失败返回 false，调用方要如实说，不许当成建过了。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="conversationIdentifier">是哪条会话点的。</param>
        /// <param name="operatorIdentifier">谁点的。</param>
        /// <param name="now">当前时间。</param>
        public static bool RecordConfirmed(
            string repositoryRoot,
            string identifier,
            string conversationIdentifier,
            string operatorIdentifier,
            DateTimeOffset now)
        {
            var record = new JsonObject
            {
                ["时间"] = now.ToString("o"),
                ["需求id"] = identifier ?? "",
                ["会话"] = conversationIdentifier ?? "",
                ["操作人"] = operatorIdentifier ?? ""
            };

            try
            {
                Directory.CreateDirectory(Path.Combine(repositoryRoot, "_Tasks", "conversations"));
                File.AppendAllText(
                    ConfirmedLedgerPath(repositoryRoot),
                    record.ToJsonString(LedgerWriteOptions) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 点完「一键建需求」之后，这条需求到底落到哪一步，翻成一句给人的话。
        ///
        /// **一句都不许含糊**：写进下游表与拉进池子是两件事，前者成了后者没成时，
        /// 人要知道「表里那条在，补跑一次入站就行」，而不是以为白干了或者以为全好了。
        ///
        /// 末尾那句关于「排活」的话是**如实交代**：池子里躺着一条草稿，离引擎真接手还差
        /// 「确认 → 入队」，而那一段现在没有命令能做（`queue.json` 至今没有写入方）。
        /// 早先这里写的是「已经交给引擎排活」——那是句吹牛，删掉了。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="decision">入站决策；这一轮没在入站结果里看到它时给 null。</param>
        /// <param name="failureReason">入池整步失败的原因；成功时给空串。</param>
        public static string DescribeLanding(string identifier, IntakeDecision? decision, string failureReason)
        {
            var name = string.IsNullOrWhiteSpace(identifier) ? "这条" : identifier;
            const string NextStep = "\n\n它现在是「草稿」。再往后（确认 → 排活）还得人来，引擎不会自己接。";

            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                return name + " 已经写进需求表了，但拉进需求池这一步没成：" + failureReason
                    + "\n\n表里那条不会丢，补跑一次 pool.pull 就能入池。";
            }

            if (decision == null)
            {
                return name + " 已经写进需求表了，但这一轮入站里没看见它——下游可能还没同步好。"
                    + "\n\n过一会儿补跑一次 pool.pull 就行，表里那条不会丢。";
            }

            switch (decision.Value)
            {
                case IntakeDecision.Accepted:
                    return "建好了：" + name + "，已经写进需求表，也拉进了需求池。" + NextStep;
                case IntakeDecision.Updated:
                    return name + " 已经写进需求表，池子里那条也按新内容更新了。" + NextStep;
                case IntakeDecision.Skipped:
                    return name + " 已经写进需求表；池子里那条内容没变，这次没动它。" + NextStep;
                case IntakeDecision.Rejected:
                    return name + " 写进需求表了，但入池被拒收——校验没过。"
                        + "\n\n拒收单在 Pools/Inbox 旁边，改完内容再点一次。";
                case IntakeDecision.Diverted:
                    return name + " 写进需求表了；池子里那条已经锁定，所以这次落成了一条变更请求，等重规划处理。";
                default:
                    return name + " 写进需求表了，但那份入站信封读不了，没能入池。"
                        + "\n\n看一眼 Pools/Inbox 里那个文件，补跑 pool.pull。";
            }
        }

        /// <summary>
        /// 这句话是不是在说「开新话题」。**按钮之外还留一条文字入口**：
        /// 卡片按钮要走回调，回调链路没通、或人在手机上把卡片折叠了，就点不着——
        /// 那时人只会打字。留这条口子的成本是几个词，收益是这个功能不依赖单一通道。
        /// </summary>
        /// <param name="text">用户这句话。</param>
        public static bool LooksLikeNewTopic(string text)
        {
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.Length > 12)
            {
                return false;
            }

            foreach (var phrase in NewTopicPhrases)
            {
                if (string.Equals(trimmed, phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>能当「开新话题」用的几句话。只认整句相等，免得把「新话题我想说说背包」也吃掉。</summary>
        private static readonly string[] NewTopicPhrases =
        {
            "开新话题", "新话题", "重新开始", "重来", "清空上下文", "换个话题", "reset", "/new", "/reset"
        };

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
