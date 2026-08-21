using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一轮取活判定的轮次记录：轮次、判定跑没跑成、取没取到活、原因与时刻。</summary>
    public sealed class DaemonTickRecord
    {
        /// <summary>
        /// 构造一轮的轮次记录。
        /// </summary>
        /// <param name="round">第几轮，从 1 开始。</param>
        /// <param name="decided">这一轮的取活判定跑成了没有。</param>
        /// <param name="shouldTake">这一轮该不该取活；判定没跑成时必须为 false。</param>
        /// <param name="workItemId">取到的需求 id，没取到时为空串。</param>
        /// <param name="fromWake">这一轮是否由唤醒信号提前触发。</param>
        /// <param name="reason">结果说明文字，无论什么结果都要写清为什么。</param>
        /// <param name="moment">这一轮的取活判定时刻，ISO 8601。</param>
        public DaemonTickRecord(
            int round,
            bool decided,
            bool shouldTake,
            string workItemId,
            bool fromWake,
            string reason,
            string moment)
        {
            Round = round;
            Decided = decided;
            // 判定没跑成时取活必须是 false（决策 42：没取到活与判定没跑成是两支，不许合并）。
            ShouldTake = decided && shouldTake;
            WorkItemId = workItemId ?? "";
            FromWake = fromWake;
            Reason = reason ?? "";
            Moment = moment ?? "";
        }

        /// <summary>第几轮，从 1 开始。</summary>
        public int Round { get; }

        /// <summary>这一轮的取活判定跑成了没有；false 表示判定段抛异常没跑成。</summary>
        public bool Decided { get; }

        /// <summary>这一轮该不该取活；判定没跑成时恒为 false。</summary>
        public bool ShouldTake { get; }

        /// <summary>取到的需求 id；没取到时为空串。</summary>
        public string WorkItemId { get; }

        /// <summary>这一轮是否由唤醒信号提前触发。</summary>
        public bool FromWake { get; }

        /// <summary>结果说明文字，永远非空。</summary>
        public string Reason { get; }

        /// <summary>这一轮的取活判定时刻，ISO 8601。</summary>
        public string Moment { get; }
    }

    /// <summary>
    /// 引擎轮次账本：&lt;仓库根&gt;/_Tasks/引擎轮次.jsonl，一行一条 JSON 追加写。
    /// 空账本（文件不存在）是正常状态不是错误（决策 77）；坏行跳过但计数。
    /// </summary>
    public static class DaemonTickLedger
    {
        /// <summary>账本文件路径：&lt;仓库根&gt;/_Tasks/引擎轮次.jsonl。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string LedgerFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "引擎轮次.jsonl");
        }

        /// <summary>最近一次 Read 跳过的坏行数；Read 每跑一次重置。</summary>
        public static int LastReadBadLineCount { get; private set; }

        /// <summary>
        /// 最近一次 Read「整份文件读不动」的原因；读成了（含文件不存在）时是空串。
        /// **有它才分得开两件事**：账本是空的（正常，决策 77）与账本读不动（故障）。
        /// 少了这一条，哪天面板拿账本印「问题 0 条」，读不动就会被印成「一切正常」——
        /// 决策 42 那类假绿。
        /// </summary>
        public static string LastReadFailureReason { get; private set; } = "";

        /// <summary>
        /// 往账本追加一行记录：UTF-8 无 BOM、行尾 \n、键名中文。
        /// 目录不存在先建；追加失败让异常冒泡——账本写不进是该停下来的硬伤。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="record">要落账的一轮记录。</param>
        public static void Append(string repositoryRoot, DaemonTickRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var filePath = LedgerFile(repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = new JsonObject
            {
                ["轮次"] = record.Round,
                ["判定跑成"] = record.Decided,
                ["取活"] = record.ShouldTake,
                ["工作项"] = record.WorkItemId,
                ["来自唤醒"] = record.FromWake,
                ["原因"] = record.Reason,
                ["时刻"] = record.Moment
            };

            File.AppendAllText(filePath, content.ToJsonString(WriteOptions) + "\n", new UTF8Encoding(false));
        }

        /// <summary>
        /// 逐行读账本：坏行跳过但计数（见 <see cref="LastReadBadLineCount"/>），
        /// 文件不存在返回空列表（空是正常状态，不是错误——决策 77）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<DaemonTickRecord> Read(string repositoryRoot)
        {
            LastReadBadLineCount = 0;
            LastReadFailureReason = "";
            var filePath = LedgerFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return Array.Empty<DaemonTickRecord>();
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 读不动仍返回空列表（调用方不必为此改形状），但**原因要留下**：
                // LastReadFailureReason 非空就说明这次的空是故障，不是「本来就没有」。
                LastReadFailureReason = $"账本读不动：{filePath}：{exception.Message}";
                return Array.Empty<DaemonTickRecord>();
            }

            var records = new List<DaemonTickRecord>();
            foreach (var line in lines)
            {
                if (TryParse(line, out var record))
                {
                    records.Add(record);
                }
                else
                {
                    LastReadBadLineCount++;
                }
            }

            return records;
        }

        /// <summary>写盘选项：以默认 options 为基类（.NET 10 下无 resolver 的 options 序列化 JsonObject 会抛）。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        // 逐行解析；七个键缺一个或类型不对都算坏行。
        private static bool TryParse(string line, out DaemonTickRecord record)
        {
            record = null;
            JsonNode node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                return false;
            }

            if (node is not JsonObject obj)
            {
                return false;
            }

            if (!TryReadInt(obj, "轮次", out var round))
            {
                return false;
            }

            if (!TryReadBool(obj, "判定跑成", out var decided))
            {
                return false;
            }

            if (!TryReadBool(obj, "取活", out var shouldTake))
            {
                return false;
            }

            if (!TryReadString(obj, "工作项", out var workItemId))
            {
                return false;
            }

            if (!TryReadBool(obj, "来自唤醒", out var fromWake))
            {
                return false;
            }

            if (!TryReadString(obj, "原因", out var reason))
            {
                return false;
            }

            if (!TryReadString(obj, "时刻", out var moment))
            {
                return false;
            }

            record = new DaemonTickRecord(round, decided, shouldTake, workItemId, fromWake, reason, moment);
            return true;
        }

        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = null;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            return jsonValue.TryGetValue<string>(out value);
        }

        private static bool TryReadBool(JsonObject obj, string key, out bool value)
        {
            value = false;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            return jsonValue.TryGetValue<bool>(out value);
        }

        private static bool TryReadInt(JsonObject obj, string key, out int value)
        {
            value = 0;
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            return jsonValue.TryGetValue<int>(out value);
        }
    }
}
