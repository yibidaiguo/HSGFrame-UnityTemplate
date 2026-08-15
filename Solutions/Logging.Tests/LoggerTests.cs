using System;
using System.IO;
using HSGFrame.Logging;
using Xunit;

namespace HSGFrame.Logging.Tests
{
    /// <summary>日志门面与落点的等级过滤、分发、格式化与文件落盘测试。</summary>
    public class LoggerTests
    {
        private static readonly DateTimeOffset FixedTimestamp = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        [Fact]
        public void FourLevelsAllReachMemorySink()
        {
            var sink = new MemoryLogSink();
            var logger = new Logger();
            logger.AddSink(sink);

            logger.Information("普通");
            logger.Success("成功");
            logger.Warning("警告");
            logger.Error("错误");

            Assert.Equal(4, sink.Entries.Count);
            Assert.Equal(LogLevel.Information, sink.Entries[0].Level);
            Assert.Equal(LogLevel.Success, sink.Entries[1].Level);
            Assert.Equal(LogLevel.Warning, sink.Entries[2].Level);
            Assert.Equal(LogLevel.Error, sink.Entries[3].Level);
        }

        [Fact]
        public void MinimumLevelFiltersLowerLevels()
        {
            var sink = new MemoryLogSink();
            var logger = new Logger { MinimumLevel = LogLevel.Warning };
            logger.AddSink(sink);

            logger.Information("普通");
            logger.Success("成功");
            logger.Warning("警告");
            logger.Error("错误");

            Assert.Equal(2, sink.Entries.Count);
            Assert.Equal(LogLevel.Warning, sink.Entries[0].Level);
            Assert.Equal(LogLevel.Error, sink.Entries[1].Level);
        }

        [Fact]
        public void MultipleSinksAllReceive()
        {
            var first = new MemoryLogSink();
            var second = new MemoryLogSink();
            var logger = new Logger();
            logger.AddSink(first);
            logger.AddSink(second);

            logger.Information("一条");

            Assert.Single(first.Entries);
            Assert.Single(second.Entries);
        }

        [Fact]
        public void DisposedSinkNoLongerReceives()
        {
            var sink = new MemoryLogSink();
            var logger = new Logger();
            var handle = logger.AddSink(sink);

            logger.Information("第一条");
            handle.Dispose();
            logger.Information("第二条");

            Assert.Single(sink.Entries);
        }

        [Fact]
        public void MemoryLogSinkDropsOldestWhenFull()
        {
            var sink = new MemoryLogSink(3);
            sink.Write(new LogEntry(LogLevel.Information, "一"));
            sink.Write(new LogEntry(LogLevel.Information, "二"));
            sink.Write(new LogEntry(LogLevel.Information, "三"));
            sink.Write(new LogEntry(LogLevel.Information, "四"));

            Assert.Equal(3, sink.Entries.Count);
            Assert.Equal("二", sink.Entries[0].Message);
            Assert.Equal("三", sink.Entries[1].Message);
            Assert.Equal("四", sink.Entries[2].Message);
        }

        [Fact]
        public void FormatTogglesTimestampSegment()
        {
            var entry = NewEntry(LogLevel.Warning, "消息");

            Assert.StartsWith("[03:04:05]", entry.Format(new LogFormatOptions { WriteTimestamp = true }));
            Assert.DoesNotContain("[03:04:05]", entry.Format(new LogFormatOptions { WriteTimestamp = false }));
        }

        [Fact]
        public void FormatTogglesThreadIdSegment()
        {
            var entry = NewEntry(LogLevel.Warning, "消息");

            Assert.Contains("[thread:42]", entry.Format(new LogFormatOptions { WriteThreadId = true }));
            Assert.DoesNotContain("[thread:42]", entry.Format(new LogFormatOptions { WriteThreadId = false }));
        }

        [Fact]
        public void FormatTogglesLevelSegment()
        {
            var entry = NewEntry(LogLevel.Warning, "消息");

            Assert.Contains("[Warning]", entry.Format(new LogFormatOptions { WriteLevel = true }));
            Assert.DoesNotContain("[Warning]", entry.Format(new LogFormatOptions { WriteLevel = false }));
        }

        [Fact]
        public void FormatWithAllSegmentsOffReturnsOnlyMessage()
        {
            var entry = NewEntry(LogLevel.Error, "只剩正文");

            Assert.Equal("只剩正文", entry.Format(new LogFormatOptions()));
        }

        [Fact]
        public void ThrowingSinkDoesNotPreventOthersAndThrowsAggregate()
        {
            var good = new MemoryLogSink();
            var logger = new Logger();
            logger.AddSink(new ThrowingSink());
            logger.AddSink(good);

            var exception = Assert.Throws<AggregateException>(() => logger.Information("一条"));

            Assert.Single(exception.InnerExceptions);
            Assert.Single(good.Entries);
        }

        [Fact]
        public void NullMessageDoesNotThrow()
        {
            var sink = new MemoryLogSink();
            var logger = new Logger();
            logger.AddSink(sink);

            logger.Information(null);

            Assert.Single(sink.Entries);
            Assert.Equal(string.Empty, sink.Entries[0].Message);
        }

        [Fact]
        public void FileLogSinkWritesLinesToFile()
        {
            var directory = NewTempDirectory();
            var filePath = Path.Combine(directory, "log.txt");
            try
            {
                using (var sink = new FileLogSink(filePath, new LogFormatOptions { WriteLevel = true }))
                {
                    sink.Write(new LogEntry(LogLevel.Information, "第一行", FixedTimestamp, 42));
                    sink.Write(new LogEntry(LogLevel.Error, "第二行", FixedTimestamp, 42));
                }

                var lines = File.ReadAllLines(filePath);
                Assert.Equal(2, lines.Length);
                Assert.Contains("第一行", lines[0]);
                Assert.Contains("第二行", lines[1]);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void FileLogSinkCreatesParentDirectory()
        {
            var root = NewTempDirectory();
            var filePath = Path.Combine(root, "nested", "deeper", "log.txt");
            try
            {
                using (var sink = new FileLogSink(filePath, new LogFormatOptions()))
                {
                    sink.Write(new LogEntry(LogLevel.Information, "一条", FixedTimestamp, 42));
                }

                Assert.True(File.Exists(filePath));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void FileLogSinkUnwritablePathSkipsQuietly()
        {
            var directory = NewTempDirectory();
            try
            {
                // 把目录本身当文件路径：打开目录会抛 UnauthorizedAccessException，应被静默吞掉而不抛。
                using (var sink = new FileLogSink(directory, new LogFormatOptions()))
                {
                    sink.Write(new LogEntry(LogLevel.Information, "写不进去", FixedTimestamp, 42));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static LogEntry NewEntry(LogLevel level, string message)
        {
            return new LogEntry(level, message, FixedTimestamp, 42);
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "hsg_logging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>故意抛异常的落点，用来验证「一个落点崩了其余照常收到」。</summary>
        private sealed class ThrowingSink : ILogSink
        {
            public void Write(LogEntry entry)
            {
                throw new InvalidOperationException("落点崩了");
            }
        }
    }
}
