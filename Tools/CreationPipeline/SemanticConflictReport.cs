using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>语义冲突报告里的一条冲突候选：需求A / 需求B / 置信度（高|中|低）/ 判据 / 说明。</summary>
    public sealed class SemanticConflictCandidate
    {
        /// <summary>
        /// 构造一条冲突候选。
        /// </summary>
        /// <param name="requirementA">需求 A 的 id。</param>
        /// <param name="requirementB">需求 B 的 id。</param>
        /// <param name="confidence">置信度：高 / 中 / 低。</param>
        /// <param name="basis">判据，命中的是什么。</param>
        /// <param name="description">一句人话说明。</param>
        public SemanticConflictCandidate(string requirementA, string requirementB, string confidence, string basis, string description)
        {
            RequirementA = requirementA ?? "";
            RequirementB = requirementB ?? "";
            Confidence = confidence ?? "";
            Basis = basis ?? "";
            Description = description ?? "";
            // 决策 66：只有置信度「高」才建议发卡，中低只在需求上标注。
            SuggestRaiseCard = string.Equals(Confidence, "高", StringComparison.Ordinal);
        }

        /// <summary>需求 A 的 id。</summary>
        public string RequirementA { get; }

        /// <summary>需求 B 的 id。</summary>
        public string RequirementB { get; }

        /// <summary>置信度：高 / 中 / 低。</summary>
        public string Confidence { get; }

        /// <summary>判据，命中的是什么。</summary>
        public string Basis { get; }

        /// <summary>一句人话说明。</summary>
        public string Description { get; }

        /// <summary>是否建议发卡：只有「高」才 true（决策 66），中低只在需求上标注。</summary>
        public bool SuggestRaiseCard { get; }

        /// <summary>把一条候选拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"需求 {RequirementA} × 需求 {RequirementB}：置信度 {Confidence}，判据 {Basis}，{Description}";
        }
    }

    /// <summary>
    /// 一次语义冲突比对的报告：解析执行后端回复成冲突候选列表，落盘
    /// <c>_Tasks/语义冲突报告.json</c>。
    /// 解析失败绝不许当成「零发现」（决策 42）：Parsed=false + 原因写清，一条候选都不给。
    /// 报告是产物不是判定（决策 89）：置信度只是报告里的字段；本类一个字都不写进协作层账本——
    /// 不写 ConflictList、不调 Append（决策 66），一个算错的相似度往账本里塞垃圾，比不算更糟。
    /// </summary>
    public sealed class SemanticConflictReport
    {
        /// <summary>报告文件名：语义冲突报告.json。</summary>
        public const string ReportFileName = "语义冲突报告.json";

        /// <summary>置信度合法值：高 / 中 / 低。</summary>
        public static readonly string[] AllowedConfidences = { "高", "中", "低" };

        /// <summary>
        /// 构造一份报告。
        /// </summary>
        /// <param name="parsed">是否解析成功（判成了）。</param>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="candidates">冲突候选列表。</param>
        /// <param name="highCount">高置信度条数。</param>
        /// <param name="mediumCount">中置信度条数。</param>
        /// <param name="lowCount">低置信度条数。</param>
        /// <param name="fromCache">是否来自缓存。</param>
        /// <param name="parseReason">解析失败原因；成功时为空串。</param>
        /// <param name="timestamp">报告生成时间，ISO 8601。</param>
        public SemanticConflictReport(
            bool parsed,
            string model,
            string promptVersion,
            string decisionKey,
            IReadOnlyList<SemanticConflictCandidate> candidates,
            int highCount,
            int mediumCount,
            int lowCount,
            bool fromCache,
            string parseReason,
            string timestamp)
        {
            Parsed = parsed;
            Model = model ?? "";
            PromptVersion = promptVersion ?? "";
            DecisionKey = decisionKey ?? "";
            Candidates = candidates ?? Array.Empty<SemanticConflictCandidate>();
            HighCount = highCount;
            MediumCount = mediumCount;
            LowCount = lowCount;
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
        /// 同一份输入换个模型、换版提示词，就是另一次判定，本来就该是另一个键（决策 20、42 那一族）。
        /// </summary>
        public string DecisionKey { get; }

        /// <summary>冲突候选列表。</summary>
        public IReadOnlyList<SemanticConflictCandidate> Candidates { get; }

        /// <summary>高置信度条数。</summary>
        public int HighCount { get; }

        /// <summary>中置信度条数。</summary>
        public int MediumCount { get; }

        /// <summary>低置信度条数。</summary>
        public int LowCount { get; }

        /// <summary>是否来自缓存（命中缓存时报告标这个，表示不是本轮真判的）。</summary>
        public bool FromCache { get; }

        /// <summary>解析失败原因；判成了时为空串。</summary>
        public string ParseReason { get; }

        /// <summary>报告生成时间，ISO 8601。</summary>
        public string Timestamp { get; }

        /// <summary>
        /// 从模型回复文本里解析报告。模型很爱在 JSON 外面裹 ```json 代码块，这里要容忍：
        /// 取第一个「{」到最后一个「}」之间的内容解析。解析失败（不是合法 JSON、缺「冲突候选」、
        /// 置信度值非法等）→ Parsed=false、零候选、原因写清——绝不许当成「没问题」（决策 42）。
        /// </summary>
        /// <param name="modelText">模型回复文本。</param>
        /// <param name="report">解析成功时的报告；失败时是 NotParsed 报告。</param>
        /// <param name="reason">解析失败原因；成功时为空串。</param>
        public static bool TryParse(string modelText, out SemanticConflictReport report, out string reason)
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

                if (!root.TryGetProperty("冲突候选", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
                {
                    reason = "模型回复缺「冲突候选」数组";
                    report = NotParsed("", "", "", reason);
                    return false;
                }

                var candidates = new List<SemanticConflictCandidate>();
                var highCount = 0;
                var mediumCount = 0;
                var lowCount = 0;
                var index = 0;
                foreach (var item in candidatesElement.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        reason = $"冲突候选第 {index} 条不是 JSON 对象";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "需求A", out var requirementA) || requirementA.Length == 0)
                    {
                        reason = $"冲突候选第 {index} 条缺「需求A」或它是空的";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "需求B", out var requirementB) || requirementB.Length == 0)
                    {
                        reason = $"冲突候选第 {index} 条缺「需求B」或它是空的";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "置信度", out var confidence))
                    {
                        reason = $"冲突候选第 {index} 条缺「置信度」或它不是字符串";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (confidence != "高" && confidence != "中" && confidence != "低")
                    {
                        reason = $"冲突候选第 {index} 条的「置信度」是「{confidence}」，只认 高/中/低";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "判据", out var basis) || basis.Length == 0)
                    {
                        reason = $"冲突候选第 {index} 条缺「判据」或它是空的";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    var description = ReadOptionalString(item, "说明");
                    candidates.Add(new SemanticConflictCandidate(requirementA, requirementB, confidence, basis, description));
                    if (confidence == "高")
                    {
                        highCount++;
                    }
                    else if (confidence == "中")
                    {
                        mediumCount++;
                    }
                    else
                    {
                        lowCount++;
                    }
                }

                report = new SemanticConflictReport(
                    parsed: true,
                    model: "",
                    promptVersion: "",
                    decisionKey: "",
                    candidates: candidates,
                    highCount: highCount,
                    mediumCount: mediumCount,
                    lowCount: lowCount,
                    fromCache: false,
                    parseReason: "",
                    timestamp: "");
                reason = "";
                return true;
            }
        }

        /// <summary>构造一份「没判成」的报告：零候选、判成了=false、原因写清（决策 42）。</summary>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="reason">为什么没判成。</param>
        public static SemanticConflictReport NotParsed(string model, string promptVersion, string decisionKey, string reason)
        {
            return new SemanticConflictReport(
                parsed: false,
                model: model,
                promptVersion: promptVersion,
                decisionKey: decisionKey,
                candidates: Array.Empty<SemanticConflictCandidate>(),
                highCount: 0,
                mediumCount: 0,
                lowCount: 0,
                fromCache: false,
                parseReason: reason ?? "",
                timestamp: "");
        }

        /// <summary>报告落盘路径：_Tasks/语义冲突报告.json（语义冲突不挂到某个需求名下，落 _Tasks 顶层）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ReportFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "_Tasks", ReportFileName);
        }

        /// <summary>把报告写成 JSON 文件（缩进、中文原样），返回落盘路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public string WriteReport(string repositoryRoot)
        {
            var filePath = ReportFile(repositoryRoot);
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
        public SemanticConflictReport AsStamped(string timestamp, bool fromCache)
        {
            return new SemanticConflictReport(Parsed, Model, PromptVersion, DecisionKey, Candidates, HighCount, MediumCount, LowCount, fromCache, ParseReason, timestamp);
        }

        /// <summary>序列化成报告 JSON 文本（缩进）。字段至少含：模型/提示词版本/判定键/判成了/冲突候选/高数/中数/低数。</summary>
        public string ToJson()
        {
            var reportObject = BuildNode();
            reportObject["时间戳"] = Timestamp;
            return reportObject.ToJsonString(WriteOptions);
        }

        /// <summary>从报告 JSON 文本解析报告；缺字段或类型不对返回 null（缓存文件坏掉就是没命中）。</summary>
        /// <param name="json">报告 JSON 文本。</param>
        public static SemanticConflictReport TryFromJson(string json)
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
                || !TryReadInt(obj, "高数", out var highCount)
                || !TryReadInt(obj, "中数", out var mediumCount)
                || !TryReadInt(obj, "低数", out var lowCount))
            {
                return null;
            }

            var candidates = new List<SemanticConflictCandidate>();
            if (obj.TryGetPropertyValue("冲突候选", out var candidatesNode) && candidatesNode is JsonArray candidatesArray)
            {
                foreach (var item in candidatesArray)
                {
                    if (item is not JsonObject candidateObject)
                    {
                        return null;
                    }

                    if (!TryReadString(candidateObject, "需求A", out var requirementA)
                        || !TryReadString(candidateObject, "需求B", out var requirementB)
                        || !TryReadString(candidateObject, "置信度", out var confidence)
                        || !TryReadString(candidateObject, "判据", out var basis)
                        || !TryReadString(candidateObject, "说明", out var description))
                    {
                        return null;
                    }

                    candidates.Add(new SemanticConflictCandidate(requirementA, requirementB, confidence, basis, description));
                }
            }

            var fromCache = false;
            if (obj.TryGetPropertyValue("来自缓存", out var cacheNode) && cacheNode is JsonValue cacheValue && cacheValue.TryGetValue<bool>(out var cacheFlag))
            {
                fromCache = cacheFlag;
            }

            var parseReason = ReadStringOrEmpty(obj, "解析原因");
            var timestamp = ReadStringOrEmpty(obj, "时间戳");
            return new SemanticConflictReport(parsed, model, promptVersion, decisionKey, candidates, highCount, mediumCount, lowCount, fromCache, parseReason, timestamp);
        }

        /// <summary>构造报告 JSON 对象（不含时间戳，时间戳由调用点决定）。</summary>
        private JsonObject BuildNode()
        {
            var candidatesArray = new JsonArray();
            foreach (var candidate in Candidates)
            {
                candidatesArray.Add(new JsonObject
                {
                    ["需求A"] = candidate.RequirementA,
                    ["需求B"] = candidate.RequirementB,
                    ["置信度"] = candidate.Confidence,
                    ["判据"] = candidate.Basis,
                    ["说明"] = candidate.Description,
                    ["建议发卡"] = candidate.SuggestRaiseCard
                });
            }

            return new JsonObject
            {
                ["模型"] = Model,
                ["提示词版本"] = PromptVersion,
                ["判定键"] = DecisionKey,
                ["判成了"] = Parsed,
                ["冲突候选"] = candidatesArray,
                ["高数"] = HighCount,
                ["中数"] = MediumCount,
                ["低数"] = LowCount,
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

        /// <summary>读字符串键；缺失或类型不对给空串（候选里的 说明 宽松处理）。</summary>
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
