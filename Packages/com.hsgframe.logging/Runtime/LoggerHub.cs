namespace HSGFrame.Logging
{
    /// <summary>
    /// 全局日志门面挂靠点：启动装配把配好落点的门面挂上来，其余各层从这里取。
    /// </summary>
    /// <remarks>
    /// 与 <c>MonoDriverHub</c> 同一路数，理由也一样：启动装配是 AOT 程序集、业务视觉层是热更程序集，
    /// 两边互相引用不得，共用的只有框架包。没挂之前 <see cref="Shared"/> 是一个不带任何落点的门面，
    /// 写进去的日志静静丢掉——这样「装配还没跑就有人打日志」不会炸，只是没输出。
    /// </remarks>
    public static class LoggerHub
    {
        private static Logger _shared = new Logger();

        /// <summary>全局共享的日志门面，装配前是一个没有落点的空门面，永远不为 null。</summary>
        public static Logger Shared => _shared;

        /// <summary>是否已经有人挂过门面。</summary>
        public static bool IsPublished { get; private set; }

        /// <summary>挂上一个配好落点的门面，覆盖上一个。</summary>
        /// <param name="logger">要挂上的门面，传 null 等同于恢复成空门面。</param>
        public static void Publish(Logger logger)
        {
            _shared = logger ?? new Logger();
            IsPublished = logger != null;
        }

        /// <summary>恢复成没有落点的空门面。</summary>
        public static void Reset()
        {
            _shared = new Logger();
            IsPublished = false;
        }
    }
}
