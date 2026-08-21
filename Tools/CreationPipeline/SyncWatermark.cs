using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>某个 driver 的同步水位：最后修改时间与最后记录 id，两个字段都可为空串。</summary>
    public sealed class SyncWatermarkEntry
    {
        /// <summary>
        /// 构造一条水位条目。
        /// </summary>
        /// <param name="moment">最后修改时间，ISO 8601 字符串；未记录时为空串。</param>
        /// <param name="recordIdentifier">最后记录 id；未记录时为空串。</param>
        internal SyncWatermarkEntry(string moment, string recordIdentifier)
        {
            Moment = moment ?? "";
            RecordIdentifier = recordIdentifier ?? "";
        }

        /// <summary>最后修改时间，ISO 8601 字符串；未记录时为空串。</summary>
        public string Moment { get; }

        /// <summary>最后记录 id；未记录时为空串。</summary>
        public string RecordIdentifier { get; }
    }

    /// <summary>
    /// 一次水位前进/回退的结果：是否成功、是否真的前进了、失败原因与写后的条目。
    /// 与 ConflictResolutionResult 同款：内部构造 + 静态工厂，命令层只读结论。
    /// </summary>
    public sealed class WatermarkAdvanceResult
    {
        /// <summary>
        /// 构造一次水位操作结果。
        /// </summary>
        /// <param name="succeeded">是否成功。</param>
        /// <param name="advanced">是否真的前进了；幂等重放（时间相同）为 false。</param>
        /// <param name="failureReason">失败原因，成功时为空串。</param>
        /// <param name="entry">操作后的水位条目，失败时为零水位条目。</param>
        internal WatermarkAdvanceResult(bool succeeded, bool advanced, string failureReason, SyncWatermarkEntry entry)
        {
            Succeeded = succeeded;
            Advanced = advanced;
            FailureReason = failureReason;
            Entry = entry;
        }

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; }

        /// <summary>是否真的前进了；幂等重放（给的时间与当前水位相同）为 false。</summary>
        public bool Advanced { get; }

        /// <summary>失败原因，成功时为空串。</summary>
        public string FailureReason { get; }

        /// <summary>操作后的水位条目，失败时为零水位条目。</summary>
        public SyncWatermarkEntry Entry { get; }

        /// <summary>构造一个成功且前进的结果。</summary>
        /// <param name="entry">写后的水位条目。</param>
        internal static WatermarkAdvanceResult Success(SyncWatermarkEntry entry)
        {
            return new WatermarkAdvanceResult(true, true, "", entry);
        }

        /// <summary>构造一个成功但水位没动（幂等重放）的结果。</summary>
        /// <param name="entry">当前水位条目。</param>
        internal static WatermarkAdvanceResult Unchanged(SyncWatermarkEntry entry)
        {
            return new WatermarkAdvanceResult(true, false, "", entry);
        }

        /// <summary>构造一个失败的结果。</summary>
        /// <param name="reason">失败原因。</param>
        internal static WatermarkAdvanceResult Failed(string reason)
        {
            return new WatermarkAdvanceResult(false, false, reason, new SyncWatermarkEntry("", ""));
        }
    }

    /// <summary>
    /// 同步水位表（Tools/CreationPipeline/Config/sync-watermark.json）：按 driver 名分键，每个 driver 记一条
    /// 「最后修改时间 + 最后记录 id」。读不到进度时返回空水位——空水位的语义是「全量拉」：
    /// 重拉一遍最安全（幂等会把重复的挡掉），不是不拉。这与决策 10（配置缺失返回值守）
    /// 方向相反是**故意的**——水位缺了最坏结果是多拉，值守缺了最坏结果是永不自动，两件事的
    /// 代价不一样。
    /// </summary>
    public sealed class SyncWatermark
    {
        private readonly IReadOnlyDictionary<string, SyncWatermarkEntry> _entries;

        /// <summary>
        /// 构造一份水位视图。
        /// </summary>
        /// <param name="entries">全部 driver 的水位，键 = driver 名。</param>
        /// <param name="loadFailureReason">加载失败原因，正常（含文件不存在）为空串。</param>
        internal SyncWatermark(IReadOnlyDictionary<string, SyncWatermarkEntry> entries, string loadFailureReason)
        {
            _entries = entries;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>全部 driver 的水位，键 = driver 名。</summary>
        public IReadOnlyDictionary<string, SyncWatermarkEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>加载失败原因；正常（含文件不存在）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 从仓库根加载同步水位：文件不存在返回空水位 + 原因空串（空水位 = 全量拉）；
        /// 顶层不是对象或整份 JSON 坏返回空水位并写清原因，不许静默当成空（决策 42）；
        /// 单个 driver 的条目坏时跳过那一个、原因累加，其余照常加载。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static SyncWatermark Load(string repositoryRoot)
        {
            var filePath = PipelinePaths.SyncWatermarkFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new SyncWatermark(new Dictionary<string, SyncWatermarkEntry>(StringComparer.Ordinal), "");
            }

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new SyncWatermark(
                    new Dictionary<string, SyncWatermarkEntry>(StringComparer.Ordinal),
                    $"同步水位读不了：{exception.Message}");
            }

            try
            {
                var root = JsonNode.Parse(text);
                if (root is not JsonObject topObject)
                {
                    return new SyncWatermark(
                        new Dictionary<string, SyncWatermarkEntry>(StringComparer.Ordinal),
                        "同步水位顶层必须是 JSON 对象");
                }

                var entries = new Dictionary<string, SyncWatermarkEntry>(StringComparer.Ordinal);
                var failures = new List<string>();
                foreach (var pair in topObject)
                {
                    if (pair.Value is not JsonObject entryObject)
                    {
                        failures.Add($"driver「{pair.Key}」的条目不是对象，已跳过");
                        continue;
                    }

                    if (!TryReadString(entryObject, "最后修改时间", out var moment))
                    {
                        failures.Add($"driver「{pair.Key}」的最后修改时间不是字符串，已跳过");
                        continue;
                    }

                    var recordIdentifier = ReadStringOrEmpty(entryObject, "最后记录id");
                    entries[pair.Key] = new SyncWatermarkEntry(moment, recordIdentifier);
                }

                var reason = failures.Count == 0 ? "" : string.Join("；", failures);
                return new SyncWatermark(entries, reason);
            }
            catch (JsonException exception)
            {
                return new SyncWatermark(
                    new Dictionary<string, SyncWatermarkEntry>(StringComparer.Ordinal),
                    $"同步水位 JSON 解析失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 查某个 driver 的水位；查不到返回一条零水位条目（两个字段都是空串），不返回 null——
        /// 调用方不必分叉「没记过」和「记过但空」，都是全量拉。
        /// </summary>
        /// <param name="driverName">driver 名。</param>
        public SyncWatermarkEntry Find(string driverName)
        {
            if (driverName != null && _entries.TryGetValue(driverName, out var entry))
            {
                return entry;
            }

            return new SyncWatermarkEntry("", "");
        }

        /// <summary>
        /// 前进水位：只许前进，不许后退。新时间早于既有 → 失败（文案里点明当前与给定两个时间，
        /// 并指路 Rewind）；等于既有 → 成功但 Advanced 为 false（幂等重放不算错也不算前进）。
        /// 写盘只改这一个 driver 的键，其余 driver 的键一字不动，整体写回。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="moment">新的最后修改时间，ISO 8601 字符串。</param>
        /// <param name="recordIdentifier">新的最后记录 id。</param>
        public static WatermarkAdvanceResult Advance(string repositoryRoot, string driverName, string moment, string recordIdentifier)
        {
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return WatermarkAdvanceResult.Failed("driver 名为空：必须给出 driver 名");
            }

            if (!TryParseMoment(moment, out var parsedMoment))
            {
                return WatermarkAdvanceResult.Failed($"时间戳解析不了：{moment}");
            }

            var filePath = PipelinePaths.SyncWatermarkFile(repositoryRoot);
            JsonObject root;
            try
            {
                root = ReadRootForWrite(filePath, out var readFailure);
                if (root == null)
                {
                    return WatermarkAdvanceResult.Failed(readFailure);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return WatermarkAdvanceResult.Failed($"同步水位读不了：{exception.Message}");
            }

            var existingMoment = ReadEntryMoment(root, driverName);
            if (existingMoment.Length > 0)
            {
                if (!TryParseMoment(existingMoment, out var existingParsed))
                {
                    return WatermarkAdvanceResult.Failed(
                        $"既有水位时间戳解析不了：{existingMoment}；无法判断前进还是后退，要重拉请用 Rewind");
                }

                if (parsedMoment < existingParsed)
                {
                    return WatermarkAdvanceResult.Failed(
                        $"水位只许前进：driver「{driverName}」当前水位是 {existingMoment}，给的是 {moment}；要重拉请用 Rewind");
                }

                if (parsedMoment == existingParsed)
                {
                    return WatermarkAdvanceResult.Unchanged(
                        new SyncWatermarkEntry(existingMoment, ReadEntryRecordIdentifier(root, driverName)));
                }
            }

            WriteEntry(root, filePath, driverName, moment, recordIdentifier);
            return WatermarkAdvanceResult.Success(new SyncWatermarkEntry(moment, recordIdentifier));
        }

        /// <summary>
        /// 回退水位：显式重拉的正门，不做前进检查，直接写。Advance 拦的是「不小心后退」，
        /// Rewind 放行的是「故意重拉」——两个入口分工不同，命令层把 Rewind 只暴露给人操作。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="moment">回退到的最后修改时间，ISO 8601 字符串。</param>
        /// <param name="recordIdentifier">回退到的最后记录 id。</param>
        public static WatermarkAdvanceResult Rewind(string repositoryRoot, string driverName, string moment, string recordIdentifier)
        {
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return WatermarkAdvanceResult.Failed("driver 名为空：必须给出 driver 名");
            }

            var filePath = PipelinePaths.SyncWatermarkFile(repositoryRoot);
            JsonObject root;
            try
            {
                root = ReadRootForWrite(filePath, out var readFailure);
                if (root == null)
                {
                    return WatermarkAdvanceResult.Failed(readFailure);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return WatermarkAdvanceResult.Failed($"同步水位读不了：{exception.Message}");
            }

            WriteEntry(root, filePath, driverName, moment, recordIdentifier);
            return WatermarkAdvanceResult.Success(new SyncWatermarkEntry(moment, recordIdentifier));
        }

        /// <summary>按 ISO 8601 解析时刻，带偏移往返保留（RoundtripKind）。</summary>
        private static bool TryParseMoment(string moment, out DateTimeOffset parsed)
        {
            return DateTimeOffset.TryParse(
                moment,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out parsed);
        }

        /// <summary>读水位文件为顶层对象；文件不存在给空对象。文件存在但顶层不是对象时返回 null 并给出原因。</summary>
        private static JsonObject ReadRootForWrite(string filePath, out string failureReason)
        {
            failureReason = "";
            if (!File.Exists(filePath))
            {
                return new JsonObject();
            }

            var parsed = JsonNode.Parse(File.ReadAllText(filePath));
            if (parsed is not JsonObject root)
            {
                failureReason = "同步水位顶层不是对象，无法写入";
                return null;
            }

            return root;
        }

        /// <summary>读某个 driver 条目里的最后修改时间；没有该 driver 或条目不是对象给空串。</summary>
        private static string ReadEntryMoment(JsonObject root, string driverName)
        {
            if (root.TryGetPropertyValue(driverName, out var node) && node is JsonObject entryObject)
            {
                return ReadStringOrEmpty(entryObject, "最后修改时间");
            }

            return "";
        }

        /// <summary>读某个 driver 条目里的最后记录 id；没有该 driver 或条目不是对象给空串。</summary>
        private static string ReadEntryRecordIdentifier(JsonObject root, string driverName)
        {
            if (root.TryGetPropertyValue(driverName, out var node) && node is JsonObject entryObject)
            {
                return ReadStringOrEmpty(entryObject, "最后记录id");
            }

            return "";
        }

        /// <summary>把某个 driver 的条目整键替换后写回水位文件，目录不存在先创建。</summary>
        private static void WriteEntry(JsonObject root, string filePath, string driverName, string moment, string recordIdentifier)
        {
            root[driverName] = new JsonObject
            {
                ["最后修改时间"] = moment,
                ["最后记录id"] = recordIdentifier
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
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

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonObject 会抛），缩进 + 不转义中文。</summary>
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
