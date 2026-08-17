using System;

namespace Template.Level.Contracts
{
    /// <summary>
    /// 当前关卡名录的挂靠点：装配方把名录挂上来，模块外从这里取。
    /// </summary>
    /// <remarks>
    /// 模板没有容器，而模块外要拿到名录总得有个不碰 <c>Level.View</c> 的地方（R2）。
    /// 挂靠点因此放在 Contracts 里，与 <c>FrameworkDriverBehaviour.Registry</c> 是同一路数。
    /// 只准装配方写、别人只读：写入口叫 Publish 而不是 setter，就是想让「谁在写」在调用点上看得见。
    /// </remarks>
    public static class LevelEntityCatalogRegistry
    {
        /// <summary>当前关卡的实体名录，还没装配时为 null。</summary>
        public static ILevelEntityCatalog Current { get; private set; }

        /// <summary>
        /// 名录换了（挂上或摘掉）时抛出，参数是新的名录，摘掉时是 null。
        /// </summary>
        /// <remarks>
        /// 有这个事件之前，消费方只能在 <c>sceneLoaded</c> 之后猜等几帧再读 <see cref="Current"/>——
        /// 而关卡装配是一串异步资产加载，等几帧都可能太早，读到的是上一关的名录或者空的。
        /// 猜错不会报错，只会表现成「这一关的东西都不能交互」。订这个事件就不必猜。
        /// </remarks>
        public static event Action<ILevelEntityCatalog> Published;

        /// <summary>挂上一份名录，覆盖上一份。</summary>
        /// <param name="catalog">要挂上的名录，传 null 等同于摘掉。</param>
        public static void Publish(ILevelEntityCatalog catalog)
        {
            Current = catalog;
            Published?.Invoke(catalog);
        }

        /// <summary>摘掉当前名录，关卡卸载时调用。</summary>
        public static void Clear()
        {
            Current = null;
            Published?.Invoke(null);
        }
    }
}
