using System;
using System.IO;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次信号目录轮询的结果：有没有信号、信号文件路径与原因说明。</summary>
    public sealed class SignalPoll
    {
        /// <summary>
        /// 构造一次轮询结果。
        /// </summary>
        /// <param name="hasSignal">目录里有没有待处理信号。</param>
        /// <param name="signalFilePath">待处理信号文件路径，没有信号时为空串。</param>
        /// <param name="reason">结果说明文字，永远非空。</param>
        public SignalPoll(bool hasSignal, string signalFilePath, string reason)
        {
            HasSignal = hasSignal;
            SignalFilePath = signalFilePath ?? "";
            Reason = reason ?? "";
        }

        /// <summary>目录里有没有待处理信号。</summary>
        public bool HasSignal { get; }

        /// <summary>待处理信号文件路径；没有信号时为空串。</summary>
        public string SignalFilePath { get; }

        /// <summary>结果说明文字，永远非空。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 「一个目录当队列」的公共实现：直属 *.json 是待处理项，处理完**移动**到归档子目录、绝不删除
    /// （决策 7：留证据，靠幂等挡重复）。
    /// 唤醒源（<see cref="WakeSignalSource"/>）与会话源（<see cref="ConversationSignalSource"/>）
    /// 共用这一份——两边的语义完全一样，抄两遍迟早分叉。
    /// </summary>
    public static class SignalDirectoryQueue
    {
        /// <summary>
        /// 扫一遍目录：直属 *.json（不递归，归档子目录天然排除），按文件名序数序取第一个。
        /// 一个都没有时 HasSignal=false 并写明原因；目录不存在同样 HasSignal=false 且不建目录。
        /// </summary>
        /// <param name="directory">信号目录。</param>
        /// <param name="emptyReason">目录存在但没有信号时的说明。</param>
        /// <param name="missingReason">目录不存在时的说明。</param>
        public static SignalPoll Poll(string directory, string emptyReason, string missingReason)
        {
            if (!Directory.Exists(directory))
            {
                return new SignalPoll(false, "", missingReason);
            }

            // 按文件名序数序排序，不许按时间排——时间在门禁里不稳定（决策 58 同源）。
            var candidates = Directory
                .GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                return new SignalPoll(false, "", emptyReason);
            }

            return new SignalPoll(true, candidates[0], $"发现信号：{candidates[0]}");
        }

        /// <summary>
        /// 消费一个信号：把文件移动到归档目录；归档同名时在扩展名前追加 -2、-3……直到不撞名。
        /// 不删除文件（决策 7）；移动失败返回空串，不抛。
        /// </summary>
        /// <param name="archiveDirectory">归档目录。</param>
        /// <param name="signalFilePath">要消费的信号文件绝对路径。</param>
        /// <returns>归档后的绝对路径；移动失败时为空串。</returns>
        public static string Consume(string archiveDirectory, string signalFilePath)
        {
            try
            {
                Directory.CreateDirectory(archiveDirectory);

                var fileName = Path.GetFileName(signalFilePath);
                var destination = Path.Combine(archiveDirectory, fileName);
                var suffix = 2;
                while (File.Exists(destination))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var extension = Path.GetExtension(fileName);
                    destination = Path.Combine(archiveDirectory, $"{name}-{suffix}{extension}");
                    suffix++;
                }

                File.Move(signalFilePath, destination);
                return destination;
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
    }

    /// <summary>
    /// 会话事件源：&lt;仓库根&gt;/_Tasks/conversations 下的 *.json 即一条待回话的消息。
    /// **与唤醒目录刻意分开两个目录**（决策 95）：唤醒信号的消费者是引擎守护，
    /// 会话消息的消费者是助手常驻会话，两个消费者盯同一个目录必然互相抢信号。
    /// 助手写完需求草稿之后**自己往唤醒目录投一个信号**，链路仍然接得上。
    /// </summary>
    public static class ConversationSignalSource
    {
        /// <summary>会话信号目录：&lt;仓库根&gt;/_Tasks/conversations。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SignalDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations");
        }

        /// <summary>已处理会话归档目录：&lt;仓库根&gt;/_Tasks/conversations/processed。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ArchiveDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations", "processed");
        }

        /// <summary>扫一遍会话目录，取文件名序数序第一个。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static SignalPoll Poll(string repositoryRoot)
        {
            return SignalDirectoryQueue.Poll(
                SignalDirectory(repositoryRoot),
                "会话目录没有待处理消息",
                "会话目录不存在");
        }

        /// <summary>消费一条会话消息：移动到归档目录，不删除。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="signalFilePath">要消费的信号文件绝对路径。</param>
        public static string Consume(string repositoryRoot, string signalFilePath)
        {
            return SignalDirectoryQueue.Consume(ArchiveDirectory(repositoryRoot), signalFilePath);
        }
    }
}
