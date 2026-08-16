using HSGFrame.Logging;
using UnityEngine;

namespace Template.Boot
{
    /// <summary>启动装配这一侧的控制台落点：把 HSGFrame 的日志转到 Unity 控制台。</summary>
    /// <remarks>
    /// 与 <c>Template.View.UnityConsoleLogSink</c> 内容几乎一样，仍然各留一份：
    /// 那一份在热更程序集 Game.View 里，本程序集是 AOT 的 Game.Boot，引用不到它，
    /// 而启动装配从第一步起就得有日志——日志落点本来就是它装配的第一件东西。
    /// </remarks>
    public sealed class BootConsoleLogSink : ILogSink
    {
        private readonly LogFormatOptions _options;

        /// <summary>用一份格式选项构造。</summary>
        /// <param name="options">决定时间戳、线程号、等级各段要不要出现在行里。</param>
        public BootConsoleLogSink(LogFormatOptions options)
        {
            _options = options;
        }

        /// <summary>把一条日志写进 Unity 控制台。</summary>
        /// <param name="entry">要写的日志。</param>
        public void Write(LogEntry entry)
        {
            var line = entry.Format(_options);

            // Unity 只有三档，成功与普通都落到 Log：把「成功」抬成 Warning 会让真正的警告淹掉。
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
