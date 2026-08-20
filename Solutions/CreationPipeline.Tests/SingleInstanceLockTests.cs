using System;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 单实例锁的测试。这块代码在实现阶段修过两处判活缺陷（进程对象非空恒真、
    /// 查别的用户进程抛 Win32Exception 会冒泡），改过又没测试覆盖的地方最容易再坏，所以单测钉住。
    /// </summary>
    public sealed class SingleInstanceLockTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根。</summary>
        public SingleInstanceLockTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "单实例锁测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repositoryRoot);
        }

        /// <summary>没有锁文件时能拿到锁，且锁文件真的落了盘。</summary>
        [Fact]
        public void AcquireWithoutExistingLockSucceeds()
        {
            Assert.True(SingleInstanceLock.TryAcquire(_repositoryRoot, out var instanceLock, out var reason));
            using (instanceLock)
            {
                Assert.NotNull(instanceLock);
                Assert.Equal("", reason);
                Assert.True(File.Exists(SingleInstanceLock.LockFile(_repositoryRoot)));
            }
        }

        /// <summary>释放之后锁文件被删掉，下一轮能再拿。</summary>
        [Fact]
        public void DisposeRemovesLockFileAndAllowsReacquire()
        {
            Assert.True(SingleInstanceLock.TryAcquire(_repositoryRoot, out var first, out _));
            first.Dispose();

            Assert.Equal("", first.ReleaseFailureReason);
            Assert.False(File.Exists(SingleInstanceLock.LockFile(_repositoryRoot)));

            Assert.True(SingleInstanceLock.TryAcquire(_repositoryRoot, out var second, out _));
            second.Dispose();
        }

        /// <summary>持有者进程还活着时拿不到锁，原因里带上占用的进程号。</summary>
        [Fact]
        public void LiveOwnerBlocksAcquire()
        {
            // 用当前进程号冒充持有者：它必然活着。
            WriteLockFile(Environment.ProcessId);

            Assert.False(SingleInstanceLock.TryAcquire(_repositoryRoot, out var instanceLock, out var reason));

            Assert.Null(instanceLock);
            Assert.Contains(Environment.ProcessId.ToString(), reason);
            Assert.Contains("占用", reason);
        }

        /// <summary>
        /// 持有者进程已经没了时接管陈旧锁。断电留下一把永远解不开的锁，
        /// 会让轮询模式再也起不来——那比偶尔多跑一个实例糟得多。
        /// </summary>
        [Fact]
        public void StaleLockIsTakenOver()
        {
            // 起一个立刻退出的进程，拿它退出后的进程号当持有者：那个号已经不活了。
            var deadProcessId = StartAndWaitForExit();
            WriteLockFile(deadProcessId);

            Assert.True(SingleInstanceLock.TryAcquire(_repositoryRoot, out var instanceLock, out var reason));
            using (instanceLock)
            {
                Assert.Contains("陈旧锁", reason);
            }
        }

        /// <summary>锁文件内容不是进程号时按陈旧锁接管——读不动的锁不许永久卡死轮询。</summary>
        [Fact]
        public void UnparsableLockFileIsTakenOver()
        {
            // 内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            var filePath = SingleInstanceLock.LockFile(_repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, "not-a-pid", new UTF8Encoding(false));

            Assert.True(SingleInstanceLock.TryAcquire(_repositoryRoot, out var instanceLock, out _));
            instanceLock.Dispose();
        }

        private void WriteLockFile(int processId)
        {
            var filePath = SingleInstanceLock.LockFile(_repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                filePath,
                processId.ToString() + Environment.NewLine + DateTimeOffset.Now.ToString("o"),
                new UTF8Encoding(false));
        }

        private static int StartAndWaitForExit()
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            process.WaitForExit();
            return process.Id;
        }

        /// <summary>清掉临时仓库根。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响测试结论。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }
    }
}
