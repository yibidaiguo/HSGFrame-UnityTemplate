using HSGFrame.Logging;
using HSGFrame.UiFramework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Template.View
{
    /// <summary>
    /// UI 运行时根：持有 <see cref="UIDocument"/> 与 <see cref="PanelRoot"/>，是 UI Toolkit 那套栈唯一的运行时落点。
    /// </summary>
    /// <remarks>
    /// <see cref="PanelRoot"/> 要一个宿主 <see cref="VisualElement"/>，运行时只能来自 <see cref="UIDocument"/>，
    /// 而 <see cref="UIDocument"/> 必须挂 <c>PanelSettings</c>——这条链原先一个环都没有，
    /// 于是项目的目标 UI 栈在运行时根本起不来。本组件把这条链接上并跨场景常驻。
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiRootBehaviour : MonoBehaviour
    {
        private static UiRootBehaviour _instance;

        [SerializeField] [Tooltip("可选的主题样式表，挂上之后面板 USS 里的 var(--xxx) 才解析得到")]
        private ThemeStyleSheet _themeStyleSheet;

        private UIDocument _document;

        /// <summary>当前的 UI 根，没有装配过时为 null。</summary>
        public static UiRootBehaviour Current => _instance;

        /// <summary>分层面板根，本组件唤醒后可用。</summary>
        public PanelRoot PanelRoot { get; private set; }

        /// <summary>打开一个面板到它自己声明的层。</summary>
        /// <param name="panel">要打开的面板。</param>
        public void Open(PanelBase panel)
        {
            if (panel == null || PanelRoot == null)
            {
                return;
            }

            PanelRoot.Open(panel);
        }

        /// <summary>关闭一个面板。</summary>
        /// <param name="panel">要关闭的面板。</param>
        public void Close(PanelBase panel)
        {
            if (panel == null || PanelRoot == null)
            {
                return;
            }

            PanelRoot.Close(panel);
        }

        private void Awake()
        {
            // 第二个 UI 根出现时自己退场：两个 UIDocument 会各建一棵视觉树，
            // 后建的那棵默认盖在上面，表现为「面板打开了但点不到」，排查起来极费劲。
            if (_instance != null && _instance != this)
            {
                LoggerHub.Shared.Warning("位置：UiRootBehaviour；原因：场景里已经有一个 UI 根了，本个自行销毁；修复：UI 根跨场景常驻，只在启动场景里放一个；参考：Assets/Game/Scenes/Boot/Boot.unity");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            _document = GetComponent<UIDocument>();
            if (_document.panelSettings == null)
            {
                LoggerHub.Shared.Error("位置：UiRootBehaviour；原因：UIDocument 上没挂 PanelSettings，视觉树建不起来；修复：把 Assets/Game/Settings/Ui/MainPanelSettings.asset 挂到 UIDocument 上；参考：《结构规范-资源》第五节");
                return;
            }

            PanelRoot = new PanelRoot(_document.rootVisualElement);
            if (_themeStyleSheet != null)
            {
                PanelRoot.ApplyTheme(_themeStyleSheet);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
