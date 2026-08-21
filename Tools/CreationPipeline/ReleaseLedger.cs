using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>放行流水里的一条：需求 id、风险级、范围、放行时间、合并提交与抽查状态。</summary>
    public sealed class ReleaseLedgerEntry
    {
        /// <summary>id 模式：RL- 加四位数字。</summary>
        public const string IdentifierPatternText = "^RL-\\d{4}$";

        /// <summary>合法的抽查状态：未抽查 / 合格 / 发现问题。抽查是销账动作，销了不许改回未抽查。</summary>
        public static readonly string[] AllowedSpotCheckStates = { "未抽查", "合格", "发现问题" };

        /// <summary>
        /// 构造一条放行流水条目。
        /// </summary>
        /// <param name="identifier">流水 id，形如 RL-0001。</param>
        /// <param name="requirementIdentifier">需求 id，形如 REQ-0042。</param>
        /// <param name="grade">风险级：低 / 常规 / 高。</param>
        /// <param name="scopes">本次改动涉及的范围。</param>
        /// <param name="releasedMoment">放行时间，ISO 8601 字符串。</param>
        /// <param name="mergeCommit">合并提交哈希；未记为空串。</param>
        /// <param name="spotCheckState">抽查状态：未抽查 / 合格 / 发现问题。</param>
        /// <param name="spotCheckConclusion">抽查结论正文；未抽查时为空串。</param>
        /// <param name="revertCommit">回滚提交哈希；未抽查或合格时为空串。</param>
        internal ReleaseLedgerEntry(
            string identifier,
            string requirementIdentifier,
            string grade,
            IReadOnlyList<string> scopes,
            string releasedMoment,
            string mergeCommit,
            string spotCheckState,
            string spotCheckConclusion,
            string revertCommit)
        {
            Identifier = identifier;
            RequirementIdentifier = requirementIdentifier;
            Grade = grade;
            Scopes = scopes ?? Array.Empty<string>();
            ReleasedMoment = releasedMoment;
            MergeCommit = mergeCommit;
            SpotCheckState = spotCheckState;
            SpotCheckConclusion = spotCheckConclusion;
            RevertCommit = revertCommit;
        }

        /// <summary>流水 id，形如 RL-0001。</summary>
        public string Identifier { get; }

        /// <summary>需求 id，形如 REQ-0042。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>风险级：低 / 常规 / 高。</summary>
        public string Grade { get; }

        /// <summary>本次改动涉及的范围。</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>放行时间，ISO 8601 字符串。</summary>
        public string ReleasedMoment { get; }

        /// <summary>合并提交哈希；未记为空串。</summary>
        public string MergeCommit { get; }

        /// <summary>抽查状态：未抽查 / 合格 / 发现问题。</summary>
        public string SpotCheckState { get; }

        /// <summary>抽查结论正文；未抽查时为空串。</summary>
        public string SpotCheckConclusion { get; }

        /// <summary>回滚提交哈希；未抽查或合格时为空串。</summary>
        public string RevertCommit { get; }

        /// <summary>是否抽查过：抽查状态不是「未抽查」即为 true。</summary>
        public bool IsSpotChecked
        {
            get { return !string.Equals(SpotCheckState, "未抽查", StringComparison.Ordinal); }
        }
    }

    /// <summary>
    /// 放行流水（Pools/release-ledger.json）：自动放行的合并全量入账，只追加 + 就地改抽查状态。
    /// 空流水是正常状态，LoadFailureReason 为空串；文件在但读不动时两者必须能分开。
    /// </summary>
    public sealed class ReleaseLedger
    {
        private readonly IReadOnlyList<ReleaseLedgerEntry> _entries;

        /// <summary>
        /// 构造一份放行流水视图。
        /// </summary>
        /// <param name="entries">全部流水条目，按 id 序数序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常时为空串。</param>
        internal ReleaseLedger(IReadOnlyList<ReleaseLedgerEntry> entries, string loadFailureReason)
        {
            _entries = entries;
            LoadFailureReason = loadFailureReason;
        }

        /// <summary>全部流水条目，按 id 序数序排列。</summary>
        public IReadOnlyList<ReleaseLedgerEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>加载失败原因；正常（含空流水）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>未抽查条数：抽查状态是「未抽查」的条数。</summary>
        public int UncheckedCount()
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.SpotCheckState, "未抽查", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>发现问题条数：抽查状态是「发现问题」的条数。</summary>
        public int ProblemCount()
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.SpotCheckState, "发现问题", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 从池子加载放行流水：文件不存在返回空流水且原因为空串（空流水是正常状态，不是错）；
        /// 文件在但读不动、不是合法 JSON、或「条目」不是数组时返回空流水并给出原因。
        /// 单条解析不了的条目跳过并把原因累加进 LoadFailureReason，不让整份流水因为一条坏数据全丢。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static ReleaseLedger Load(string poolRoot)
        {
            var filePath = PoolPaths.ReleaseLedgerFile(poolRoot);
            if (!File.Exists(filePath))
            {
                return new ReleaseLedger(Array.Empty<ReleaseLedgerEntry>(), "");
            }

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ReleaseLedger(Array.Empty<ReleaseLedgerEntry>(), $"放行流水读不了：{exception.Message}");
            }

            try
            {
                var root = JsonNode.Parse(text);
                if (root is not JsonObject rootObject)
                {
                    return new ReleaseLedger(Array.Empty<ReleaseLedgerEntry>(), "放行流水顶层必须是 JSON 对象");
                }

                if (rootObject["条目"] is not JsonArray array)
                {
                    return new ReleaseLedger(Array.Empty<ReleaseLedgerEntry>(), "放行流水的「条目」必须是数组");
                }

                var entries = new List<ReleaseLedgerEntry>();
                var failures = new List<string>();
                var index = 0;
                foreach (var node in array)
                {
                    index++;
                    if (node is not JsonObject entryObject)
                    {
                        failures.Add($"第 {index} 条不是对象，已跳过");
                        continue;
                    }

                    if (!TryReadEntry(entryObject, out var entry, out var failureReason))
                    {
                        failures.Add($"第 {index} 条解析失败：{failureReason}，已跳过");
                        continue;
                    }

                    entries.Add(entry);
                }

                entries.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
                var reason = failures.Count == 0 ? "" : string.Join("；", failures);
                return new ReleaseLedger(entries, reason);
            }
            catch (JsonException exception)
            {
                return new ReleaseLedger(Array.Empty<ReleaseLedgerEntry>(), $"放行流水 JSON 解析失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 往放行流水追加一条自动放行的合并并写盘，返回新建的那条。
        /// 取号 = 扫现存最大号 +1；新条目的抽查状态恒为「未抽查」、抽查结论与回滚提交恒为空串。
        /// 只追加，绝不改写已有条目的任何字段。往一份读不动的账本上追加会把已有条目冲掉，
        /// 所以 LoadFailureReason 非空时拒绝追加。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，形如 REQ-0042。</param>
        /// <param name="grade">风险级：低 / 常规 / 高。</param>
        /// <param name="scopes">本次改动涉及的范围。</param>
        /// <param name="releasedMoment">放行时间，ISO 8601 字符串。</param>
        /// <param name="mergeCommit">合并提交哈希；没记就传空串。</param>
        public static ReleaseLedgerEntry Append(
            string poolRoot,
            string requirementIdentifier,
            string grade,
            IReadOnlyList<string> scopes,
            string releasedMoment,
            string mergeCommit)
        {
            var ledger = Load(poolRoot);
            if (ledger.LoadFailureReason.Length > 0)
            {
                throw new InvalidOperationException($"放行流水读不动，拒绝追加：{ledger.LoadFailureReason}");
            }

            var filePath = PoolPaths.ReleaseLedgerFile(poolRoot);
            var nextNumber = ScanNextNumber(filePath);
            var identifier = "RL-" + nextNumber.ToString().PadLeft(4, '0');

            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["需求id"] = requirementIdentifier,
                ["风险级"] = grade,
                ["范围"] = new JsonArray(scopes.Select(scope => (JsonNode)JsonValue.Create(scope)).ToArray()),
                ["放行时间"] = releasedMoment,
                ["合并提交"] = mergeCommit,
                ["抽查状态"] = "未抽查",
                ["抽查结论"] = "",
                ["回滚提交"] = ""
            };

            JsonObject root;
            if (File.Exists(filePath))
            {
                var parsed = JsonNode.Parse(File.ReadAllText(filePath));
                if (parsed is not JsonObject existingRoot || existingRoot["条目"] is not JsonArray existingArray)
                {
                    throw new InvalidOperationException($"放行流水顶层或「条目」不是预期形状，无法追加：{filePath}");
                }

                existingArray.Add(entryObject);
                root = existingRoot;
            }
            else
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                root = new JsonObject
                {
                    ["条目"] = new JsonArray(entryObject)
                };
            }

            File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return ReadEntryFromObject(entryObject);
        }

        /// <summary>
        /// 就地记一条抽查结论：把该条的「抽查状态」「抽查结论」「回滚提交」三个键换掉，
        /// 其余字段与其余条目一字不动。以下情形返回 false 并写清 reason：
        /// id 找不到；结论状态不在「合格 / 发现问题」里（不许改回未抽查）；这一条已经抽查过
        /// （重复抽查会把第一次的结论覆盖掉，那是在销毁证据）。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="identifier">流水 id，形如 RL-0001。</param>
        /// <param name="conclusionState">抽查结论状态：合格 / 发现问题。</param>
        /// <param name="conclusionText">抽查结论正文。</param>
        /// <param name="revertCommit">回滚提交哈希；合格时传空串。</param>
        /// <param name="reason">失败原因，成功时为空串。</param>
        public static bool RecordSpotCheck(
            string poolRoot,
            string identifier,
            string conclusionState,
            string conclusionText,
            string revertCommit,
            out string reason)
        {
            var filePath = PoolPaths.ReleaseLedgerFile(poolRoot);
            if (!File.Exists(filePath))
            {
                reason = $"流水条目 {identifier} 不存在：放行流水文件不存在";
                return false;
            }

            JsonObject root;
            JsonArray array;
            try
            {
                var parsed = JsonNode.Parse(File.ReadAllText(filePath));
                if (parsed is not JsonObject parsedRoot || parsedRoot["条目"] is not JsonArray parsedArray)
                {
                    reason = "放行流水读不了：顶层必须是对象且「条目」必须是数组";
                    return false;
                }

                root = parsedRoot;
                array = parsedArray;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                reason = $"放行流水读不了：{exception.Message}";
                return false;
            }

            JsonObject target = null;
            foreach (var node in array)
            {
                if (node is JsonObject candidate
                    && TryReadString(candidate, "id", out var candidateIdentifier)
                    && string.Equals(candidateIdentifier, identifier, StringComparison.Ordinal))
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                reason = $"流水条目 {identifier} 不存在";
                return false;
            }

            if (!IsAllowedConclusionState(conclusionState))
            {
                reason = $"抽查结论状态「{conclusionState}」不合法；合法值是：合格、发现问题（不许改回未抽查）";
                return false;
            }

            var currentState = ReadEntryString(target, "抽查状态");
            if (!string.Equals(currentState, "未抽查", StringComparison.Ordinal))
            {
                var previous = currentState.Length > 0 ? $"当时结论是「{currentState}」" : "当时没有抽查记录";
                reason = $"流水条目 {identifier} 已经抽查过，{previous}；重复抽查会覆盖第一次的结论，拒绝";
                return false;
            }

            target["抽查状态"] = conclusionState;
            target["抽查结论"] = conclusionText ?? "";
            target["回滚提交"] = revertCommit ?? "";

            File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            reason = "";
            return true;
        }

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonArray 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>扫放行流水文件原文里 id 是 RL-四位数字 的最大号 +1；文件不存在或没有匹配返回 1。</summary>
        private static int ScanNextNumber(string filePath)
        {
            var maxNumber = 0;
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                foreach (Match match in Regex.Matches(text, "\"id\"\\s*:\\s*\"RL-(\\d{4})\""))
                {
                    if (int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                    {
                        maxNumber = number;
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>从 JsonObject 读一条流水条目；九个必需键缺一或类型不对返回 false 并给原因。</summary>
        private static bool TryReadEntry(JsonObject obj, out ReleaseLedgerEntry entry, out string failureReason)
        {
            entry = null;
            failureReason = "";

            if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
            {
                failureReason = "缺少 id";
                return false;
            }

            if (!TryReadString(obj, "需求id", out var requirementIdentifier))
            {
                failureReason = "缺少 需求id";
                return false;
            }

            if (!TryReadString(obj, "风险级", out var grade))
            {
                failureReason = "缺少 风险级";
                return false;
            }

            if (!TryReadStringArray(obj, "范围", out var scopes))
            {
                failureReason = "缺少 范围 或它必须是字符串数组";
                return false;
            }

            if (!TryReadString(obj, "放行时间", out var releasedMoment))
            {
                failureReason = "缺少 放行时间";
                return false;
            }

            if (!TryReadString(obj, "合并提交", out var mergeCommit))
            {
                failureReason = "缺少 合并提交";
                return false;
            }

            if (!TryReadString(obj, "抽查状态", out var spotCheckState))
            {
                failureReason = "缺少 抽查状态";
                return false;
            }

            if (!TryReadString(obj, "抽查结论", out var spotCheckConclusion))
            {
                failureReason = "缺少 抽查结论";
                return false;
            }

            if (!TryReadString(obj, "回滚提交", out var revertCommit))
            {
                failureReason = "缺少 回滚提交";
                return false;
            }

            entry = new ReleaseLedgerEntry(
                identifier,
                requirementIdentifier,
                grade,
                scopes,
                releasedMoment,
                mergeCommit,
                spotCheckState,
                spotCheckConclusion,
                revertCommit);
            return true;
        }

        /// <summary>从 JsonObject 读一条流水条目并构造成 ReleaseLedgerEntry（追加后共用）。</summary>
        private static ReleaseLedgerEntry ReadEntryFromObject(JsonObject obj)
        {
            return new ReleaseLedgerEntry(
                ReadEntryString(obj, "id"),
                ReadEntryString(obj, "需求id"),
                ReadEntryString(obj, "风险级"),
                ReadStringArray(obj, "范围"),
                ReadEntryString(obj, "放行时间"),
                ReadEntryString(obj, "合并提交"),
                ReadEntryString(obj, "抽查状态"),
                ReadEntryString(obj, "抽查结论"),
                ReadEntryString(obj, "回滚提交"));
        }

        /// <summary>读条目自己的字符串键；缺失给空串。</summary>
        private static string ReadEntryString(JsonObject obj, string key)
        {
            return TryReadString(obj, key, out var value) ? value : "";
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

        /// <summary>读必须为字符串数组的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadStringArray(JsonObject obj, string key, out IReadOnlyList<string> value)
        {
            value = Array.Empty<string>();
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            {
                return false;
            }

            var items = new List<string>();
            foreach (var item in array)
            {
                if (item is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.String)
                {
                    return false;
                }

                items.Add(jsonValue.GetValue<string>() ?? "");
            }

            value = items;
            return true;
        }

        /// <summary>读字符串数组键，缺失或类型不对给空列表。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string key)
        {
            return TryReadStringArray(obj, key, out var value) ? value : Array.Empty<string>();
        }

        private static bool IsAllowedConclusionState(string conclusionState)
        {
            return string.Equals(conclusionState, "合格", StringComparison.Ordinal)
                || string.Equals(conclusionState, "发现问题", StringComparison.Ordinal);
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
