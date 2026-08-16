using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Template.Toolkit.UiScaffold
{
    /// <summary>面板定义的轻量模型，对应 uidef.json 的顶层字段。</summary>
    public sealed class UiPanelDefinitionSource
    {
        /// <summary>面板显示名，例如「主界面」。</summary>
        [JsonPropertyName("面板名")]
        public string PanelName { get; set; }

        /// <summary>面板标识名，用作文件名与 C# 类名，例如 MainPanel。</summary>
        [JsonPropertyName("面板标识名")]
        public string PanelIdentifierName { get; set; }

        // 层名直接写进生成的 C#（PanelLayer.<层名>），所以取值必须是 HSGFrame.UiFramework.PanelLayer
        // 上真有的成员名：Hud / Normal / Dialog / Tip / Loading。写错了会在 Unity 编译那一级报出来。
        /// <summary>面板所在的层名，缺省是 Normal。</summary>
        [JsonPropertyName("面板层")]
        public string PanelLayer
        {
            // 空白一律归成 Normal：字段缺失、写成 null、写成空串三种情形在生成物里表现完全一样
            //（都会渲染出 `PanelLayer.` 这种编不过的残句），在这里一次性收口比在模板里判省事。
            get => string.IsNullOrWhiteSpace(_panelLayer) ? "Normal" : _panelLayer;
            set => _panelLayer = value;
        }

        private string _panelLayer;

        /// <summary>面板包含的元素清单。</summary>
        [JsonPropertyName("元素清单")]
        public List<UiElementSource> Elements { get; set; } = new List<UiElementSource>();
    }

    /// <summary>面板单个元素的轻量模型，对应 uidef.json 元素清单里的每一项。</summary>
    public sealed class UiElementSource
    {
        /// <summary>元素显示名，例如「血条」。</summary>
        [JsonPropertyName("元素名")]
        public string ElementName { get; set; }

        /// <summary>元素标识名，例如 HealthBar。</summary>
        [JsonPropertyName("标识名")]
        public string IdentifierName { get; set; }

        /// <summary>元素类型，对应 UI Toolkit 控件名，例如 ProgressBar。</summary>
        [JsonPropertyName("元素类型")]
        public string ElementType { get; set; }
    }
}
