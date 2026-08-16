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

        /// <summary>挂上一份名录，覆盖上一份。</summary>
        /// <param name="catalog">要挂上的名录，传 null 等同于摘掉。</param>
        public static void Publish(ILevelEntityCatalog catalog)
        {
            Current = catalog;
        }

        /// <summary>摘掉当前名录，关卡卸载时调用。</summary>
        public static void Clear()
        {
            Current = null;
        }
    }
}
