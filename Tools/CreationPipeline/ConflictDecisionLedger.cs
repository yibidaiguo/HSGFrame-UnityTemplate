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
    /// <summary>裁决流水里的一条记录：谁、何时、选了哪个、从什么状态到什么状态，七个字段齐全。</summary>
    public sealed class ConflictDecisionRecord
    {
        /// <summary>
        /// 构造一条裁决流水记录。
        /// </summary>
        /// <param name="sequenceNumber">序号，从 1 起连续递增。</param>
        /// <param name="conflictIdentifier">冲突 id，形如 CF-0001。</param>
        /// <param name="resolverName">裁决人姓名。</param>
        /// <param name="choice">裁决选择：改新的 / 改旧的 / 强制推送。</param>
        /// <param name="moment">裁决时间，ISO 8601 字符串。</param>
        /// <param name="stateBefore">裁决前状态。</param>
        /// <param name="stateAfter">裁决后状态。</param>
        internal ConflictDecisionRecord(
            int sequenceNumber,
            string conflictIdentifier,
            string resolverName,
            string choice,
            string moment,
            string stateBefore,
            string stateAfter)
        {
            SequenceNumber = sequenceNumber;
            ConflictIdentifier = conflictIdentifier;
            ResolverName = resolverName;
            Choice = choice;
            Moment = moment;
            StateBefore = stateBefore;
            StateAfter = stateAfter;
        }

        /// <summary>序号，从 1 起连续递增。</summary>
        public int SequenceNumber { get; }

        /// <summary>冲突 id，形如 CF-0001。</summary>
        public string ConflictIdentifier { get; }

        /// <summary>裁决人姓名。</summary>
        public string ResolverName { get; }

        /// <summary>裁决选择：改新的 / 改旧的 / 强制推送。</summary>
        public string Choice { get; }

        /// <summary>裁决时间，ISO 8601 字符串。</summary>
        public string Moment { get; }

        /// <summary>裁决前状态。</summary>
        public string StateBefore { get; }

        /// <summary>裁决后状态。</summary>
        public string StateAfter { get; }
    }

    /// <summary>
    /// 冲突裁决流水（Pools/Designs/conflict-decisions.json）：只追加的账本，永不改既有条目。
    /// 每条裁决（含强制推送挂账与事后补选销账）都追加一条，补选不会覆盖掉上一次的裁决——
    /// 「当初是谁强制推送的」永远查得出来。
    /// </summary>
    public sealed class ConflictDecisionLedger
    {
        private readonly IReadOnlyList<ConflictDecisionRecord> _records;

        /// <summary>
        /// 构造一份裁决流水视图。
        /// </summary>
        /// <param name="records">全部记录，按序号升序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常（含空流水）为空串。</param>
        internal ConflictDecisionLedger(IReadOnlyList<ConflictDecisionRecord> records, string loadFailureReason)
        {
            _records = records;
            LoadFailureReason = loadFailureReason;
        }

        /// <summary>全部记录，按序号升序。</summary>
        public IReadOnlyList<ConflictDecisionRecord> Records
        {
            get { return _records; }
        }

        /// <summary>加载失败原因；正常（含空流水）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 从池根加载裁决流水：文件不存在返回空流水 + 原因空串（空流水是正常状态）；
        /// 顶层不是数组或整份 JSON 坏返回空流水并给原因；单条解析不了的条目跳过并把原因
        /// 累加进 LoadFailureReason，不让一份坏数据把整本流水读没。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static ConflictDecisionLedger Load(string poolRoot)
        {
            var filePath = PoolPaths.ConflictDecisionLedgerFile(poolRoot);
            if (!File.Exists(filePath))
            {
                return new ConflictDecisionLedger(Array.Empty<ConflictDecisionRecord>(), "");
            }

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ConflictDecisionLedger(Array.Empty<ConflictDecisionRecord>(), $"裁决流水读不了：{exception.Message}");
            }

            try
            {
                var root = JsonNode.Parse(text);
                if (root is not JsonArray array)
                {
                    return new ConflictDecisionLedger(Array.Empty<ConflictDecisionRecord>(), "裁决流水顶层必须是 JSON 数组");
                }

                var records = new List<ConflictDecisionRecord>();
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

                    if (!TryReadRecord(entryObject, out var record, out var failureReason))
                    {
                        failures.Add($"第 {index} 条解析失败：{failureReason}，已跳过");
                        continue;
                    }

                    records.Add(record);
                }

                records.Sort((left, right) => left.SequenceNumber.CompareTo(right.SequenceNumber));
                var reason = failures.Count == 0 ? "" : string.Join("；", failures);
                return new ConflictDecisionLedger(records, reason);
            }
            catch (JsonException exception)
            {
                return new ConflictDecisionLedger(Array.Empty<ConflictDecisionRecord>(), $"裁决流水 JSON 解析失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 往裁决流水追加一条记录并写盘，返回新建的那条。序号 = 现存最大序号 + 1（文件不存在从 1 起）。
        /// 只往数组末尾 Add，绝不修改任何既有元素——这是账本的底线。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="conflictIdentifier">冲突 id，形如 CF-0001。</param>
        /// <param name="resolverName">裁决人姓名。</param>
        /// <param name="choice">裁决选择：改新的 / 改旧的 / 强制推送。</param>
        /// <param name="moment">裁决时间，ISO 8601 字符串。</param>
        /// <param name="stateBefore">裁决前状态。</param>
        /// <param name="stateAfter">裁决后状态。</param>
        public static ConflictDecisionRecord Append(
            string poolRoot,
            string conflictIdentifier,
            string resolverName,
            string choice,
            string moment,
            string stateBefore,
            string stateAfter)
        {
            var filePath = PoolPaths.ConflictDecisionLedgerFile(poolRoot);
            var nextNumber = ScanNextNumber(filePath);

            var recordObject = new JsonObject
            {
                ["序号"] = nextNumber,
                ["冲突id"] = conflictIdentifier,
                ["人"] = resolverName,
                ["选择"] = choice,
                ["时间"] = moment,
                ["裁决前状态"] = stateBefore,
                ["裁决后状态"] = stateAfter
            };

            JsonArray array;
            if (File.Exists(filePath))
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                if (root is not JsonArray existingArray)
                {
                    throw new InvalidOperationException($"裁决流水顶层不是数组，无法追加：{filePath}");
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

            array.Add(recordObject);
            File.WriteAllText(filePath, array.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return ReadRecordFromObject(recordObject);
        }

        /// <summary>
        /// 按冲突 id 查它的全部裁决历史，按序号升序返回；没裁决过返回空列表。
        /// </summary>
        /// <param name="conflictIdentifier">冲突 id，形如 CF-0001。</param>
        public IReadOnlyList<ConflictDecisionRecord> FindByConflict(string conflictIdentifier)
        {
            var matched = new List<ConflictDecisionRecord>();
            foreach (var record in _records)
            {
                if (string.Equals(record.ConflictIdentifier, conflictIdentifier, StringComparison.Ordinal))
                {
                    matched.Add(record);
                }
            }

            matched.Sort((left, right) => left.SequenceNumber.CompareTo(right.SequenceNumber));
            return matched;
        }

        /// <summary>扫流水文件原文里 序号 的最大值 +1；文件不存在或没有匹配返回 1。</summary>
        private static int ScanNextNumber(string filePath)
        {
            var maxNumber = 0;
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                foreach (Match match in Regex.Matches(text, "\"序号\"\\s*:\\s*(\\d+)"))
                {
                    if (int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                    {
                        maxNumber = number;
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>从 JsonObject 读一条流水记录；序号缺失或类型不对返回 false 并给原因。</summary>
        private static bool TryReadRecord(JsonObject obj, out ConflictDecisionRecord record, out string failureReason)
        {
            record = null;
            failureReason = "";
            if (!TryReadInt(obj, "序号", out var sequenceNumber))
            {
                failureReason = "缺少 序号";
                return false;
            }

            record = new ConflictDecisionRecord(
                sequenceNumber,
                ReadStringOrEmpty(obj, "冲突id"),
                ReadStringOrEmpty(obj, "人"),
                ReadStringOrEmpty(obj, "选择"),
                ReadStringOrEmpty(obj, "时间"),
                ReadStringOrEmpty(obj, "裁决前状态"),
                ReadStringOrEmpty(obj, "裁决后状态"));
            return true;
        }

        /// <summary>从 JsonObject 读一条记录并构造成 ConflictDecisionRecord（追加后共用）。</summary>
        private static ConflictDecisionRecord ReadRecordFromObject(JsonObject obj)
        {
            return new ConflictDecisionRecord(
                TryReadInt(obj, "序号", out var sequenceNumber) ? sequenceNumber : 0,
                ReadStringOrEmpty(obj, "冲突id"),
                ReadStringOrEmpty(obj, "人"),
                ReadStringOrEmpty(obj, "选择"),
                ReadStringOrEmpty(obj, "时间"),
                ReadStringOrEmpty(obj, "裁决前状态"),
                ReadStringOrEmpty(obj, "裁决后状态"));
        }

        /// <summary>读必须为数字的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadInt(JsonObject obj, string key, out int value)
        {
            value = 0;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            if (jsonValue.GetValueKind() != JsonValueKind.Number)
            {
                return false;
            }

            value = jsonValue.GetValue<int>();
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

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonArray 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

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
