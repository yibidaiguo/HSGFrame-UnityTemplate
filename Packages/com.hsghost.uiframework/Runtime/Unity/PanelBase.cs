using UnityEngine.UIElements;

namespace HSGhost.UiFramework
{
    /// <summary>面板基类：挂在 UI Toolkit 视觉树上，承载单个面板的根元素与生命周期。</summary>
    public abstract class PanelBase : VisualElement
    {
        /// <summary>面板根元素，子元素挂到它下面。</summary>
        protected VisualElement Root;

        /// <summary>初始化面板根元素，默认指向面板自身。</summary>
        protected PanelBase()
        {
            Root = this;
        }

        /// <summary>面板打开时的初始化。</summary>
        public abstract void OnOpen();

        /// <summary>面板关闭时的清理。</summary>
        public abstract void OnClose();

        /// <summary>本面板所在的层。</summary>
        public abstract PanelLayer Layer { get; }
    }
}
