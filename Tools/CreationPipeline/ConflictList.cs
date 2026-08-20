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
    /// 冲突列表的只读视图：全部条目（按 id 序数序）与加载失败原因。
    /// 空冲突列表是正常状态，LoadFailureReason 为空串，不是错误。
    /// </summary>
    public sealed class ConflictList
    {
        private readonly IReadOnlyList<ConflictEntry> _entries;

        /// <summary>
        /// 构造一个冲突列表视图。
        /// </summary>
        /// <param name="entries">全部冲突条目，按 id 序数序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常时为空串。</param>
        internal ConflictList(IReadOnlyList<ConflictEntry> entries, string loadFailureReason)
        {
            _entries = entries;
            LoadFailureReason = loadFailureReason;
        }

        /// <summary>全部冲突条目，按 id 序数序排列。</summary>
        public IReadOnlyList<ConflictEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>加载失败原因；正常（含空列表）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 未销账条数：状态不是「已裁决」的都算。强制推送只挂账不销账，它的状态留在「未决」，
        /// 所以自然被算进来；第二个条件是给历史数据兜底（早先版本把强制推送写成过已裁决）。
        /// </summary>
        public int PendingCount()
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal)
                    || string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 从池子加载冲突列表：文件不存在返回空列表且原因为空串；顶层不是数组或整份 JSON 坏掉时
        /// 返回空列表并给出原因；单条解析不了的条目跳过并把原因累加进 LoadFailureReason，
        /// 不让整份列表因为一条坏数据全丢。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static ConflictList Load(string poolRoot)
        {
            var filePath = PoolPaths.ConflictListFile(poolRoot);
            if (!File.Exists(filePath))
            {
                return new ConflictList(Array.Empty<ConflictEntry>(), "");
            }

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ConflictList(Array.Empty<ConflictEntry>(), $"冲突列表读不了：{exception.Message}");
            }

            try
            {
                var root = JsonNode.Parse(text);
                if (root is not JsonArray array)
                {
                    return new ConflictList(Array.Empty<ConflictEntry>(), "冲突列表顶层必须是 JSON 数组");
                }

                var entries = new List<ConflictEntry>();
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
                return new ConflictList(entries, reason);
            }
            catch (JsonException exception)
            {
                return new ConflictList(Array.Empty<ConflictEntry>(), $"冲突列表 JSON 解析失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 往冲突列表追加一条状态=未决的冲突并写盘，返回新建的那条。
        /// 取号 = 扫现存最大号 +1；发现阶段不合法时抛 InvalidOperationException。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="oldIdentifier">旧设计或旧需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="discoveryStage">发现阶段，必须是 入库 / 影响评估 之一。</param>
        public static ConflictEntry Append(string poolRoot, string oldIdentifier, string newIdentifier, string discoveryStage)
        {
            if (!IsAllowedStage(discoveryStage))
            {
                throw new InvalidOperationException(
                    $"发现阶段「{discoveryStage}」不合法；合法值是：{string.Join("、", ConflictEntry.AllowedStages)}");
            }

            var filePath = PoolPaths.ConflictListFile(poolRoot);
            var nextNumber = ScanNextNumber(filePath);
            var identifier = "CF-" + nextNumber.ToString().PadLeft(4, '0');

            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["旧"] = oldIdentifier,
                ["新"] = newIdentifier,
                ["发现阶段"] = discoveryStage,
                ["状态"] = ConflictEntry.PendingState,
                ["裁决"] = null
            };

            JsonArray array;
            if (File.Exists(filePath))
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                if (root is not JsonArray existingArray)
                {
                    throw new InvalidOperationException($"冲突列表顶层不是数组，无法追加：{filePath}");
                }

                array = existingArray;
            }
            else
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                array = new JsonArray();
            }

            array.Add(entryObject);
            File.WriteAllText(filePath, array.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return ReadEntryFromObject(entryObject);
        }

        /// <summary>
        /// 就地裁决一条冲突：把该条的「状态」与「裁决」两个键换掉，其余键与其余条目一字不动，整体写回。
        /// 已销账（状态=已裁决）的条目不许再裁决——销账动作改了就查不出账。
        /// 强制推送只挂账、状态留未决，之后可以补选改新的/改旧的来销账；但不许重复强制推送。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="conflictIdentifier">冲突 id，形如 CF-0009。</param>
        /// <param name="resolverName">裁决人姓名，不能为空白。</param>
        /// <param name="choice">三选一：改新的 / 改旧的 / 强制推送。</param>
        /// <param name="moment">裁决时间，写进「时间」字段。</param>
        public static ConflictResolutionResult Resolve(
            string poolRoot,
            string conflictIdentifier,
            string resolverName,
            string choice,
            string moment)
        {
            var filePath = PoolPaths.ConflictListFile(poolRoot);
            if (!File.Exists(filePath))
            {
                return ConflictResolutionResult.Failed($"冲突 {conflictIdentifier} 不存在：冲突列表文件不存在");
            }

            JsonArray array;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                if (root is not JsonArray parsedArray)
                {
                    return ConflictResolutionResult.Failed("冲突列表顶层不是数组，无法裁决");
                }

                array = parsedArray;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return ConflictResolutionResult.Failed($"冲突列表读不了：{exception.Message}");
            }

            JsonObject target = null;
            foreach (var node in array)
            {
                if (node is JsonObject candidate
                    && TryReadString(candidate, "id", out var candidateIdentifier)
                    && string.Equals(candidateIdentifier, conflictIdentifier, StringComparison.Ordinal))
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                return ConflictResolutionResult.Failed($"冲突 {conflictIdentifier} 不存在");
            }

            if (!IsAllowedChoice(choice))
            {
                return ConflictResolutionResult.Failed(
                    $"裁决选择「{choice}」不合法；合法值是：{string.Join("、", ConflictEntry.AllowedChoices)}");
            }

            if (string.Equals(ReadEntryString(target, "状态"), ConflictEntry.ResolvedState, StringComparison.Ordinal))
            {
                var previousChoice = ReadResolutionString(target, "选择");
                var previous = previousChoice.Length > 0 ? $"当时选的是「{previousChoice}」" : "当时没有选择记录";
                return ConflictResolutionResult.Failed(
                    $"冲突 {conflictIdentifier} 已经销账，{previous}；销账不许覆盖");
            }

            // 强制推送是挂账不是销账，所以上面那道「已裁决」的闸拦不住它——它的状态还是未决。
            // 但同一条重复强制推送没有意义：账已经挂上了，再挂一次只是把时间戳往后推，
            // 反而让「这条挂了多久」查不出来。要销账得补选改新的/改旧的。
            if (string.Equals(ReadResolutionString(target, "选择"), ForcePushChoice, StringComparison.Ordinal)
                && string.Equals(choice, ForcePushChoice, StringComparison.Ordinal))
            {
                return ConflictResolutionResult.Failed(
                    $"冲突 {conflictIdentifier} 已经强制推送挂账；要销账请补选「改新的」或「改旧的」");
            }

            if (string.IsNullOrWhiteSpace(resolverName))
            {
                return ConflictResolutionResult.Failed("裁决人为空：必须给出裁决人姓名");
            }

            // 强制推送只是把冲突挂上账（总方案 §三：「新旧配对进冲突列表挂账」），
            // 状态留在未决——真正的裁决是事后补选改新的/改旧的，那才叫销账。
            // 把强制推送也置成已裁决，会连同「已裁决不许覆盖」一起把销账路径堵死。
            var isForcePush = string.Equals(choice, ForcePushChoice, StringComparison.Ordinal);
            target["状态"] = isForcePush ? ConflictEntry.PendingState : ConflictEntry.ResolvedState;
            target["裁决"] = new JsonObject
            {
                ["人"] = resolverName,
                ["选择"] = choice,
                ["时间"] = moment
            };

            File.WriteAllText(filePath, array.ToJsonString(WriteOptions), new UTF8Encoding(false));

            var resolvedEntry = ReadEntryFromObject(target);
            return ConflictResolutionResult.Resolved(resolvedEntry, BuildSystemActions(resolvedEntry));
        }

        /// <summary>「强制推送」这个选择：挂账不销账，与另外两个选择的处置不同，单独立个常量。</summary>
        private const string ForcePushChoice = "强制推送";

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonArray 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>按三选一产出系统动作文案，逐字见任务书；这些是给人看的待办，不是执行。</summary>
        private static IReadOnlyList<string> BuildSystemActions(ConflictEntry entry)
        {
            if (string.Equals(entry.Choice, "改新的", StringComparison.Ordinal))
            {
                return new[] { "新需求让步于既有设计：助手辅助改写新需求，重新入库" };
            }

            if (string.Equals(entry.Choice, "改旧的", StringComparison.Ordinal))
            {
                return new[]
                {
                    "新需求挂「设计变更」义务：完成时作废或替换旧设计记录",
                    $"旧需求打「已被 {entry.NewIdentifier} 演进」标"
                };
            }

            if (string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal))
            {
                return new[]
                {
                    "新需求照常入库执行",
                    "新旧配对挂账，冲突不拦执行；本条仍算未销账",
                    "销账要事后补选「改新的」或「改旧的」"
                };
            }

            return Array.Empty<string>();
        }

        /// <summary>扫冲突列表文件原文里 id 是 CF-四位数字 的最大号 +1；文件不存在或没有匹配返回 1。</summary>
        private static int ScanNextNumber(string filePath)
        {
            var maxNumber = 0;
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                foreach (Match match in Regex.Matches(text, "\"id\"\\s*:\\s*\"CF-(\\d{4})\""))
                {
                    if (int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                    {
                        maxNumber = number;
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>从 JsonObject 读一条冲突条目；五个必需键缺一或类型不对返回 false 并给原因。</summary>
        private static bool TryReadEntry(JsonObject obj, out ConflictEntry entry, out string failureReason)
        {
            entry = null;
            failureReason = "";

            if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
            {
                failureReason = "缺少 id";
                return false;
            }

            if (!TryReadString(obj, "旧", out var oldIdentifier))
            {
                failureReason = "缺少 旧";
                return false;
            }

            if (!TryReadString(obj, "新", out var newIdentifier))
            {
                failureReason = "缺少 新";
                return false;
            }

            if (!TryReadString(obj, "发现阶段", out var discoveryStage))
            {
                failureReason = "缺少 发现阶段";
                return false;
            }

            if (!TryReadString(obj, "状态", out var state))
            {
                failureReason = "缺少 状态";
                return false;
            }

            var hasResolution = false;
            var resolverName = "";
            var choice = "";
            var resolvedMoment = "";
            if (obj.TryGetPropertyValue("裁决", out var resolutionNode)
                && resolutionNode != null
                && resolutionNode.GetValueKind() != JsonValueKind.Null)
            {
                if (resolutionNode is not JsonObject resolutionObject)
                {
                    failureReason = "裁决 必须是 null 或对象";
                    return false;
                }

                hasResolution = true;
                resolverName = ReadStringOrEmpty(resolutionObject, "人");
                choice = ReadStringOrEmpty(resolutionObject, "选择");
                resolvedMoment = ReadStringOrEmpty(resolutionObject, "时间");
            }

            entry = new ConflictEntry(
                identifier,
                oldIdentifier,
                newIdentifier,
                discoveryStage,
                state,
                resolverName,
                choice,
                resolvedMoment,
                hasResolution);
            return true;
        }

        /// <summary>从 JsonObject 读一条条目并构造成 ConflictEntry（追加与裁决后共用）。</summary>
        private static ConflictEntry ReadEntryFromObject(JsonObject obj)
        {
            return new ConflictEntry(
                ReadEntryString(obj, "id"),
                ReadEntryString(obj, "旧"),
                ReadEntryString(obj, "新"),
                ReadEntryString(obj, "发现阶段"),
                ReadEntryString(obj, "状态"),
                ReadResolutionString(obj, "人"),
                ReadResolutionString(obj, "选择"),
                ReadResolutionString(obj, "时间"),
                obj.TryGetPropertyValue("裁决", out var resolutionNode)
                    && resolutionNode != null
                    && resolutionNode.GetValueKind() != JsonValueKind.Null);
        }

        /// <summary>读条目自己的字符串键；缺失给空串。</summary>
        private static string ReadEntryString(JsonObject obj, string key)
        {
            return TryReadString(obj, key, out var value) ? value : "";
        }

        /// <summary>读「裁决」对象里的字符串键；裁决缺失或不是对象给空串。</summary>
        private static string ReadResolutionString(JsonObject obj, string key)
        {
            if (obj.TryGetPropertyValue("裁决", out var resolutionNode)
                && resolutionNode is JsonObject resolutionObject)
            {
                return ReadStringOrEmpty(resolutionObject, key);
            }

            return "";
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

        private static bool IsAllowedStage(string discoveryStage)
        {
            foreach (var allowed in ConflictEntry.AllowedStages)
            {
                if (string.Equals(discoveryStage, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedChoice(string choice)
        {
            foreach (var allowed in ConflictEntry.AllowedChoices)
            {
                if (string.Equals(choice, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
