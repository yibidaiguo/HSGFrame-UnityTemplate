using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>队列里的一条任务：需求 id、入队时间与入队理由。</summary>
    public sealed class QueueEntry
    {
        /// <summary>
        /// 构造一条队列条目。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="enqueueTime">入队时间，原样保留的字符串。</param>
        /// <param name="reason">入队理由。</param>
        public QueueEntry(string requirementIdentifier, string enqueueTime, string reason)
        {
            RequirementIdentifier = requirementIdentifier;
            EnqueueTime = enqueueTime ?? "";
            Reason = reason ?? "";
        }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>入队时间，原样保留的字符串。</summary>
        public string EnqueueTime { get; }

        /// <summary>入队理由。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 先进先出的执行队列：Pools/队列.json 的内存形态。
    /// 文件不存在、JSON 坏掉或「条目」不是数组时退化为空队列，不抛异常，原因记在 LoadFailureReason。
    /// </summary>
    public sealed class ExecutionQueue
    {
        private readonly List<QueueEntry> _entries;

        /// <summary>
        /// 构造一份执行队列。
        /// </summary>
        /// <param name="entries">初始条目，顺序即先进先出顺序；传 null 视为空列表。</param>
        /// <param name="loadFailureReason">加载失败原因，正常为空串。</param>
        public ExecutionQueue(IReadOnlyList<QueueEntry> entries, string loadFailureReason)
        {
            _entries = entries == null ? new List<QueueEntry>() : new List<QueueEntry>(entries);
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>
        /// 从池根加载执行队列：读 &lt;池根&gt;/队列.json。
        /// 文件不存在、JSON 语法错误、「条目」缺失或不是数组时返回空队列不抛异常，原因记进 LoadFailureReason；
        /// 单个条目缺「需求id」时跳过该条目。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static ExecutionQueue Load(string poolRoot)
        {
            var filePath = PoolPaths.QueueFile(poolRoot);
            if (!File.Exists(filePath))
            {
                return new ExecutionQueue(null, $"队列文件不存在：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new ExecutionQueue(null, $"队列文件解析失败：{filePath}：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("条目", out var entriesElement)
                    || entriesElement.ValueKind != JsonValueKind.Array)
                {
                    return new ExecutionQueue(null, $"队列文件缺少「条目」数组：{filePath}");
                }

                var entries = new List<QueueEntry>();
                foreach (var element in entriesElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object
                        || !element.TryGetProperty("需求id", out var identifierElement)
                        || identifierElement.ValueKind != JsonValueKind.String
                        || string.IsNullOrEmpty(identifierElement.GetString()))
                    {
                        continue;
                    }

                    entries.Add(new QueueEntry(
                        identifierElement.GetString(),
                        ReadStringOrEmpty(element, "入队时间"),
                        ReadStringOrEmpty(element, "理由")));
                }

                return new ExecutionQueue(entries, "");
            }
        }

        /// <summary>条目列表，顺序即先进先出的顺序。</summary>
        public IReadOnlyList<QueueEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>加载失败原因，正常加载为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 把一条任务追加到队尾。
        /// 队列里已有同一个「需求id」时返回 false 且不改动（幂等）；否则追加到队尾返回 true。
        /// 入队时间写 moment.ToString("o") 原样保留。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="moment">入队时刻。</param>
        /// <param name="reason">入队理由。</param>
        public bool Enqueue(string requirementIdentifier, DateTimeOffset moment, string reason)
        {
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.RequirementIdentifier, requirementIdentifier, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            _entries.Add(new QueueEntry(requirementIdentifier, moment.ToString("o"), reason ?? ""));
            return true;
        }

        /// <summary>
        /// 取队首并移除；空队列返回 false、entry 为 null。
        /// </summary>
        /// <param name="entry">取出的队首条目；空队列时为 null。</param>
        public bool TryDequeue(out QueueEntry entry)
        {
            if (_entries.Count == 0)
            {
                entry = null;
                return false;
            }

            entry = _entries[0];
            _entries.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// 返回队首但不移除；空队列返回 null。
        /// </summary>
        public QueueEntry Peek()
        {
            return _entries.Count == 0 ? null : _entries[0];
        }

        /// <summary>
        /// 写回 &lt;池根&gt;/队列.json，目录不存在就建；缩进 + 不转义中文。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public void Save(string poolRoot)
        {
            var filePath = PoolPaths.QueueFile(poolRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var items = new JsonArray();
            foreach (var entry in _entries)
            {
                items.Add(new JsonObject
                {
                    ["需求id"] = entry.RequirementIdentifier,
                    ["入队时间"] = entry.EnqueueTime,
                    ["理由"] = entry.Reason
                });
            }

            var content = new JsonObject
            {
                ["条目"] = items
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            // 以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver：
            // 队列 JSON 里的 JsonArray 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
