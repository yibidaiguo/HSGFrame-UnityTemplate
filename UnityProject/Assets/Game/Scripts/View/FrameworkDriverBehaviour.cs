using HSGFrame.MonoDriver;
using UnityEngine;

namespace Template.View
{
    /// <summary>把引擎的帧回调转给纯 C# 的 MonoDriverRegistry。框架那一层不认识 Unity，驱动它的是这个壳。</summary>
    /// <remarks>
    /// 登记表本身住在 <see cref="MonoDriverHub"/>，不是本类型私有的——启动装配（AOT 程序集）也要往同一张表上登记，
    /// 而它引用不到本程序集。本壳按 <see cref="MonoDriverHub.TryClaimDriver"/> 认领驱动权：
    /// 启动装配已经在驱动时本壳自动让位，只在没有启动装配的场合（例如直接进世界场景、PlayMode 测试）才自己推进。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FrameworkDriverBehaviour : MonoBehaviour
    {
        private static FrameworkDriverBehaviour _instance;

        /// <summary>全局唯一的帧回调登记表。</summary>
        public static MonoDriverRegistry Registry => MonoDriverHub.Shared;

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

        /// <summary>本壳当前是否持有驱动权。</summary>
        public bool IsDriving { get; private set; }

        private void OnEnable()
        {
            IsDriving = MonoDriverHub.TryClaimDriver(this);
        }

        private void OnDisable()
        {
            if (IsDriving)
            {
                MonoDriverHub.ReleaseDriver(this);
                IsDriving = false;
            }
        }

        private void Update()
        {
            if (IsDriving)
            {
                Registry.TickUpdate();
            }
        }

        private void LateUpdate()
        {
            if (IsDriving)
            {
                Registry.TickLateUpdate();
            }
        }

        private void FixedUpdate()
        {
            if (IsDriving)
            {
                Registry.TickFixedUpdate(Time.fixedDeltaTime);
            }
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
