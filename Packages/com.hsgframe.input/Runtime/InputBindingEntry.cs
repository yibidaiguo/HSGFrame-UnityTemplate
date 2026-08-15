using System.Text.Json.Serialization;

namespace HSGFrame.Input
{
    /// <summary>一条按键绑定：一个动作名对应主键与副键。</summary>
    public sealed class InputBindingEntry
    {
        /// <summary>动作名称。</summary>
        [JsonPropertyName("动作")]
        public string ActionName { get; set; }

        /// <summary>主按键。</summary>
        [JsonPropertyName("主键")]
        public string PrimaryKey { get; set; }

        /// <summary>副按键。</summary>
        [JsonPropertyName("副键")]
        public string SecondaryKey { get; set; }
    }
}
