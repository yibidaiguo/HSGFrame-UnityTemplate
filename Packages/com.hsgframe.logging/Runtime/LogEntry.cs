using System;
using System.Text;
using System.Threading;

namespace HSGFrame.Logging
{
    /// <summary>日志等级。四个等级沿用旧实现的语义：普通、成功、警告、错误。</summary>
    public enum LogLevel
    {
        /// <summary>普通信息。</summary>
        Information = 0,

        /// <summary>成功。</summary>
        Success = 1,

        /// <summary>警告。</summary>
        Warning = 2,

        /// <summary>错误。</summary>
        Error = 3
    }

    /// <summary>一条日志：等级、正文、发生时刻与线程号。</summary>
    public sealed class LogEntry
    {
        /// <summary>用当前时刻与当前线程号构造一条日志。</summary>
        /// <param name="level">日志等级。</param>
        /// <param name="message">正文，null 按空串处理。</param>
        public LogEntry(LogLevel level, string message)
            : this(level, message, DateTimeOffset.Now, Thread.CurrentThread.ManagedThreadId)
        {
        }

        /// <summary>用指定时刻与线程号构造一条日志，测试里用它钉住格式化输出。</summary>
        /// <param name="level">日志等级。</param>
        /// <param name="message">正文，null 按空串处理。</param>
        /// <param name="timestamp">发生时刻。</param>
        /// <param name="threadId">线程号。</param>
        public LogEntry(LogLevel level, string message, DateTimeOffset timestamp, int threadId)
        {
            Level = level;
            Message = message ?? string.Empty;
            Timestamp = timestamp;
            ThreadId = threadId;
        }

        /// <summary>日志等级。</summary>
        public LogLevel Level { get; }

        /// <summary>正文。</summary>
        public string Message { get; }

        /// <summary>发生时刻。</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>线程号。</summary>
        public int ThreadId { get; }

        /// <summary>按当前格式选项把这条日志拼成一行文本。</summary>
        /// <param name="options">格式选项，null 按全部关闭处理。</param>
        public string Format(LogFormatOptions options)
        {
            var builder = new StringBuilder();
            if (options != null)
            {
                if (options.WriteTimestamp)
                {
                    builder.Append('[').Append(Timestamp.ToString("HH:mm:ss")).Append("] ");
                }

                if (options.WriteThreadId)
                {
                    builder.Append("[thread:").Append(ThreadId).Append("] ");
                }

                if (options.WriteLevel)
                {
                    builder.Append('[').Append(Level.ToString()).Append("] ");
                }
            }

            builder.Append(Message);
            return builder.ToString();
        }
    }

    /// <summary>格式化选项：各段要不要出现在行里。</summary>
    public sealed class LogFormatOptions
    {
        /// <summary>是否写入发生时刻。</summary>
        public bool WriteTimestamp { get; set; }

        /// <summary>是否写入线程号。</summary>
        public bool WriteThreadId { get; set; }

        /// <summary>是否写入日志等级。</summary>
        public bool WriteLevel { get; set; }
    }
}
