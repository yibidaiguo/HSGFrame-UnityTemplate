using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>预审报告里的一条发现：分级 / 文件 / 位置 / 问题 / 依据。</summary>
    public sealed class PreReviewFinding
    {
        /// <summary>
        /// 构造一条发现。
        /// </summary>
        /// <param name="grade">分级：阻断级 / 建议级。</param>
        /// <param name="file">涉及文件，diff 里的仓库相对路径。</param>
        /// <param name="location">位置，行号或函数名。</param>
        /// <param name="issue">问题描述。</param>
        /// <param name="basis">依据，为什么算问题。</param>
        public PreReviewFinding(string grade, string file, string location, string issue, string basis)
        {
            Grade = grade ?? "";
            File = file ?? "";
            Location = location ?? "";
            Issue = issue ?? "";
            Basis = basis ?? "";
        }

        /// <summary>分级：阻断级 / 建议级。</summary>
        public string Grade { get; }

        /// <summary>涉及文件，diff 里的仓库相对路径。</summary>
        public string File { get; }

        /// <summary>位置，行号或函数名。</summary>
        public string Location { get; }

        /// <summary>问题描述。</summary>
        public string Issue { get; }

        /// <summary>依据，为什么算问题。</summary>
        public string Basis { get; }

        /// <summary>把一条发现拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"分级：{Grade}；文件：{File}；位置：{Location}；问题：{Issue}；依据：{Basis}";
        }
    }

    /// <summary>
    /// 一次 AI 对抗预审的报告：解析模型回复成发现列表，落盘 <c>_Tasks/&lt;需求id&gt;/预审报告.json</c>。
    /// 解析失败绝不许当成「零发现」（决策 42）：Parsed=false + 原因写清，一个发现都不给。
    /// 报告是产物不是判定（决策 89）：发现分级只是报告里的字段，不进门禁、不改状态。
    /// </summary>
    public sealed class PreReviewReport
    {
        /// <summary>报告文件名：预审报告.json。</summary>
        public const string ReportFileName = "预审报告.json";

        /// <summary>分级合法值：阻断级 / 建议级。</summary>
        public static readonly string[] AllowedGrades = { "阻断级", "建议级" };

        /// <summary>
        /// 构造一份报告。
        /// </summary>
        /// <param name="parsed">是否解析成功（判成了）。</param>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="findings">发现列表。</param>
        /// <param name="blockingCount">阻断级条数。</param>
        /// <param name="suggestionCount">建议级条数。</param>
        /// <param name="fromCache">是否来自缓存。</param>
        /// <param name="parseReason">解析失败原因；成功时为空串。</param>
        /// <param name="timestamp">报告生成时间，ISO 8601。</param>
        public PreReviewReport(
            bool parsed,
            string model,
            string promptVersion,
            string decisionKey,
            IReadOnlyList<PreReviewFinding> findings,
            int blockingCount,
            int suggestionCount,
            bool fromCache,
            string parseReason,
            string timestamp)
        {
            Parsed = parsed;
            Model = model ?? "";
            PromptVersion = promptVersion ?? "";
            DecisionKey = decisionKey ?? "";
            Findings = findings ?? Array.Empty<PreReviewFinding>();
            BlockingCount = blockingCount;
            SuggestionCount = suggestionCount;
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
        /// **它不是「输入的哈希」**——原来这个字段叫「输入哈希」，装的却是这个三元组的哈希，
        /// 标签在说谎（决策 20、42 那一族）。名字改成判定键之后它才名副其实：
        /// 同一份 diff 换个模型、换版提示词，就是另一次判定，本来就该是另一个键。
        /// </summary>
        public string DecisionKey { get; }

        /// <summary>发现列表。</summary>
        public IReadOnlyList<PreReviewFinding> Findings { get; }

        /// <summary>阻断级条数。</summary>
        public int BlockingCount { get; }

        /// <summary>建议级条数。</summary>
        public int SuggestionCount { get; }

        /// <summary>是否来自缓存（命中缓存时报告标这个，表示不是本轮真判的）。</summary>
        public bool FromCache { get; }

        /// <summary>解析失败原因；判成了时为空串。</summary>
        public string ParseReason { get; }

        /// <summary>报告生成时间，ISO 8601。</summary>
        public string Timestamp { get; }

        /// <summary>
        /// 从模型回复文本里解析报告。模型很爱在 JSON 外面裹 ```json 代码块，这里要容忍：
        /// 取第一个「{」到最后一个「}」之间的内容解析。解析失败（不是合法 JSON、缺「发现」、
        /// 分级值非法等）→ Parsed=false、零发现、原因写清——绝不许当成「没问题」（决策 42）。
        /// </summary>
        /// <param name="modelText">模型回复文本。</param>
        /// <param name="report">解析成功时的报告；失败时是 NotParsed 报告。</param>
        /// <param name="reason">解析失败原因；成功时为空串。</param>
        public static bool TryParse(string modelText, out PreReviewReport report, out string reason)
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

                if (!root.TryGetProperty("发现", out var findingsElement) || findingsElement.ValueKind != JsonValueKind.Array)
                {
                    reason = "模型回复缺「发现」数组";
                    report = NotParsed("", "", "", reason);
                    return false;
                }

                var findings = new List<PreReviewFinding>();
                var blockingCount = 0;
                var suggestionCount = 0;
                var index = 0;
                foreach (var item in findingsElement.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        reason = $"发现第 {index} 条不是 JSON 对象";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "分级", out var grade))
                    {
                        reason = $"发现第 {index} 条缺「分级」或它不是字符串";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (grade != "阻断级" && grade != "建议级")
                    {
                        reason = $"发现第 {index} 条的「分级」是「{grade}」，只认 阻断级/建议级";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    if (!TryReadRequiredString(item, "问题", out var issue) || issue.Length == 0)
                    {
                        reason = $"发现第 {index} 条缺「问题」或它是空的";
                        report = NotParsed("", "", "", reason);
                        return false;
                    }

                    var file = ReadOptionalString(item, "文件");
                    var location = ReadOptionalString(item, "位置");
                    var basis = ReadOptionalString(item, "依据");
                    findings.Add(new PreReviewFinding(grade, file, location, issue, basis));
                    if (grade == "阻断级")
                    {
                        blockingCount++;
                    }
                    else
                    {
                        suggestionCount++;
                    }
                }

                report = new PreReviewReport(
                    parsed: true,
                    model: "",
                    promptVersion: "",
                    decisionKey: "",
                    findings: findings,
                    blockingCount: blockingCount,
                    suggestionCount: suggestionCount,
                    fromCache: false,
                    parseReason: "",
                    timestamp: "");
                reason = "";
                return true;
            }
        }

        /// <summary>构造一份「没判成」的报告：零发现、判成了=false、原因写清（决策 42）。</summary>
        /// <param name="model">服务端回传的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        /// <param name="decisionKey">判定键。</param>
        /// <param name="reason">为什么没判成。</param>
        public static PreReviewReport NotParsed(string model, string promptVersion, string decisionKey, string reason)
        {
            return new PreReviewReport(
                parsed: false,
                model: model,
                promptVersion: promptVersion,
                decisionKey: decisionKey,
                findings: Array.Empty<PreReviewFinding>(),
                blockingCount: 0,
                suggestionCount: 0,
                fromCache: false,
                parseReason: reason ?? "",
                timestamp: "");
        }

        /// <summary>报告落盘路径：_Tasks/&lt;需求id&gt;/预审报告.json。</summary>
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
        public PreReviewReport AsStamped(string timestamp, bool fromCache)
        {
            return new PreReviewReport(Parsed, Model, PromptVersion, DecisionKey, Findings, BlockingCount, SuggestionCount, fromCache, ParseReason, timestamp);
        }

        /// <summary>序列化成报告 JSON 文本（缩进）。字段至少含：模型/提示词版本/判定键/判成了/发现/阻断级数/建议级数。</summary>
        public string ToJson()
        {
            var reportObject = BuildNode();
            reportObject["时间戳"] = Timestamp;
            return reportObject.ToJsonString(WriteOptions);
        }

        /// <summary>从报告 JSON 文本解析报告；缺字段或类型不对返回 null（缓存文件坏掉就是没命中）。</summary>
        /// <param name="json">报告 JSON 文本。</param>
        public static PreReviewReport TryFromJson(string json)
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
                || !TryReadInt(obj, "阻断级数", out var blockingCount)
                || !TryReadInt(obj, "建议级数", out var suggestionCount))
            {
                return null;
            }

            var findings = new List<PreReviewFinding>();
            if (obj.TryGetPropertyValue("发现", out var findingsNode) && findingsNode is JsonArray findingsArray)
            {
                foreach (var item in findingsArray)
                {
                    if (item is not JsonObject findingObject)
                    {
                        return null;
                    }

                    if (!TryReadString(findingObject, "分级", out var grade)
                        || !TryReadString(findingObject, "文件", out var file)
                        || !TryReadString(findingObject, "位置", out var location)
                        || !TryReadString(findingObject, "问题", out var issue)
                        || !TryReadString(findingObject, "依据", out var basis))
                    {
                        return null;
                    }

                    findings.Add(new PreReviewFinding(grade, file, location, issue, basis));
                }
            }

            var fromCache = false;
            if (obj.TryGetPropertyValue("来自缓存", out var cacheNode) && cacheNode is JsonValue cacheValue && cacheValue.TryGetValue<bool>(out var cacheFlag))
            {
                fromCache = cacheFlag;
            }

            var parseReason = ReadStringOrEmpty(obj, "解析原因");
            var timestamp = ReadStringOrEmpty(obj, "时间戳");
            return new PreReviewReport(parsed, model, promptVersion, decisionKey, findings, blockingCount, suggestionCount, fromCache, parseReason, timestamp);
        }

        /// <summary>构造报告 JSON 对象（不含时间戳，时间戳由调用点决定）。</summary>
        private JsonObject BuildNode()
        {
            var findingsArray = new JsonArray();
            foreach (var finding in Findings)
            {
                findingsArray.Add(new JsonObject
                {
                    ["分级"] = finding.Grade,
                    ["文件"] = finding.File,
                    ["位置"] = finding.Location,
                    ["问题"] = finding.Issue,
                    ["依据"] = finding.Basis
                });
            }

            return new JsonObject
            {
                ["模型"] = Model,
                ["提示词版本"] = PromptVersion,
                ["判定键"] = DecisionKey,
                ["判成了"] = Parsed,
                ["发现"] = findingsArray,
                ["阻断级数"] = BlockingCount,
                ["建议级数"] = SuggestionCount,
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

        /// <summary>读字符串键；缺失或类型不对给空串（发现里的 文件/位置/依据 宽松处理）。</summary>
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
