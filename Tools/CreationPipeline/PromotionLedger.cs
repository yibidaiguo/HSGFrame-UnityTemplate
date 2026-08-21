using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一条晋升提案：同类打回意见攒够阈值后入库，带状态机（待批 → 已批准/已拒绝 → 已落地）。
    /// 一个提案一个 PR-xxxx.json 文件；终态（已拒绝/已落地）不许覆盖。
    /// </summary>
    public sealed class PromotionRecord
    {
        /// <summary>id 模式：PR- 加四位数字。</summary>
        public const string IdentifierPatternText = "^PR-\\d{4}$";

        /// <summary>合法的状态值。</summary>
        public static readonly string[] AllowedStates = { "待批", "已批准", "已拒绝", "已落地" };

        /// <summary>待批状态值。</summary>
        public const string PendingState = "待批";

        /// <summary>已批准状态值。</summary>
        public const string ApprovedState = "已批准";

        /// <summary>已拒绝状态值。</summary>
        public const string RejectedState = "已拒绝";

        /// <summary>已落地状态值。</summary>
        public const string LandedState = "已落地";

        /// <summary>
        /// 构造一条晋升提案。
        /// </summary>
        /// <param name="identifier">提案 id，形如 PR-0001。</param>
        /// <param name="category">问题类别。</param>
        /// <param name="count">同类条数。</param>
        /// <param name="rulability">可规则化性。</param>
        /// <param name="targetChannel">晋升去向：检查器 / 预审规则 / 无。</param>
        /// <param name="moduleNames">涉及模块。</param>
        /// <param name="quotations">原文引用。</param>
        /// <param name="state">状态：待批 / 已批准 / 已拒绝 / 已落地。</param>
        /// <param name="proposedMoment">提出时间，ISO 8601 字符串。</param>
        /// <param name="deciderName">裁决人，未裁决时为空串。</param>
        /// <param name="decidedMoment">裁决时间，未裁决时为空串。</param>
        /// <param name="landingArtifact">落地产物路径，未落地时为空串。</param>
        public PromotionRecord(
            string identifier,
            string category,
            int count,
            string rulability,
            string targetChannel,
            IReadOnlyList<string> moduleNames,
            IReadOnlyList<string> quotations,
            string state,
            string proposedMoment,
            string deciderName,
            string decidedMoment,
            string landingArtifact)
        {
            Identifier = identifier ?? "";
            Category = category ?? "";
            Count = count;
            Rulability = rulability ?? "";
            TargetChannel = targetChannel ?? "";
            ModuleNames = moduleNames ?? Array.Empty<string>();
            Quotations = quotations ?? Array.Empty<string>();
            State = state ?? "";
            ProposedMoment = proposedMoment ?? "";
            DeciderName = deciderName ?? "";
            DecidedMoment = decidedMoment ?? "";
            LandingArtifact = landingArtifact ?? "";
        }

        /// <summary>提案 id，形如 PR-0001。</summary>
        public string Identifier { get; }

        /// <summary>问题类别。</summary>
        public string Category { get; }

        /// <summary>同类条数。</summary>
        public int Count { get; }

        /// <summary>可规则化性。</summary>
        public string Rulability { get; }

        /// <summary>晋升去向：检查器 / 预审规则 / 无。</summary>
        public string TargetChannel { get; }

        /// <summary>涉及模块。</summary>
        public IReadOnlyList<string> ModuleNames { get; }

        /// <summary>原文引用。</summary>
        public IReadOnlyList<string> Quotations { get; }

        /// <summary>状态：待批 / 已批准 / 已拒绝 / 已落地。</summary>
        public string State { get; }

        /// <summary>提出时间，ISO 8601 字符串。</summary>
        public string ProposedMoment { get; }

        /// <summary>裁决人，未裁决时为空串。</summary>
        public string DeciderName { get; }

        /// <summary>裁决时间，未裁决时为空串。</summary>
        public string DecidedMoment { get; }

        /// <summary>落地产物路径，未落地时为空串。</summary>
        public string LandingArtifact { get; }

        /// <summary>是否未关闭：状态是 待批 或 已批准。未关闭的同类提案挡新提案入库。</summary>
        public bool IsOpen
        {
            get
            {
                return string.Equals(State, PendingState, StringComparison.Ordinal)
                    || string.Equals(State, ApprovedState, StringComparison.Ordinal);
            }
        }

        /// <summary>是否终态：状态是 已拒绝 或 已落地。终态不许再改。</summary>
        public bool IsTerminal
        {
            get
            {
                return string.Equals(State, RejectedState, StringComparison.Ordinal)
                    || string.Equals(State, LandedState, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 晋升提案账本（Pools/Promotions/）：一个提案一个 PR-xxxx.json 文件，只追加、就地改状态。
    /// 空账本是正常状态（目录不存在时 Load 返回空账本、LoadFailureReason 为空串）；
    /// 「目录空」与「文件读不动」必须分开（锁定决策 42）。
    /// </summary>
    public sealed class PromotionLedger
    {
        private readonly IReadOnlyList<PromotionRecord> _records;

        /// <summary>
        /// 构造一份晋升提案账本视图。
        /// </summary>
        /// <param name="records">全部提案，按 id 序数序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常时为空串。</param>
        public PromotionLedger(IReadOnlyList<PromotionRecord> records, string loadFailureReason)
        {
            _records = records ?? Array.Empty<PromotionRecord>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>全部提案，按 id 序数序排列。</summary>
        public IReadOnlyList<PromotionRecord> Records
        {
            get { return _records; }
        }

        /// <summary>加载失败原因；正常（含空账本）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>未关闭条数：状态是 待批 或 已批准 的都算。待批提案是待办不是违规。</summary>
        public int OpenCount()
        {
            var count = 0;
            foreach (var record in _records)
            {
                if (record.IsOpen)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>按问题类别找第一条未关闭的提案；没有返回 null。</summary>
        /// <param name="category">问题类别。</param>
        public PromotionRecord FindOpenByCategory(string category)
        {
            foreach (var record in _records)
            {
                if (record.IsOpen && string.Equals(record.Category, category, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// 从池根加载晋升提案账本：逐文件读 &lt;池根&gt;/Promotions/PR-xxxx.json。
        /// 目录不存在返回空账本、原因空串（空账本是正常状态）；单个坏文件跳过并累加原因到
        /// LoadFailureReason，不让一份坏条目把整本账读没。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static PromotionLedger Load(string poolRoot)
        {
            var directory = PoolPaths.PromotionProposalDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return new PromotionLedger(Array.Empty<PromotionRecord>(), "");
            }

            var records = new List<PromotionRecord>();
            var failures = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(directory, "PR-*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(filePath));
                    if (root is not JsonObject entryObject)
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：顶层不是对象，已跳过");
                        continue;
                    }

                    if (!TryReadRecord(entryObject, out var record, out var failureReason))
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：{failureReason}，已跳过");
                        continue;
                    }

                    records.Add(record);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    failures.Add($"{Path.GetFileName(filePath)}：{exception.Message}，已跳过");
                }
            }

            records.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            var reason = failures.Count == 0 ? "" : string.Join("；", failures);
            return new PromotionLedger(records, reason);
        }

        /// <summary>
        /// 往晋升提案账本追加一条提案并写盘，返回新建的那条；不入库时返回 null 并把原因写进 reason。
        /// 幂等：同问题类别已有未关闭（待批 / 已批准）的提案时不入库；已拒绝 / 已落地的不挡新提案
        /// ——终态当挡板会让沉淀闭环卡死在第一次拒绝上。
        /// 晋升去向是「无」（不可规则化）的不入库；账本读不动（LoadFailureReason 非空）时拒绝入库。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="proposal">晋升提案。</param>
        /// <param name="proposedMoment">提出时间，ISO 8601 字符串。</param>
        /// <param name="reason">不入库的原因，入库时为空串。</param>
        public static PromotionRecord Append(
            string poolRoot,
            PromotionProposal proposal,
            string proposedMoment,
            out string reason)
        {
            if (proposal == null)
            {
                reason = "提案为空，无法入库";
                return null;
            }

            var ledger = Load(poolRoot);
            if (ledger.LoadFailureReason.Length > 0)
            {
                reason = $"账本读不动，拒绝入库：{ledger.LoadFailureReason}";
                return null;
            }

            var openRecord = ledger.FindOpenByCategory(proposal.Category);
            if (openRecord != null)
            {
                reason = $"同类问题「{proposal.Category}」已有未关闭提案 {openRecord.Identifier}"
                    + $"（状态「{openRecord.State}」）；先裁决它或等它落地再提";
                return null;
            }

            if (string.Equals(proposal.TargetChannel, "无", StringComparison.Ordinal))
            {
                reason = $"提案「{proposal.Category}」的晋升去向是「无」（不可规则化），没有落点，不入库";
                return null;
            }

            var directory = PoolPaths.PromotionProposalDirectory(poolRoot);
            Directory.CreateDirectory(directory);
            var nextNumber = ScanNextNumber(directory);
            var identifier = "PR-" + nextNumber.ToString().PadLeft(4, '0');
            var filePath = Path.Combine(directory, identifier + ".json");

            var content = new JsonObject
            {
                ["id"] = identifier,
                ["问题类别"] = proposal.Category,
                ["同类条数"] = proposal.Count,
                ["可规则化性"] = proposal.Rulability,
                ["晋升去向"] = proposal.TargetChannel,
                ["涉及模块"] = ToJsonArray(proposal.ModuleNames),
                ["原文引用"] = ToJsonArray(proposal.Quotations),
                ["状态"] = PromotionRecord.PendingState,
                ["提出时间"] = proposedMoment ?? "",
                ["裁决人"] = "",
                ["裁决时间"] = "",
                ["落地产物"] = ""
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));

            reason = "";
            return new PromotionRecord(
                identifier,
                proposal.Category,
                proposal.Count,
                proposal.Rulability,
                proposal.TargetChannel,
                proposal.ModuleNames,
                proposal.Quotations,
                PromotionRecord.PendingState,
                proposedMoment ?? "",
                "",
                "",
                "");
        }

        /// <summary>
        /// 就地改一条提案的状态，其余字段一字不动，整体写回。合法转换只有三条：
        /// 待批 → 已批准（要求裁决人非空）、待批 → 已拒绝（要求裁决人非空）、已批准 → 已落地（要求产物路径非空）。
        /// 除这三条之外一律返回 false 并写清 reason；终态（已拒绝 / 已落地）一律拒绝再改——销账不许覆盖。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="identifier">提案 id，形如 PR-0001。</param>
        /// <param name="newState">目标状态。</param>
        /// <param name="deciderName">裁决人姓名。</param>
        /// <param name="decidedMoment">裁决时间，ISO 8601 字符串。</param>
        /// <param name="landingArtifact">落地产物路径。</param>
        /// <param name="reason">失败原因，成功时为空串。</param>
        public static bool UpdateState(
            string poolRoot,
            string identifier,
            string newState,
            string deciderName,
            string decidedMoment,
            string landingArtifact,
            out string reason)
        {
            var filePath = Path.Combine(PoolPaths.PromotionProposalDirectory(poolRoot), identifier + ".json");
            if (!File.Exists(filePath))
            {
                reason = $"提案 {identifier} 不存在：{filePath}";
                return false;
            }

            JsonObject target;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                if (root is not JsonObject entryObject)
                {
                    reason = $"提案 {identifier} 顶层不是对象，无法改状态";
                    return false;
                }

                target = entryObject;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                reason = $"提案 {identifier} 读不了：{exception.Message}";
                return false;
            }

            var currentState = ReadStringOrEmpty(target, "状态");
            if (!IsAllowedState(newState))
            {
                reason = $"新状态「{newState}」不合法；合法值是：{string.Join("、", PromotionRecord.AllowedStates)}";
                return false;
            }

            if (IsTerminalState(currentState))
            {
                var decider = ReadStringOrEmpty(target, "裁决人");
                var previous = decider.Length > 0 ? $"当时的状态是「{currentState}」，裁决人「{decider}」" : $"当时的状态是「{currentState}」，没有裁决记录";
                reason = $"提案 {identifier} 已经是终态，{previous}；终态不许覆盖";
                return false;
            }

            // 三条合法转换；其余组合（含 待批 → 已落地）一律拒绝。
            if (string.Equals(currentState, PromotionRecord.PendingState, StringComparison.Ordinal)
                && string.Equals(newState, PromotionRecord.ApprovedState, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(deciderName))
                {
                    reason = $"提案 {identifier} 批准必须给出裁决人姓名";
                    return false;
                }

                target["状态"] = PromotionRecord.ApprovedState;
                target["裁决人"] = deciderName ?? "";
                target["裁决时间"] = decidedMoment ?? "";
                target["落地产物"] = landingArtifact ?? "";
                WriteBack(filePath, target);
                reason = "";
                return true;
            }

            if (string.Equals(currentState, PromotionRecord.PendingState, StringComparison.Ordinal)
                && string.Equals(newState, PromotionRecord.RejectedState, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(deciderName))
                {
                    reason = $"提案 {identifier} 拒绝必须给出裁决人姓名";
                    return false;
                }

                target["状态"] = PromotionRecord.RejectedState;
                target["裁决人"] = deciderName ?? "";
                target["裁决时间"] = decidedMoment ?? "";
                target["落地产物"] = landingArtifact ?? "";
                WriteBack(filePath, target);
                reason = "";
                return true;
            }

            if (string.Equals(currentState, PromotionRecord.ApprovedState, StringComparison.Ordinal)
                && string.Equals(newState, PromotionRecord.LandedState, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(landingArtifact))
                {
                    reason = $"提案 {identifier} 落地必须给出产物路径";
                    return false;
                }

                target["状态"] = PromotionRecord.LandedState;
                target["落地产物"] = landingArtifact ?? "";
                WriteBack(filePath, target);
                reason = "";
                return true;
            }

            if (string.Equals(currentState, PromotionRecord.PendingState, StringComparison.Ordinal)
                && string.Equals(newState, PromotionRecord.LandedState, StringComparison.Ordinal))
            {
                reason = $"提案 {identifier} 还在待批；落地必须先批准";
                return false;
            }

            reason = $"提案 {identifier} 不支持的转换：{currentState} → {newState}";
            return false;
        }

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonArray 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>扫晋升提案目录里 PR-四位数字 的最大号 +1；目录为空返回 1。</summary>
        private static int ScanNextNumber(string directory)
        {
            var maxNumber = 0;
            foreach (var filePath in Directory.EnumerateFiles(directory, "PR-*.json", SearchOption.TopDirectoryOnly))
            {
                var match = Regex.Match(Path.GetFileName(filePath), "^PR-(\\d{4})\\.json$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                {
                    maxNumber = number;
                }
            }

            return maxNumber + 1;
        }

        /// <summary>把字符串列表转成 JsonArray。</summary>
        private static JsonArray ToJsonArray(IReadOnlyList<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }

        /// <summary>读一条提案；id 缺失或类型不对算坏文件，其余字段宽松读（门禁负责查空）。</summary>
        private static bool TryReadRecord(JsonObject obj, out PromotionRecord record, out string failureReason)
        {
            record = null;
            failureReason = "";

            if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
            {
                failureReason = "缺少 id";
                return false;
            }

            record = new PromotionRecord(
                identifier,
                ReadStringOrEmpty(obj, "问题类别"),
                TryReadInt(obj, "同类条数"),
                ReadStringOrEmpty(obj, "可规则化性"),
                ReadStringOrEmpty(obj, "晋升去向"),
                ReadStringArray(obj, "涉及模块"),
                ReadStringArray(obj, "原文引用"),
                ReadStringOrEmpty(obj, "状态"),
                ReadStringOrEmpty(obj, "提出时间"),
                ReadStringOrEmpty(obj, "裁决人"),
                ReadStringOrEmpty(obj, "裁决时间"),
                ReadStringOrEmpty(obj, "落地产物"));
            return true;
        }

        /// <summary>读必须为字符串的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = "";
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            if (jsonValue.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            value = jsonValue.GetValue<string>() ?? "";
            return true;
        }

        /// <summary>读字符串键，缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonObject obj, string key)
        {
            return TryReadString(obj, key, out var value) ? value : "";
        }

        /// <summary>读整数键，缺失或类型不对给 0。</summary>
        private static int TryReadInt(JsonObject obj, string key)
        {
            if (obj.TryGetPropertyValue(key, out var node) && node is JsonValue jsonValue
                && jsonValue.GetValueKind() == JsonValueKind.Number
                && jsonValue.TryGetValue<int>(out var value))
            {
                return value;
            }

            return 0;
        }

        /// <summary>读字符串数组键，缺失、类型不对或含非字符串元素时给空数组。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string key)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var item in array)
            {
                if (item is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                {
                    values.Add(jsonValue.GetValue<string>() ?? "");
                }
            }

            return values;
        }

        private static bool IsAllowedState(string state)
        {
            foreach (var allowed in PromotionRecord.AllowedStates)
            {
                if (string.Equals(state, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTerminalState(string state)
        {
            return string.Equals(state, PromotionRecord.RejectedState, StringComparison.Ordinal)
                || string.Equals(state, PromotionRecord.LandedState, StringComparison.Ordinal);
        }

        /// <summary>把改过的提案整体写回原文件，保持 UTF-8 无 BOM。</summary>
        private static void WriteBack(string filePath, JsonObject target)
        {
            File.WriteAllText(filePath, target.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

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
