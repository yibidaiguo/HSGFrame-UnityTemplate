using System;
using UnityEngine.UIElements;

namespace HSGFrame.UiFramework
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

        /// <summary>面板标识名，分层栈里存的就是它，默认取运行时类型名。</summary>
        public virtual string PanelIdentifierName => GetType().Name;

        /// <summary>面板当前是否处于打开状态。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>面板打开时的初始化。</summary>
        public abstract void OnOpen();

        /// <summary>面板关闭时的清理。</summary>
        public abstract void OnClose();

        /// <summary>本面板所在的层。</summary>
        public abstract PanelLayer Layer { get; }

        /// <summary>打开面板：挂到给定的层容器上、压入分层栈、再回调 OnOpen。</summary>
        /// <remarks>OnOpen 放在最后：业务在 OnOpen 里查栈顶时应当已经能查到自己。</remarks>
        /// <param name="layerContainer">面板要挂到的层容器。</param>
        /// <param name="panelStack">分层面板栈。</param>
        public void Open(VisualElement layerContainer, PanelStack panelStack)
        {
            if (layerContainer == null)
            {
                throw new ArgumentNullException(nameof(layerContainer));
            }

            if (panelStack == null)
            {
                throw new ArgumentNullException(nameof(panelStack));
            }

            if (IsOpen)
            {
                return;
            }

            layerContainer.Add(this);
            style.display = DisplayStyle.Flex;
            panelStack.Push(Layer, PanelIdentifierName);
            IsOpen = true;
            OnOpen();
        }

        /// <summary>关闭面板：先回调 OnClose，再从分层栈与视觉树上摘掉。</summary>
        /// <remarks>OnClose 放在最前：业务在 OnClose 里还能读到自己在栈上。</remarks>
        /// <param name="panelStack">分层面板栈。</param>
        public void Close(PanelStack panelStack)
        {
            if (!IsOpen)
            {
                return;
            }

            OnClose();
            panelStack.Remove(Layer, PanelIdentifierName);
            RemoveFromHierarchy();
            IsOpen = false;
        }
    }
}
