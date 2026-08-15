namespace HSGFrame.UiFramework
{
    /// <summary>面板所在的显示层，数值越大越靠上。</summary>
    public enum PanelLayer
    {
        /// <summary>底层 HUD：常驻的状态条、小地图等。</summary>
        Hud = 0,

        /// <summary>普通面板：主界面、背包等常规界面。</summary>
        Normal = 1,

        /// <summary>弹窗：对话框、确认框等模态界面。</summary>
        Dialog = 2,

        /// <summary>提示：飘字、Toast 等短暂提示。</summary>
        Tip = 3,

        /// <summary>加载：全屏加载遮罩，永远盖在最上层。</summary>
        Loading = 4
    }
}
