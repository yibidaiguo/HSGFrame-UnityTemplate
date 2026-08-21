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
    /// <summary>影响评估报告里的一条工作项判定：工作项 / 结论（脏|净）/ 理由。</summary>
    public sealed class ImpactAssessVerdict
    {
        /// <summary>
        /// 构造一条工作项判定。
        /// </summary>
        /// <param name="workItem">工作项名，与输入列表里的写法一致。</param>
        /// <param name="conclusion">结论：脏 / 净。</param>
        /// <param name="reason">理由，为什么受影响或不受影响。</param>
        public ImpactAssessVerdict(string workItem, string conclusion, string reason)
        {
            WorkItem = workItem ?? "";
            Conclusion = conclusion ?? "";
            Reason = reason ?? "";
        }

        /// <summary>工作项名，与输入列表里的写法一致。</summary>
        public string WorkItem { get; }

        /// <summary>结论：脏 / 净。</summary>
        public string Conclusion { get; }

        /// <summary>理由，为什么受影响或不受影响。</summary>
        public string Reason { get; }

        /// <summary>把一条判定拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"工作项：{WorkItem}；结论：{Conclusion}；理由：{Reason}";
        }
    }

    /// <summary>
    /// 一次影响评估的报告：解析执行后端回复成工作项判定列表，落盘
    /// <c>_Tasks/&lt;需求id&gt;/影响评估.json</c>。
    /// 解析失败绝不许当成「零发现」（决策 42）：Parsed=false + 原因写清，一条结论都不给。
    /// 模型漏答的项进「漏判的工作项」列表，**绝不默认成「净」**——默认成净等于悄悄放过
    /// 一个可能受影响的工作项（决策 42 最贵的一种长相）。
    /// 报告是产物不是判定（决策 89）：脏/净只是报告里的字段，不进门禁、不改状态。
    /// </summary>
    public sealed class ImpactAssessReport
    {
        /// <summary>报告文件名：影响评估.json。</summary>
        public const string ReportFileName = "影响评估.json";

        /// <summary>结论合法值：脏 / 净。</summary>
        public static readonly string[] AllowedConclusions = { "脏", "净" };

        /// <summary>
        /// 构造一份报告。
        /// </summary>
        /// <param name="parsed">是否解析成功（判成了）。</param>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="verdicts">工作项判定列表。</param>
        /// <param name="missingWorkItems">模型漏答的工作项列表。</param>
        /// <param name="dirtyCount">判成「脏」的条数。</param>
        /// <param name="cleanCount">判成「净」的条数。</param>
        /// <param name="fromCache">是否来自缓存。</param>
        /// <param name="parseReason">解析失败原因；成功时为空串。</param>
        /// <param name="timestamp">报告生成时间，ISO 8601。</param>
        public ImpactAssessReport(
            bool parsed,
            string model,
            string promptVersion,
            string decisionKey,
            IReadOnlyList<ImpactAssessVerdict> verdicts,
            IReadOnlyList<string> missingWorkItems,
            int dirtyCount,
            int cleanCount,
            bool fromCache,
            string parseReason,
            string timestamp)
        {
            Parsed = parsed;
            Model = model ?? "";
            PromptVersion = promptVersion ?? "";
            DecisionKey = decisionKey ?? "";
            Verdicts = verdicts ?? Array.Empty<ImpactAssessVerdict>();
            MissingWorkItems = missingWorkItems ?? Array.Empty<string>();
            DirtyCount = dirtyCount;
            CleanCount = cleanCount;
            FromCache = fromCache;
            ParseReason = parseReason ?? "";
            Timestamp = timestamp ?? "";
        }

        /// <summary>是否判成了（模型回复解析成功）。</summary>
        public bool Parsed { get; }

        /// <summary>服务端回传的模型名（决策 89：报告里必须写清用的哪个模型）。</summary>
        public string Model { get; }

        /// <summary>提示词版本（决策 89：报告里必须写清哪一版提示词）。</summary>
        public string PromptVersion { get; }

        /// <summary>
        /// 判定键：<c>SHA256(提示词全文 + 模型名 + 提示词版本)</c>，与缓存键是同一个值。
        /// 同一份 diff 换个模型、换版提示词，就是另一次判定，本来就该是另一个键（决策 20、42 那一族）。
        /// </summary>
        public string DecisionKey { get; }

        /// <summary>工作项判定列表。</summary>
        public IReadOnlyList<ImpactAssessVerdict> Verdicts { get; }

        /// <summary>模型漏答的工作项列表（输入列表里有、模型回复里没有的）。不默认成净（决策 42）。</summary>
        public IReadOnlyList<string> MissingWorkItems { get; }

        /// <summary>判成「脏」的条数。</summary>
        public int DirtyCount { get; }

        /// <summary>判成「净」的条数。</summary>
        public int CleanCount { get; }

        /// <summary>是否来自缓存（命中缓存时报告标这个，表示不是本轮真判的）。</summary>
        public bool FromCache { get; }

        /// <summary>解析失败原因；判成了时为空串。</summary>
        public string ParseReason { get; }

        /// <summary>报告生成时间，ISO 8601。</summary>
        public string Timestamp { get; }

        /// <summary>
        /// 从模型回复文本里解析报告。模型很爱在 JSON 外面裹 ```json 代码块，这里要容忍：
        /// 取第一个「{」到最后一个「}」之间的内容解析。解析失败（不是合法 JSON、缺「评估」、
        /// 结论值非法等）→ Parsed=false、零结论、原因写清——绝不许当成「没问题」（决策 42）。
        /// 解析成功后做漏判检查：输入列表里有、模型没答的工作项进 <see cref="MissingWorkItems"/>，
        /// 绝不默认成「净」。
        /// </summary>
        /// <param name="modelText">模型回复文本。</param>
        /// <param name="requestedWorkItems">这次要求模型判的工作项列表（漏判检查的依据）。</param>
        /// <param name="report">解析成功时的报告；失败时是 NotParsed 报告。</param>
        /// <param name="reason">解析失败原因；成功时为空串。</param>
        public static bool TryParse(string modelText, IReadOnlyList<string> requestedWorkItems, out ImpactAssessReport report, out string reason)
        {
            if (!TryExtractJsonObject(modelText, out var json, out reason))
            {
                report = NotParsed("", "", "", reason);
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                reason = "模型回复里的 JSON 不是合法 JSON：" + exception.Message;
                report = NotParsed("", "", "", reason);
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "模型回复的顶层必须是 JSON 对象";
                    report = NotParsed("", "", "", reason);
                    return false;
                }

                if (!root.TryGetProperty("评估", out var assessmentsElement) || assessmentsElement.ValueKind != JsonValueKind.Array)
                {
                    reason = "模型回复缺「评估」数组";
                    report = NotParsed("", "", "", reason);
                    return false;
                }

                var verdicts = new List<ImpactAssessVerdict>();
                var dirtyCount = 0;
                var cleanCount = 0;
                var index = 0;
                foreach (var item in assessmentsElement.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        reason = $"评估第 {index} 条不是 JSON 对象";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "工作项", out var workItem) || workItem.Length == 0)
                    {
                        reason = $"评估第 {index} 条缺「工作项」或它是空的";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "结论", out var conclusion))
                    {
                        reason = $"评估第 {index} 条缺「结论」或它不是字符串";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (conclusion != "脏" && conclusion != "净")
                    {
                        reason = $"评估第 {index} 条的「结论」是「{conclusion}」，只认 脏/净";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    var verdictReason = ReadOptionalString(item, "理由");
                    verdicts.Add(new ImpactAssessVerdict(workItem, conclusion, verdictReason));
                    if (conclusion == "脏")
                    {
                        dirtyCount++;
                    }
                    else
                    {
                        cleanCount++;
                    }
                }

                // 漏判检查：输入列表里有、模型没答的项，进漏判列表，绝不默认成「净」（决策 42）。
                var answered = new HashSet<string>(StringComparer.Ordinal);
                foreach (var verdict in verdicts)
                {
                    answered.Add(verdict.WorkItem);
                }

                var missing = new List<string>();
                if (requestedWorkItems != null)
                {
                    foreach (var workItem in requestedWorkItems)
                    {
                        if (workItem != null && !answered.Contains(workItem))
                        {
                            missing.Add(workItem);
                        }
                    }
                }

                report = new ImpactAssessReport(
                    parsed: true,
                    model: "",
                    promptVersion: "",
                    decisionKey: "",
                    verdicts: verdicts,
                    missingWorkItems: missing,
                    dirtyCount: dirtyCount,
                    cleanCount: cleanCount,
                    fromCache: false,
                    parseReason: "",
                    timestamp: "");
                reason = "";
                return true;
            }
        }

        /// <summary>构造一份「没判成」的报告：零结论、判成了=false、原因写清（决策 42）。</summary>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="reason">为什么没判成。</param>
        public static ImpactAssessReport NotParsed(string model, string promptVersion, string decisionKey, string reason)
        {
            return new ImpactAssessReport(
                parsed: false,
                model: model,
                promptVersion: promptVersion,
                decisionKey: decisionKey,
                verdicts: Array.Empty<ImpactAssessVerdict>(),
                missingWorkItems: Array.Empty<string>(),
                dirtyCount: 0,
                cleanCount: 0,
                fromCache: false,
                parseReason: reason ?? "",
                timestamp: "");
        }

        /// <summary>报告落盘路径：_Tasks/&lt;需求id&gt;/影响评估.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string ReportFile(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(PipelinePaths.TaskDirectory(repositoryRoot, requirementIdentifier), ReportFileName);
        }

        /// <summary>把报告写成 JSON 文件（缩进、中文原样），返回落盘路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public string WriteReport(string repositoryRoot, string requirementIdentifier)
        {
            var filePath = ReportFile(repositoryRoot, requirementIdentifier);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, ToJson(), new UTF8Encoding(false));
            return filePath;
        }

        /// <summary>构造一份同一内容、但更新了时间戳与缓存标记的报告（落盘前用：缓存命中重标时间，新判的标当前时刻）。</summary>
        /// <param name="timestamp">新的时间戳，ISO 8601。</param>
        /// <param name="fromCache">是否来自缓存。</param>
        public ImpactAssessReport AsStamped(string timestamp, bool fromCache)
        {
            return new ImpactAssessReport(Parsed, Model, PromptVersion, DecisionKey, Verdicts, MissingWorkItems, DirtyCount, CleanCount, fromCache, ParseReason, timestamp);
        }

        /// <summary>序列化成报告 JSON 文本（缩进）。字段至少含：模型/提示词版本/判定键/判成了/评估/漏判的工作项/脏数/净数。</summary>
        public string ToJson()
        {
            var reportObject = BuildNode();
            reportObject["时间戳"] = Timestamp;
            return reportObject.ToJsonString(WriteOptions);
        }

        /// <summary>从报告 JSON 文本解析报告；缺字段或类型不对返回 null（缓存文件坏掉就是没命中）。</summary>
        /// <param name="json">报告 JSON 文本。</param>
        public static ImpactAssessReport TryFromJson(string json)
        {
            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            if (node is not JsonObject obj)
            {
                return null;
            }

            if (!TryReadBool(obj, "判成了", out var parsed)
                || !TryReadString(obj, "模型", out var model)
                || !TryReadString(obj, "提示词版本", out var promptVersion)
                || !TryReadString(obj, "判定键", out var decisionKey)
                || !TryReadInt(obj, "脏数", out var dirtyCount)
                || !TryReadInt(obj, "净数", out var cleanCount))
            {
                return null;
            }

            var verdicts = new List<ImpactAssessVerdict>();
            if (obj.TryGetPropertyValue("评估", out var verdictsNode) && verdictsNode is JsonArray verdictsArray)
            {
                foreach (var item in verdictsArray)
                {
                    if (item is not JsonObject verdictObject)
                    {
                        return null;
                    }

                    if (!TryReadString(verdictObject, "工作项", out var workItem)
                        || !TryReadString(verdictObject, "结论", out var conclusion)
                        || !TryReadString(verdictObject, "理由", out var reason))
                    {
                        return null;
                    }

                    verdicts.Add(new ImpactAssessVerdict(workItem, conclusion, reason));
                }
            }

            var missingWorkItems = new List<string>();
            if (obj.TryGetPropertyValue("漏判的工作项", out var missingNode) && missingNode is JsonArray missingArray)
            {
                foreach (var item in missingArray)
                {
                    if (item is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.String)
                    {
                        return null;
                    }

                    missingWorkItems.Add(jsonValue.GetValue<string>() ?? "");
                }
            }

            var fromCache = false;
            if (obj.TryGetPropertyValue("来自缓存", out var cacheNode) && cacheNode is JsonValue cacheValue && cacheValue.TryGetValue<bool>(out var cacheFlag))
            {
                fromCache = cacheFlag;
            }

            var parseReason = ReadStringOrEmpty(obj, "解析原因");
            var timestamp = ReadStringOrEmpty(obj, "时间戳");
            return new ImpactAssessReport(parsed, model, promptVersion, decisionKey, verdicts, missingWorkItems, dirtyCount, cleanCount, fromCache, parseReason, timestamp);
        }

        /// <summary>构造报告 JSON 对象（不含时间戳，时间戳由调用点决定）。</summary>
        private JsonObject BuildNode()
        {
            var verdictsArray = new JsonArray();
            foreach (var verdict in Verdicts)
            {
                verdictsArray.Add(new JsonObject
                {
                    ["工作项"] = verdict.WorkItem,
                    ["结论"] = verdict.Conclusion,
                    ["理由"] = verdict.Reason
                });
            }

            var missingArray = new JsonArray();
            foreach (var workItem in MissingWorkItems)
            {
                missingArray.Add(workItem);
            }

            return new JsonObject
            {
                ["模型"] = Model,
                ["提示词版本"] = PromptVersion,
                ["判定键"] = DecisionKey,
                ["判成了"] = Parsed,
                ["评估"] = verdictsArray,
                ["漏判的工作项"] = missingArray,
                ["脏数"] = DirtyCount,
                ["净数"] = CleanCount,
                ["来自缓存"] = FromCache,
                ["解析原因"] = ParseReason
            };
        }

        /// <summary>从模型回复文本里抠出最外层 JSON 对象（容忍 ```json 代码块与前后废话）。</summary>
        private static bool TryExtractJsonObject(string text, out string json, out string reason)
        {
            json = "";
            reason = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "模型回复为空";
                return false;
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < start)
            {
                reason = "模型回复里没有 JSON 对象";
                return false;
            }

            json = text.Substring(start, end - start + 1);
            return true;
        }

        /// <summary>读必须为字符串的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadRequiredString(JsonElement obj, string key, out string value)
        {
            value = "";
            if (!obj.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>读字符串键；缺失或类型不对给空串（评估里的 理由 宽松处理）。</summary>
        private static string ReadOptionalString(JsonElement obj, string key)
        {
            return TryReadRequiredString(obj, key, out var value) ? value : "";
        }

        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = "";
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            value = jsonValue.GetValue<string>() ?? "";
            return true;
        }

        private static bool TryReadBool(JsonObject obj, string key, out bool value)
        {
            value = false;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue || (jsonValue.GetValueKind() != JsonValueKind.True && jsonValue.GetValueKind() != JsonValueKind.False))
            {
                return false;
            }

            value = jsonValue.GetValue<bool>();
            return true;
        }

        private static bool TryReadInt(JsonObject obj, string key, out int value)
        {
            value = 0;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.Number)
            {
                return false;
            }

            try
            {
                value = jsonValue.GetValue<int>();
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
                return false;
            }
        }

        private static string ReadStringOrEmpty(JsonObject obj, string key)
        {
            return TryReadString(obj, key, out var value) ? value : "";
        }

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonObject 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
