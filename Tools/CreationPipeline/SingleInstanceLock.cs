using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 单实例锁：锁文件 &lt;仓库根&gt;/_Tasks/.engine.lock 保证同时只有一个轮询实例在跑。
    /// 陈旧锁（进程已退出）会被接管——机器断电后留下一把永远解不开的锁，
    /// 会让轮询模式再也起不来，那比偶尔多跑一个实例糟得多。
    /// </summary>
    public sealed class SingleInstanceLock : IDisposable
    {
        private readonly string _lockFilePath;

        private bool _released;

        private SingleInstanceLock(string lockFilePath)
        {
            _lockFilePath = lockFilePath;
            ReleaseFailureReason = "";
        }

        /// <summary>
        /// 锁文件的路径：<paramref name="repositoryRoot"/>/_Tasks/.engine.lock。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string LockFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", ".engine.lock");
        }

        /// <summary>
        /// 尝试获取单实例锁：锁文件不存在或持有进程已退出（陈旧锁）时写入并接管返回 true；
        /// 持有进程还活着时返回 false，reason 说清是哪个进程号占着。
        /// 锁文件内容为两行：进程号与获取时刻（ISO 8601）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="instanceLock">获取到的锁实例；失败时为 null。</param>
        /// <param name="reason">结果说明文字，成功与失败都要写清。</param>
        public static bool TryAcquire(string repositoryRoot, out SingleInstanceLock instanceLock, out string reason)
        {
            instanceLock = null;
            reason = "";
            var filePath = LockFile(repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(filePath))
            {
                var ownerPid = 0;
                var alive = false;
                try
                {
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length >= 1 && int.TryParse(lines[0].Trim(), out ownerPid))
                    {
                        alive = IsProcessAlive(ownerPid);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // 锁文件读不了：当成陈旧锁接管，否则一把读不动的锁会永久卡死轮询模式。
                    alive = false;
                }

                if (alive)
                {
                    reason = $"引擎已被进程 {ownerPid} 占用（锁文件：{filePath}），这一轮不取活";
                    return false;
                }

                reason = $"接管了一把陈旧锁（原进程 {ownerPid} 已退出）：{filePath}";
            }

            try
            {
                File.WriteAllText(
                    filePath,
                    Environment.ProcessId.ToString() + Environment.NewLine + DateTimeOffset.Now.ToString("o"),
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = $"锁文件写入失败：{filePath}：{exception.Message}";
                return false;
            }

            instanceLock = new SingleInstanceLock(filePath);
            return true;
        }

        /// <summary>释放锁时的失败原因；正常释放（含从未释放过）为空串。</summary>
        public string ReleaseFailureReason { get; private set; }

        /// <summary>
        /// 删除锁文件；删不掉不抛异常（进程正在退出时抛异常没有意义），失败原因记进
        /// <see cref="ReleaseFailureReason"/>。
        /// </summary>
        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                ReleaseFailureReason = $"锁文件删除失败：{_lockFilePath}：{exception.Message}";
            }
        }

        /// <summary>进程号对应的进程是否还活着；进程不存在返回 false。查不动时保守视为活着（不接管）。</summary>
        private static bool IsProcessAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                // 进程号不存在（PID 已释放）→ 陈旧锁，可以接管。
                return false;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                // 查别的用户/别的会话的进程会抛 Win32Exception；进程对象已释放会抛 InvalidOperationException。
                // 查不动时保守视为活着，不接管——不能证明持有者已退出就不冒险抢锁。
                return true;
            }
        }
    }
}
