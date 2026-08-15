using System.Collections.Generic;

namespace HSGFrame.Logging
{
    /// <summary>日志落点：一条日志最终写到哪里由实现决定（内存、文件、引擎控制台）。</summary>
    public interface ILogSink
    {
        /// <summary>写一条日志。</summary>
        /// <param name="entry">要写的日志。</param>
        void Write(LogEntry entry);
    }

    /// <summary>写进内存清单的落点，测试与「运行时日志窗」都用它。带容量上限，超了丢最旧的。</summary>
    public sealed class MemoryLogSink : ILogSink
    {
        private readonly int _capacity;
        private readonly List<LogEntry> _entries = new List<LogEntry>();

        /// <summary>用容量构造，默认 200 条。</summary>
        /// <param name="capacity">容量上限，小于 1 时按 1 处理。</param>
        public MemoryLogSink(int capacity = 200)
        {
            _capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary>当前存着的日志，按写入先后排列。</summary>
        public IReadOnlyList<LogEntry> Entries => _entries;

        /// <summary>写一条日志，超出容量时丢掉最旧的那条。</summary>
        /// <param name="entry">要写的日志。</param>
        public void Write(LogEntry entry)
        {
            if (_entries.Count >= _capacity)
            {
                _entries.RemoveAt(0);
            }

            _entries.Add(entry);
        }

        /// <summary>清空内存里的全部日志。</summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
