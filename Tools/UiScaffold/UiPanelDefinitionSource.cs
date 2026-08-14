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
