using System;

namespace HSGFrame.Event
{
    /// <summary>一次订阅的句柄：Dispose 即退订，重复 Dispose 安全。</summary>
    public sealed class EventSubscription : IDisposable
    {
        private Action _unsubscribe;
        private bool _disposed;

        /// <summary>用退订动作构造句柄，仅供事件总线内部创建。</summary>
        internal EventSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        /// <summary>退订这一次订阅；重复调用只生效一次。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var unsubscribe = _unsubscribe;
            _unsubscribe = null;
            unsubscribe?.Invoke();
        }
    }
}
