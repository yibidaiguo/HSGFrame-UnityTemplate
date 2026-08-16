namespace HSGFrame.MonoDriver
{
    /// <summary>
    /// 全局帧驱动挂靠点：一张共享的登记表，外加「这一帧由谁推进」的认领。
    /// </summary>
    /// <remarks>
    /// 存在的理由是装配分两层：启动装配是 AOT 程序集，视觉层是热更程序集，两边互相引用不得
    /// （AOT 不许引热更），却要共用同一张登记表——否则登记在 A 表上的回调永远等不到 B 表的心跳。
    /// 挂靠点放在框架包里，两层都只引用框架，谁也不用认识谁。
    /// 认领机制解决第二个问题：两层各有一个驱动壳，不认领就会同一帧推进两次，
    /// 表现为「所有按帧计的东西都快一倍」，而且没有任何报错。
    /// </remarks>
    public static class MonoDriverHub
    {
        private static object _activeDriver;

        /// <summary>全局共享的帧回调登记表。</summary>
        public static MonoDriverRegistry Shared { get; } = new MonoDriverRegistry();

        /// <summary>当前正在推进登记表的驱动壳，没人认领时为 null。</summary>
        public static object ActiveDriver => _activeDriver;

        /// <summary>
        /// 认领驱动权。没人认领时认领成功；已经是自己时也算成功（重复认领安全）。
        /// </summary>
        /// <param name="driver">要认领的驱动壳，null 一律失败。</param>
        public static bool TryClaimDriver(object driver)
        {
            if (driver == null)
            {
                return false;
            }

            if (_activeDriver == null || ReferenceEquals(_activeDriver, driver))
            {
                _activeDriver = driver;
                return true;
            }

            return false;
        }

        /// <summary>交还驱动权。不是当前驱动方时什么也不做。</summary>
        /// <param name="driver">要交还的驱动壳。</param>
        public static void ReleaseDriver(object driver)
        {
            if (driver != null && ReferenceEquals(_activeDriver, driver))
            {
                _activeDriver = null;
            }
        }
    }
}
