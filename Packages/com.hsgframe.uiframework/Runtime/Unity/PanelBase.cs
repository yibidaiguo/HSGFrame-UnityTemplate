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

        /// <summary>
        /// 进场前那一帧挂的样式类：透明 + 往下偏一点。
        /// 布局完成后摘掉，于是过渡到常态——动画本身由 theme-motion.uss 里的 transition 做，
        /// 这里只管什么时候加、什么时候摘。
        /// </summary>
        public const string EnterFromClassName = "面板-进场前";

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
            PlayEnterTransition();
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

        /// <summary>
        /// 起一次进场过渡：先加「进场前」那一档，等布局跑完一次再摘掉。
        ///
        /// **必须等一次布局**。直接加完就摘，两次改动落在同一帧里，
        /// UI Toolkit 只会看到最终值，过渡根本不会发生——
        /// 那种错的症状是「动效在编辑器里偶尔能看到、打包后再也没有」，
        /// 因为它取决于那一帧有没有恰好被打断。所以用 GeometryChangedEvent
        /// （布局跑完才发）而不是 schedule 延时若干毫秒：后者是在赌帧率。
        ///
        /// **退场没有对应的动画，这是有意的**：Close 现在是同步的——
        /// 回调、出栈、摘节点一气呵成。要放退场动画就得等过渡结束再摘节点，
        /// 于是 Close 返回时面板还在树上、还在栈里。那会把「关了就是关了」
        /// 变成「关了但还在」，栈顶查询、返回键、连续开关都要跟着改。
        /// 为一个几百毫秒的淡出改掉这条契约不划算。
        /// </summary>
        private void PlayEnterTransition()
        {
            AddToClassList(EnterFromClassName);

            void OnFirstLayout(GeometryChangedEvent geometryChangedEvent)
            {
                UnregisterCallback<GeometryChangedEvent>(OnFirstLayout);
                RemoveFromClassList(EnterFromClassName);
            }

            RegisterCallback<GeometryChangedEvent>(OnFirstLayout);
        }
    }
}
