using System;
using System.Collections.Generic;

namespace HSGFrame.Scene
{
    /// <summary>场景加载方式：替换当前全部场景，或叠加到当前场景之上。</summary>
    public enum SceneLoadMode
    {
        /// <summary>替换当前全部场景。</summary>
        Single,

        /// <summary>叠加到当前场景之上。</summary>
        Additive,
    }

    /// <summary>一次场景加载请求：场景名、加载方式，以及加载完是否立刻激活。</summary>
    public sealed class SceneLoadRequest
    {
        /// <summary>用场景名、加载方式与是否立刻激活构造。</summary>
        public SceneLoadRequest(string sceneName, SceneLoadMode mode = SceneLoadMode.Single, bool activateOnLoad = true)
        {
            SceneName = sceneName;
            Mode = mode;
            ActivateOnLoad = activateOnLoad;
        }

        /// <summary>场景名。</summary>
        public string SceneName { get; }

        /// <summary>加载方式。</summary>
        public SceneLoadMode Mode { get; }

        /// <summary>为 false 时加载完不立刻激活，留给调用方挑时机（例如等进度条播完）。</summary>
        public bool ActivateOnLoad { get; }
    }

    /// <summary>
    /// 场景加载队列：请求排队、逐个推进、把进度归一化后报给订阅者。
    /// 归一化是本类型存在的主要理由：ActivateOnLoad 为 false 的请求，引擎侧的原始进度会停在 0.9
    /// （Unity 不允许激活时进度封顶在 0.9），这里把 0~0.9 映射成 0~1，让调用方看到的进度条能走满；
    /// ActivateOnLoad 为 true 时不做这个映射。
    /// </summary>
    public sealed class SceneLoadQueue
    {
        private readonly Queue<SceneLoadRequest> _pending = new Queue<SceneLoadRequest>();
        private SceneLoadRequest _current;

        /// <summary>排队中的请求数量（含正在加载的那一个）。</summary>
        public int PendingCount => _pending.Count + (_current != null ? 1 : 0);

        /// <summary>正在加载的请求，空闲时为 null。</summary>
        public SceneLoadRequest Current => _current;

        /// <summary>把一个请求排进队列。</summary>
        public void Enqueue(SceneLoadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentException(
                    "位置：SceneLoadQueue.Enqueue；原因：请求是 null；修复：传入非空的 SceneLoadRequest；参考：参见 SceneLoadQueue.Enqueue 的 request 参数说明");
            }

            if (string.IsNullOrEmpty(request.SceneName))
            {
                throw new ArgumentException(
                    "位置：SceneLoadQueue.Enqueue；原因：场景名是空串或 null；修复：传入非空的场景名字符串；参考：参见 SceneLoadRequest 的 sceneName 参数说明");
            }

            _pending.Enqueue(request);
        }

        /// <summary>取下一个请求开始加载；队列空或已有请求在加载时返回 null。</summary>
        public SceneLoadRequest StartNext()
        {
            if (_current != null || _pending.Count == 0)
            {
                return null;
            }

            _current = _pending.Dequeue();
            return _current;
        }

        /// <summary>上报当前请求的原始进度（0 到 1），触发 ProgressChanged。</summary>
        public void ReportProgress(float rawProgress)
        {
            if (_current == null)
            {
                return;
            }

            ProgressChanged?.Invoke(Normalize(rawProgress));
        }

        /// <summary>把当前请求标记成加载完成，Current 归空。</summary>
        public void CompleteCurrent()
        {
            if (_current == null)
            {
                return;
            }

            var completed = _current;
            _current = null;
            Completed?.Invoke(completed);
        }

        /// <summary>进度变化时触发，参数是 0 到 1 的归一化进度。</summary>
        public event Action<float> ProgressChanged;

        /// <summary>一个请求加载完成时触发。</summary>
        public event Action<SceneLoadRequest> Completed;

        private float Normalize(float rawProgress)
        {
            var clamped = Clamp(rawProgress);

            if (!_current.ActivateOnLoad)
            {
                // 不允许激活时引擎进度封顶在 0.9，把 0~0.9 线性映射到 0~1。
                return clamped / 0.9f;
            }

            return clamped;
        }

        private static float Clamp(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
