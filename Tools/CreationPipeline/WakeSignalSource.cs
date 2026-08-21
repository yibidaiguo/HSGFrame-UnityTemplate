using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次唤醒目录轮询的结果：有没有信号、信号文件路径与原因说明。</summary>
    public sealed class WakeSignalPoll
    {
        /// <summary>
        /// 构造一次唤醒目录轮询结果。
        /// </summary>
        /// <param name="hasSignal">唤醒目录里有没有待处理信号。</param>
        /// <param name="signalFilePath">待处理信号文件路径，没有信号时为空串。</param>
        /// <param name="reason">结果说明文字，永远非空。</param>
        public WakeSignalPoll(bool hasSignal, string signalFilePath, string reason)
        {
            HasSignal = hasSignal;
            SignalFilePath = signalFilePath ?? "";
            Reason = reason ?? "";
        }

        /// <summary>唤醒目录里有没有待处理信号。</summary>
        public bool HasSignal { get; }

        /// <summary>待处理信号文件路径；没有信号时为空串。</summary>
        public string SignalFilePath { get; }

        /// <summary>结果说明文字，永远非空。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 文件投递式唤醒事件源：&lt;仓库根&gt;/_Tasks/唤醒 下的 *.json 即唤醒信号。
    /// 信号只移动归档、绝不删除（决策 7：处理后不删，留证据）；事件只当「提前唤醒」，
    /// 判定逻辑仍与轮询同一条（决策 56：处理逻辑与轮询同一条，防漏）。
    /// </summary>
    public static class WakeSignalSource
    {
        /// <summary>唤醒信号目录：&lt;仓库根&gt;/_Tasks/唤醒。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SignalDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "唤醒");
        }

        /// <summary>已处理信号归档目录：&lt;仓库根&gt;/_Tasks/唤醒/已处理。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ArchiveDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "唤醒", "已处理");
        }

        /// <summary>
        /// 扫一遍唤醒目录：直属 *.json（不递归，已处理子目录天然排除），按文件名序数序取第一个。
        /// 一个都没有时 HasSignal=false 并写明原因；目录不存在同样 HasSignal=false 且不建目录。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static WakeSignalPoll Poll(string repositoryRoot)
        {
            var poll = SignalDirectoryQueue.Poll(
                SignalDirectory(repositoryRoot),
                "唤醒目录没有待处理信号",
                "唤醒目录不存在");
            return new WakeSignalPoll(poll.HasSignal, poll.SignalFilePath, poll.Reason);
        }

        /// <summary>
        /// 消费一个信号：把信号文件移动到归档目录；归档同名时在扩展名前追加 -2、-3……直到不撞名。
        /// 不删除文件（决策 7）；移动失败返回空串，不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="signalFilePath">要消费的信号文件绝对路径。</param>
        /// <returns>归档后的绝对路径；移动失败时为空串。</returns>
        public static string Consume(string repositoryRoot, string signalFilePath)
        {
            return SignalDirectoryQueue.Consume(ArchiveDirectory(repositoryRoot), signalFilePath);
        }

        /// <summary>
        /// 投一个唤醒信号：本进程自己产的事件（如助手写完了需求草稿）也要能叫醒引擎。
        /// 文件名带时间戳与事件名，同一秒多个信号靠毫秒段与去重后缀区分，不互相覆盖。
        /// 写失败返回空串、不抛——投信号失败不该把调用方那一轮弄崩（决策 83 同源）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="eventKind">事件名，进文件名，非 ASCII 与路径分隔符会被替换掉。</param>
        /// <param name="detail">事件明细，原样进信号的「载荷」。</param>
        /// <param name="now">当前时间，由调用方给（门禁里要可复现，决策 58）。</param>
        /// <returns>写成的信号文件绝对路径；失败时为空串。</returns>
        public static string Emit(string repositoryRoot, string eventKind, JsonObject detail, DateTimeOffset now)
        {
            try
            {
                var directory = SignalDirectory(repositoryRoot);
                Directory.CreateDirectory(directory);

                var safeKind = SanitizeForFileName(string.IsNullOrWhiteSpace(eventKind) ? "信号" : eventKind);
                var stamp = now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'-'fff");
                var candidate = Path.Combine(directory, $"{stamp}-{safeKind}.json");
                var suffix = 2;
                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(directory, $"{stamp}-{safeKind}-{suffix}.json");
                    suffix++;
                }

                var body = new JsonObject
                {
                    ["来源"] = "引擎内部",
                    ["事件"] = eventKind ?? "",
                    ["收到时间"] = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    ["载荷"] = detail ?? new JsonObject()
                };

                var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                File.WriteAllText(candidate, JsonSerializer.Serialize(body, options), new UTF8Encoding(false));
                return candidate;
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                return "";
            }
        }

        /// <summary>把事件名里的路径分隔符与非法文件名字符换成下划线，防路径穿越。</summary>
        private static string SanitizeForFileName(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();
            foreach (var ch in text)
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            var result = builder.ToString().Trim();
            return result.Length == 0 ? "信号" : result;
        }
    }
}
