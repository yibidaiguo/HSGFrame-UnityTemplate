using HSGFrame.Logging;
using UnityEngine;

namespace Template.Presentation.Framework
{
    /// <summary>把 HSGFrame 的日志转到 Unity 控制台的落点。等级映射成 Unity 的三档。</summary>
    public sealed class UnityConsoleLogSink : ILogSink
    {
        private readonly LogFormatOptions _options;

        /// <summary>用一份格式选项构造。</summary>
        /// <param name="options">决定时间戳、线程号、等级各段要不要出现在行里。</param>
        public UnityConsoleLogSink(LogFormatOptions options)
        {
            _options = options;
        }

        /// <summary>把一条日志写进 Unity 控制台。</summary>
        /// <param name="entry">要写的日志。</param>
        public void Write(LogEntry entry)
        {
            var line = entry.Format(_options);

            // Unity 只有三档，成功与普通都落到 Log：控制台没有「成功」这一档，
            // 把它抬成 Warning 会让真正的警告淹掉。
            switch (entry.Level)
            {
                case LogLevel.Error:
                    Debug.LogError(line);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }
    }
}
