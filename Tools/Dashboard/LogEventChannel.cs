using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.Dashboard
{
    /// <summary>日志事件通道：把命令层吐出的一行行 JSON 日志广播给所有订阅者，并保留最近一段历史。</summary>
    public sealed class LogEventChannel
    {
        private const int BufferCapacity = 200;

        private readonly object _gate = new object();

        private readonly List<Action<string>> _subscribers = new List<Action<string>>();

        private readonly Queue<string> _recentLines = new Queue<string>();

        /// <summary>把一行日志投递给全部订阅者，并记入最近历史缓冲。</summary>
        /// <param name="logLine">单行日志文本，通常是一行 JSON。</param>
        public void Publish(string logLine)
        {
            if (logLine == null)
            {
                return;
            }

            Action<string>[] snapshot;
            lock (_gate)
            {
                _recentLines.Enqueue(logLine);
                while (_recentLines.Count > BufferCapacity)
                {
                    _recentLines.Dequeue();
                }

                snapshot = _subscribers.ToArray();
            }

            foreach (var subscriber in snapshot)
            {
                subscriber(logLine);
            }
        }

        /// <summary>
        /// 注册一个订阅者并返回注销句柄。
        /// 新订阅者接入时先补发最近的历史行：否则页面一刷新，SSE 连接建立后只有新行，
        /// 已经吐过的日志就再也看不到了，浏览器会白屏。
        /// </summary>
        /// <param name="onLogLine">收到一行日志时的回调。</param>
        public IDisposable Subscribe(Action<string> onLogLine)
        {
            if (onLogLine == null)
            {
                throw new ArgumentNullException(nameof(onLogLine));
            }

            string[] backlog;
            lock (_gate)
            {
                _subscribers.Add(onLogLine);
                backlog = _recentLines.ToArray();
            }

            // 补发放在锁外：回调里可能再次 Publish 或退订，锁内回调有死锁风险。
            foreach (var line in backlog)
            {
                onLogLine(line);
            }

            return new Subscription(this, onLogLine);
        }

        /// <summary>返回最近若干行日志，按时间从旧到新排列。</summary>
        /// <param name="maxCount">最多返回的行数。</param>
        public IReadOnlyList<string> RecentLines(int maxCount)
        {
            lock (_gate)
            {
                return _recentLines.Take(maxCount).ToArray();
            }
        }

        private void Unsubscribe(Action<string> onLogLine)
        {
            lock (_gate)
            {
                _subscribers.Remove(onLogLine);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly LogEventChannel _owner;

            private readonly Action<string> _onLogLine;

            private bool _disposed;

            public Subscription(LogEventChannel owner, Action<string> onLogLine)
            {
                _owner = owner;
                _onLogLine = onLogLine;
            }

            /// <summary>注销订阅，之后不再收到新行。</summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Unsubscribe(_onLogLine);
            }
        }
    }
}
