using HSGFrame.MonoDriver;
using UnityEngine;

namespace Template.Boot
{
    /// <summary>启动装配这一侧的帧驱动壳：跨场景常驻，按帧推进 <see cref="MonoDriverHub.Shared"/>。</summary>
    /// <remarks>
    /// 与 <c>Template.View.FrameworkDriverBehaviour</c> 是同一件事的两个落点，靠
    /// <see cref="MonoDriverHub.TryClaimDriver"/> 保证同一帧只有一个真在推进。
    /// 两个落点是程序集分层逼出来的：本壳属 AOT 的 Game.Boot，那个属热更的 Game.View，
    /// AOT 不许引热更，所以合并不了；共用的只有框架包里的那张表。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FrameworkDriverHost : MonoBehaviour
    {
        /// <summary>本壳当前是否持有驱动权。</summary>
        public bool IsDriving { get; private set; }

        /// <summary>建一个跨场景常驻的驱动壳。</summary>
        public static FrameworkDriverHost Create()
        {
            var host = new GameObject(nameof(FrameworkDriverHost));
            var driver = host.AddComponent<FrameworkDriverHost>();
            DontDestroyOnLoad(host);
            return driver;
        }

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
                MonoDriverHub.Shared.TickUpdate();
            }
        }

        private void LateUpdate()
        {
            if (IsDriving)
            {
                MonoDriverHub.Shared.TickLateUpdate();
            }
        }

        private void FixedUpdate()
        {
            if (IsDriving)
            {
                MonoDriverHub.Shared.TickFixedUpdate(Time.fixedDeltaTime);
            }
        }
    }
}
