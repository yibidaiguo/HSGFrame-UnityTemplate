using System;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>唤醒事件源的行为测试：目录不存在、空目录、多个信号按序数序取第一个、消费归档与撞名追加后缀。</summary>
    public sealed class WakeSignalSourceTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根。</summary>
        public WakeSignalSourceTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "唤醒信号测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repositoryRoot);
        }

        /// <summary>唤醒目录不存在时 HasSignal=false、Reason 写明「唤醒目录不存在」，且不建目录。</summary>
        [Fact]
        public void MissingDirectoryReportsNoSignalAndDoesNotCreate()
        {
            var poll = WakeSignalSource.Poll(_repositoryRoot);

            Assert.False(poll.HasSignal);
            Assert.Equal("", poll.SignalFilePath);
            Assert.Equal("唤醒目录不存在", poll.Reason);
            Assert.False(Directory.Exists(WakeSignalSource.SignalDirectory(_repositoryRoot)));
        }

        /// <summary>唤醒目录存在但空时 HasSignal=false，Reason 写明没有待处理信号。</summary>
        [Fact]
        public void EmptyDirectoryReportsNoSignal()
        {
            Directory.CreateDirectory(WakeSignalSource.SignalDirectory(_repositoryRoot));

            var poll = WakeSignalSource.Poll(_repositoryRoot);

            Assert.False(poll.HasSignal);
            Assert.Equal("", poll.SignalFilePath);
            Assert.Equal("唤醒目录没有待处理信号", poll.Reason);
        }

        /// <summary>多个信号按文件名序数序取第一个；序数序下大写字母排在所有小写字母之前。</summary>
        [Fact]
        public void MultipleSignalsPickFirstByOrdinalFileName()
        {
            var directory = WakeSignalSource.SignalDirectory(_repositoryRoot);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "a-1.json"), "{}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "Z-9.json"), "{}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "b-2.json"), "{}", new UTF8Encoding(false));

            var poll = WakeSignalSource.Poll(_repositoryRoot);

            Assert.True(poll.HasSignal);
            Assert.Equal("Z-9.json", Path.GetFileName(poll.SignalFilePath));
        }

        /// <summary>Consume 之后原位置没有了、归档里有同一份文件。</summary>
        [Fact]
        public void ConsumeMovesSignalIntoArchive()
        {
            var directory = WakeSignalSource.SignalDirectory(_repositoryRoot);
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "wake-1.json");
            File.WriteAllText(sourcePath, "{}", new UTF8Encoding(false));

            var archivedPath = WakeSignalSource.Consume(_repositoryRoot, sourcePath);

            Assert.False(File.Exists(sourcePath));
            Assert.True(File.Exists(archivedPath));
            Assert.Equal("wake-1.json", Path.GetFileName(archivedPath));
            Assert.StartsWith(WakeSignalSource.ArchiveDirectory(_repositoryRoot), archivedPath, StringComparison.Ordinal);
        }

        /// <summary>归档撞名时在扩展名前追加 -2、-3……直到不撞名。</summary>
        [Fact]
        public void ConsumeAddsSuffixWhenArchiveNameTaken()
        {
            var directory = WakeSignalSource.SignalDirectory(_repositoryRoot);
            Directory.CreateDirectory(directory);
            var archiveDirectory = WakeSignalSource.ArchiveDirectory(_repositoryRoot);
            Directory.CreateDirectory(archiveDirectory);

            // 归档里已有 wake-1.json 与 wake-1-2.json，新消费的应该落到 wake-1-3.json。
            File.WriteAllText(Path.Combine(archiveDirectory, "wake-1.json"), "old", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(archiveDirectory, "wake-1-2.json"), "old2", new UTF8Encoding(false));
            var sourcePath = Path.Combine(directory, "wake-1.json");
            File.WriteAllText(sourcePath, "{}", new UTF8Encoding(false));

            var archivedPath = WakeSignalSource.Consume(_repositoryRoot, sourcePath);

            Assert.Equal("wake-1-3.json", Path.GetFileName(archivedPath));
            Assert.False(File.Exists(sourcePath));
            Assert.True(File.Exists(archivedPath));
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
