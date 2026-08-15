using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HSGFrame.UiFramework
{
    /// <summary>面板根：为每个 PanelLayer 建一个容器并按层序挂到宿主元素上，同时持有分层栈。</summary>
    public sealed class PanelRoot
    {
        private readonly VisualElement _hostElement;
        private readonly Dictionary<PanelLayer, VisualElement> _layerContainers = new Dictionary<PanelLayer, VisualElement>();

        /// <summary>用宿主元素构造面板根，容器按 PanelLayer 的枚举值从小到大添加。</summary>
        /// <param name="hostElement">承载各层容器的宿主元素。</param>
        public PanelRoot(VisualElement hostElement)
        {
            _hostElement = hostElement;

            foreach (PanelLayer layer in (PanelLayer[])Enum.GetValues(typeof(PanelLayer)))
            {
                var container = new VisualElement
                {
                    name = "层-" + layer,
                    pickingMode = PickingMode.Ignore,
                };
                container.style.position = Position.Absolute;
                container.style.left = 0;
                container.style.top = 0;
                container.style.right = 0;
                container.style.bottom = 0;

                _layerContainers[layer] = container;
                hostElement.Add(container);
            }
        }

        /// <summary>分层面板栈。</summary>
        public PanelStack Stack { get; } = new PanelStack();

        /// <summary>取某一层的容器元素。</summary>
        /// <param name="layer">目标层。</param>
        public VisualElement GetLayerContainer(PanelLayer layer)
        {
            return _layerContainers[layer];
        }

        /// <summary>打开一个面板到它自己声明的层。</summary>
        /// <param name="panel">要打开的面板。</param>
        public void Open(PanelBase panel)
        {
            panel.Open(GetLayerContainer(panel.Layer), Stack);
        }

        /// <summary>关闭一个面板。</summary>
        /// <param name="panel">要关闭的面板。</param>
        public void Close(PanelBase panel)
        {
            panel.Close(Stack);
        }

        /// <summary>把主题样式表挂到宿主元素上，让面板 USS 里的 var(--xxx) 解析得到。</summary>
        /// <param name="themeStyleSheet">主题样式表。</param>
        public void ApplyTheme(StyleSheet themeStyleSheet)
        {
            if (themeStyleSheet == null)
            {
                throw new ArgumentNullException(nameof(themeStyleSheet));
            }

            _hostElement.styleSheets.Add(themeStyleSheet);
        }
    }
}
