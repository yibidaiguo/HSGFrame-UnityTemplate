using System.Collections.Generic;
using HSGFrame.MonoDriver;
using UnityEngine;

namespace Template.Presentation.Framework
{
    /// <summary>把引擎的帧回调转给纯 C# 的 MonoDriverRegistry。框架那一层不认识 Unity，驱动它的是这个壳。</summary>
    [DisallowMultipleComponent]
    public sealed class FrameworkDriverBehaviour : MonoBehaviour
    {
        private static FrameworkDriverBehaviour _instance;

        /// <summary>全局唯一的帧回调登记表。</summary>
        public static MonoDriverRegistry Registry { get; } = new MonoDriverRegistry();

        /// <summary>取得驱动壳，场景里没有就现建一个并跨场景保留。</summary>
        public static FrameworkDriverBehaviour Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var host = new GameObject(nameof(FrameworkDriverBehaviour));
            _instance = host.AddComponent<FrameworkDriverBehaviour>();

            // 帧驱动跨场景存在：它管的是框架自己的心跳，被某次场景切换顺手销毁掉，
            // 所有登记者会一起哑掉，而且哑得没有任何报错。
            // DontDestroyOnLoad 只在运行模式下有效，编辑模式下调它会抛异常，所以按模式分开处理。
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(host);
            }
            return _instance;
        }

        private void Update()
        {
            Registry.TickUpdate();
        }

        private void LateUpdate()
        {
            Registry.TickLateUpdate();
        }

        private void FixedUpdate()
        {
            Registry.TickFixedUpdate(Time.fixedDeltaTime);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
