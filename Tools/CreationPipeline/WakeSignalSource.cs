using System;
using System.IO;
using System.Linq;

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
            var directory = SignalDirectory(repositoryRoot);
            if (!Directory.Exists(directory))
            {
                return new WakeSignalPoll(false, "", "唤醒目录不存在");
            }

            // 按文件名序数序排序，不许按时间排——时间在门禁里不稳定（决策 58 同源）。
            var candidates = Directory
                .GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                return new WakeSignalPoll(false, "", "唤醒目录没有待处理信号");
            }

            return new WakeSignalPoll(true, candidates[0], $"发现唤醒信号：{candidates[0]}");
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
            try
            {
                var archiveDirectory = ArchiveDirectory(repositoryRoot);
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
}
