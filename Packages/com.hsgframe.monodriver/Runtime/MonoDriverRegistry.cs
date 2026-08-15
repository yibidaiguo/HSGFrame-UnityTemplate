using System;
using System.Collections.Generic;

namespace HSGFrame.MonoDriver
{
    /// <summary>帧回调登记表：把每帧、每晚帧、每固定帧要调的委托登记在这里，由宿主按帧驱动。</summary>
    public sealed class MonoDriverRegistry
    {
        private readonly List<ActionRegistration> _updateListeners = new List<ActionRegistration>();
        private readonly List<ActionRegistration> _lateUpdateListeners = new List<ActionRegistration>();
        private readonly List<FixedUpdateRegistration> _fixedUpdateListeners = new List<FixedUpdateRegistration>();

        /// <summary>每帧回调的数量。</summary>
        public int UpdateListenerCount => _updateListeners.Count;

        /// <summary>晚帧回调的数量。</summary>
        public int LateUpdateListenerCount => _lateUpdateListeners.Count;

        /// <summary>固定帧回调的数量。</summary>
        public int FixedUpdateListenerCount => _fixedUpdateListeners.Count;

        /// <summary>登记一个每帧回调，返回句柄，Dispose 即注销。</summary>
        /// <param name="listener">要登记的回调。</param>
        public IDisposable AddUpdateListener(Action listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            var registration = new ActionRegistration(listener);
            _updateListeners.Add(registration);
            return new ListenerHandle(() => Remove(_updateListeners, registration));
        }

        /// <summary>登记一个晚帧回调。</summary>
        /// <param name="listener">要登记的回调。</param>
        public IDisposable AddLateUpdateListener(Action listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            var registration = new ActionRegistration(listener);
            _lateUpdateListeners.Add(registration);
            return new ListenerHandle(() => Remove(_lateUpdateListeners, registration));
        }

        /// <summary>登记一个固定帧回调，回调参数是固定步长秒数。</summary>
        /// <param name="listener">要登记的回调。</param>
        public IDisposable AddFixedUpdateListener(Action<float> listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            var registration = new FixedUpdateRegistration(listener);
            _fixedUpdateListeners.Add(registration);
            return new ListenerHandle(() => Remove(_fixedUpdateListeners, registration));
        }

        /// <summary>推进一帧。</summary>
        public void TickUpdate()
        {
            Dispatch(_updateListeners);
        }

        /// <summary>推进一个晚帧。</summary>
        public void TickLateUpdate()
        {
            Dispatch(_lateUpdateListeners);
        }

        /// <summary>推进一个固定帧。</summary>
        /// <param name="fixedDeltaSeconds">固定步长秒数。</param>
        public void TickFixedUpdate(float fixedDeltaSeconds)
        {
            // 派发前先取快照：回调里注销自己或再登记一个都不会在遍历中修改这个数组。
            // 新登记的回调不在快照里，本帧不执行，下一帧才轮到。
            var snapshot = _fixedUpdateListeners.ToArray();
            var errors = new List<Exception>();
            foreach (var registration in snapshot)
            {
                if (registration.IsRemoved)
                {
                    continue;
                }

                try
                {
                    registration.Listener(fixedDeltaSeconds);
                }
                catch (Exception exception)
                {
                    // 回调可能抛出任意异常，这里必须接住 Exception 才能让其余回调照常执行，
                    // 最后统一抛出。半路中断会让「谁先登记谁说了算」。
                    errors.Add(exception);
                }
            }

            ThrowIfAny(errors);
        }

        /// <summary>注销全部回调。</summary>
        public void ClearAll()
        {
            _updateListeners.Clear();
            _lateUpdateListeners.Clear();
            _fixedUpdateListeners.Clear();
        }

        private static void Dispatch(List<ActionRegistration> registrations)
        {
            // 快照语义见 TickFixedUpdate 的说明：回调里的增删不影响本轮遍历。
            var snapshot = registrations.ToArray();
            var errors = new List<Exception>();
            foreach (var registration in snapshot)
            {
                if (registration.IsRemoved)
                {
                    continue;
                }

                try
                {
                    registration.Listener();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            ThrowIfAny(errors);
        }

        private static void ThrowIfAny(List<Exception> errors)
        {
            if (errors.Count > 0)
            {
                throw new AggregateException(errors);
            }
        }

        private static void Remove(List<ActionRegistration> registrations, ActionRegistration registration)
        {
            if (registration.IsRemoved)
            {
                return;
            }

            registration.IsRemoved = true;
            registrations.Remove(registration);
        }

        private static void Remove(List<FixedUpdateRegistration> registrations, FixedUpdateRegistration registration)
        {
            if (registration.IsRemoved)
            {
                return;
            }

            registration.IsRemoved = true;
            registrations.Remove(registration);
        }

        /// <summary>每帧/晚帧回调的登记条目，IsRemoved 标记它是否已注销。</summary>
        private sealed class ActionRegistration
        {
            public ActionRegistration(Action listener)
            {
                Listener = listener;
            }

            /// <summary>要执行的回调。</summary>
            public Action Listener { get; }

            /// <summary>是否已注销。</summary>
            public bool IsRemoved { get; set; }
        }

        /// <summary>固定帧回调的登记条目。</summary>
        private sealed class FixedUpdateRegistration
        {
            public FixedUpdateRegistration(Action<float> listener)
            {
                Listener = listener;
            }

            /// <summary>要执行的回调。</summary>
            public Action<float> Listener { get; }

            /// <summary>是否已注销。</summary>
            public bool IsRemoved { get; set; }
        }

        /// <summary>登记句柄：持有注销动作，Dispose 两次安全。</summary>
        private sealed class ListenerHandle : IDisposable
        {
            private readonly Action _onDispose;
            private bool _isDisposed;

            public ListenerHandle(Action onDispose)
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
