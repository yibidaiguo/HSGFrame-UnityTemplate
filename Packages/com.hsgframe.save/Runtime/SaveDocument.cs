using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSGFrame.Save
{
    /// <summary>一份状态快照式存档：Sections 按状态域存放各域 JSON 文本，本层只搬运不解析。</summary>
    public sealed class SaveDocument
    {
        /// <summary>存档版本号，供迁移链判断是否需要升级。</summary>
        [JsonPropertyName("版本")]
        public int Version { get; set; }

        // 键是状态域名（如「背包」「任务」），值是那一域的 JSON 文本——存档等于状态层快照，
        // 域内结构由各自的状态类负责，这一层只搬运，不解析域内内容。
        /// <summary>数据域表：键是状态域名，值是那一域的 JSON 文本。</summary>
        [JsonPropertyName("数据域")]
        public Dictionary<string, string> Sections { get; set; } = new Dictionary<string, string>();
    }
}
