using System;
using System.Collections.Generic;

namespace HSGFrame.Event
{
    /// <summary>
    /// 事件总线：按事件名订阅与派发，订阅返回句柄，Dispose 即退订。
    /// 只做无参与单负载两种形状——要传多个值时把它们装进一个负载类型，
    /// 负载有名字、能加字段而不破坏既有订阅方，比 0~16 个参数各写一套重载更好维护。
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<string, List<SubscriptionEntry>> _entriesByName = new Dictionary<string, List<SubscriptionEntry>>();

        /// <summary>已登记的事件名数量。</summary>
        public int EventNameCount => _entriesByName.Count;

        /// <summary>订阅一个无参事件。</summary>
        public IDisposable Subscribe(string eventName, Action handler)
        {
            ValidateEventName(eventName);
            var entries = GetOrCreateEntries(eventName);
            var entry = new SubscriptionEntry(payloadType: null, handler, this, eventName);
            entries.Add(entry);
            return entry.Subscription;
        }

        /// <summary>订阅一个带负载的事件；负载类型对不上的那一次派发会被跳过。</summary>
        public IDisposable Subscribe<TPayload>(string eventName, Action<TPayload> handler)
        {
            ValidateEventName(eventName);
            var entries = GetOrCreateEntries(eventName);
            var entry = new SubscriptionEntry(typeof(TPayload), handler, this, eventName);
            entries.Add(entry);
            return entry.Subscription;
        }

        /// <summary>派发一个无参事件。</summary>
        public void Publish(string eventName)
        {
            var entries = Snapshot(eventName);
            if (entries == null)
            {
                return;
            }

            var errors = new List<Exception>();
            foreach (var entry in entries)
            {
                if (entry.PayloadType != null)
                {
                    continue;
                }

                try
                {
                    ((Action)entry.Handler)();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            ThrowIfAny(errors);
        }

        /// <summary>派发一个带负载的事件。</summary>
        public void Publish<TPayload>(string eventName, TPayload payload)
        {
            var entries = Snapshot(eventName);
            if (entries == null)
            {
                return;
            }

            var errors = new List<Exception>();
            foreach (var entry in entries)
            {
                if (entry.PayloadType != typeof(TPayload))
                {
                    continue;
                }

                try
                {
                    ((Action<TPayload>)entry.Handler)(payload);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            ThrowIfAny(errors);
        }

        /// <summary>某个事件名当前的订阅者数量。</summary>
        public int SubscriberCount(string eventName)
        {
            return _entriesByName.TryGetValue(eventName, out var entries) ? entries.Count : 0;
        }

        /// <summary>清掉某个事件名的全部订阅。</summary>
        public bool ClearEvent(string eventName)
        {
            return _entriesByName.Remove(eventName);
        }

        /// <summary>清掉全部订阅。</summary>
        public void ClearAll()
        {
            _entriesByName.Clear();
        }

        private static void ValidateEventName(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                throw new ArgumentException(
                    $"位置：EventBus.Subscribe；原因：事件名是空串或 null；修复：传入非空的事件名字符串；参考：参见 EventBus.Subscribe 的参数说明");
            }
        }

        private List<SubscriptionEntry> GetOrCreateEntries(string eventName)
        {
            if (_entriesByName.TryGetValue(eventName, out var entries))
            {
                return entries;
            }

            entries = new List<SubscriptionEntry>();
            _entriesByName.Add(eventName, entries);
            return entries;
        }

        private List<SubscriptionEntry> Snapshot(string eventName)
        {
            if (!_entriesByName.TryGetValue(eventName, out var entries))
            {
                return null;
            }

            // 先取快照再逐个调：回调里退订自己、清空事件或再订阅，都不会修改正在遍历的集合。
            return new List<SubscriptionEntry>(entries);
        }

        private void RemoveEntry(string eventName, SubscriptionEntry entry)
        {
            if (!_entriesByName.TryGetValue(eventName, out var entries))
            {
                return;
            }

            entries.Remove(entry);
            if (entries.Count == 0)
            {
                _entriesByName.Remove(eventName);
            }
        }

        private static void ThrowIfAny(List<Exception> errors)
        {
            if (errors.Count > 0)
            {
                throw new AggregateException(errors);
            }
        }

        /// <summary>一条订阅的内部记录：负载类型 + 回调委托 + 退订句柄。</summary>
        private sealed class SubscriptionEntry
        {
            public SubscriptionEntry(Type payloadType, Delegate handler, EventBus owner, string eventName)
            {
                PayloadType = payloadType;
                Handler = handler;
                Subscription = new EventSubscription(() => owner.RemoveEntry(eventName, this));
            }

            /// <summary>负载类型，无参订阅为 null。</summary>
            public Type PayloadType { get; }

            /// <summary>回调委托，无参是 Action，带负载是 Action&lt;TPayload&gt;。</summary>
            public Delegate Handler { get; }

            /// <summary>退订句柄。</summary>
            public EventSubscription Subscription { get; }
        }
    }
}
