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

        private readonly List<Action<long, string>> _numberedSubscribers = new List<Action<long, string>>();

        private readonly Queue<LoggedLine> _recentLines = new Queue<LoggedLine>();

        // 每行日志的单调递增编号，从 1 开始，供 SSE 断点补发对齐用。
        private long _nextEventId = 1;

        /// <summary>把一行日志投递给全部订阅者，并记入最近历史缓冲。</summary>
        /// <param name="logLine">单行日志文本，通常是一行 JSON。</param>
        public void Publish(string logLine)
        {
            if (logLine == null)
            {
                return;
            }

            Action<string>[] snapshot;
            Action<long, string>[] numberedSnapshot;
            long eventId;
            lock (_gate)
            {
                eventId = _nextEventId++;
                _recentLines.Enqueue(new LoggedLine(eventId, logLine));
                while (_recentLines.Count > BufferCapacity)
                {
                    _recentLines.Dequeue();
                }

                snapshot = _subscribers.ToArray();
                numberedSnapshot = _numberedSubscribers.ToArray();
            }

            foreach (var subscriber in snapshot)
            {
                subscriber(logLine);
            }

            foreach (var subscriber in numberedSnapshot)
            {
                subscriber(eventId, logLine);
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
                backlog = _recentLines.Select(line => line.Line).ToArray();
            }

            // 补发放在锁外：回调里可能再次 Publish 或退订，锁内回调有死锁风险。
            foreach (var line in backlog)
            {
                onLogLine(line);
            }

            return new Subscription(() => RemoveSubscriber(_subscribers, onLogLine));
        }

        /// <summary>
        /// 注册一个带编号的订阅者并返回注销句柄。
        /// 补发历史时只补编号大于 afterEventId 的行；afterEventId 为 null 时补发全部历史。
        /// </summary>
        /// <param name="onLogLine">收到一行日志时的回调，第一参数是该行的单调递增编号。</param>
        /// <param name="afterEventId">断点编号，补发编号大于它的历史行；null 表示没有断点。</param>
        public IDisposable Subscribe(Action<long, string> onLogLine, long? afterEventId)
        {
            if (onLogLine == null)
            {
                throw new ArgumentNullException(nameof(onLogLine));
            }

            LoggedLine[] backlog;
            lock (_gate)
            {
                _numberedSubscribers.Add(onLogLine);
                backlog = _recentLines
                    .Where(line => !afterEventId.HasValue || line.Id > afterEventId.Value)
                    .ToArray();
            }

            // 补发放在锁外：回调里可能再次 Publish 或退订，锁内回调有死锁风险。
            foreach (var line in backlog)
            {
                onLogLine(line.Id, line.Line);
            }

            return new Subscription(() => RemoveSubscriber(_numberedSubscribers, onLogLine));
        }

        /// <summary>返回最近若干行日志，按时间从旧到新排列。</summary>
        /// <param name="maxCount">最多返回的行数。</param>
        public IReadOnlyList<string> RecentLines(int maxCount)
        {
            lock (_gate)
            {
                return _recentLines.Select(line => line.Line).Take(maxCount).ToArray();
            }
        }

        private void RemoveSubscriber<T>(List<T> subscribers, T callback)
        {
            lock (_gate)
            {
                subscribers.Remove(callback);
            }
        }

        /// <summary>历史缓冲里的一条日志：单调递增编号与文本行。</summary>
        private readonly struct LoggedLine
        {
            public LoggedLine(long id, string line)
            {
                Id = id;
                Line = line;
            }

            public long Id { get; }

            public string Line { get; }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _unsubscribe;

            private bool _disposed;

            public Subscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            /// <summary>注销订阅，之后不再收到新行。</summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _unsubscribe();
            }
        }
    }
}
