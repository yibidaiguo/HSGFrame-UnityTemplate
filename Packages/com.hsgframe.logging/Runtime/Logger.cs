using System;
using System.Collections.Generic;

namespace HSGFrame.Logging
{
    /// <summary>日志门面：按等级过滤，再把日志分发给全部落点。</summary>
    public sealed class Logger
    {
        private readonly List<SinkRegistration> _sinks = new List<SinkRegistration>();

        /// <summary>用格式选项构造，默认最低等级是 Information。</summary>
        /// <param name="options">格式选项，null 按全部关闭处理。</param>
        public Logger(LogFormatOptions options = null)
        {
            Options = options ?? new LogFormatOptions();
        }

        /// <summary>低于这个等级的日志被丢弃。</summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

        /// <summary>当前的格式选项。</summary>
        public LogFormatOptions Options { get; }

        /// <summary>挂一个落点，返回句柄，Dispose 即摘掉。</summary>
        /// <param name="sink">要挂的落点。</param>
        public IDisposable AddSink(ILogSink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            var registration = new SinkRegistration(sink);
            _sinks.Add(registration);
            return new SinkHandle(() => Remove(registration));
        }

        /// <summary>写一条普通日志。</summary>
        /// <param name="message">正文，null 按空串处理。</param>
        public void Information(string message)
        {
            Write(LogLevel.Information, message);
        }

        /// <summary>写一条成功日志。</summary>
        /// <param name="message">正文，null 按空串处理。</param>
        public void Success(string message)
        {
            Write(LogLevel.Success, message);
        }

        /// <summary>写一条警告日志。</summary>
        /// <param name="message">正文，null 按空串处理。</param>
        public void Warning(string message)
        {
            Write(LogLevel.Warning, message);
        }

        /// <summary>写一条错误日志。</summary>
        /// <param name="message">正文，null 按空串处理。</param>
        public void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        /// <summary>按等级写一条日志。</summary>
        /// <param name="level">日志等级。</param>
        /// <param name="message">正文，null 按空串处理。</param>
        public void Write(LogLevel level, string message)
        {
            // 等级过滤在分发之前：低于门槛的日志连落点都不进。
            if (level < MinimumLevel)
            {
                return;
            }

            var entry = new LogEntry(level, message ?? string.Empty);

            // 先取快照再分发：某个落点里摘掉或新挂落点都不会在遍历中修改集合。
            var snapshot = _sinks.ToArray();
            var errors = new List<Exception>();
            foreach (var registration in snapshot)
            {
                if (registration.IsRemoved)
                {
                    continue;
                }

                try
                {
                    registration.Sink.Write(entry);
                }
                catch (Exception exception)
                {
                    // 落点可能抛出任意异常（文件、网络等），必须接住 Exception 才能让其余落点照常收到。
                    errors.Add(exception);
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(errors);
            }
        }

        private void Remove(SinkRegistration registration)
        {
            if (registration.IsRemoved)
            {
                return;
            }

            registration.IsRemoved = true;
            _sinks.Remove(registration);
        }

        /// <summary>落点登记条目，IsRemoved 标记它是否已摘掉。</summary>
        private sealed class SinkRegistration
        {
            public SinkRegistration(ILogSink sink)
            {
                Sink = sink;
            }

            /// <summary>要写的落点。</summary>
            public ILogSink Sink { get; }

            /// <summary>是否已摘掉。</summary>
            public bool IsRemoved { get; set; }
        }

        /// <summary>落点句柄：持有摘除动作，Dispose 两次安全。</summary>
        private sealed class SinkHandle : IDisposable
        {
            private readonly Action _onDispose;
            private bool _isDisposed;

            public SinkHandle(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _onDispose();
            }
        }
    }
}
