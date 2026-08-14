using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameTemplateForAgent.GraphAdapter
{
    /// <summary>图里的一个节点实例：类型、参数与端口接线。</summary>
    public sealed class GraphNodeInstance
    {
        /// <summary>实例编号，图内唯一。</summary>
        [JsonPropertyName("instanceId")]
        public string InstanceId { get; set; }

        /// <summary>节点类型名，决定执行器如何解释该节点。</summary>
        [JsonPropertyName("nodeType")]
        public string NodeType { get; set; }

        /// <summary>参数表：键是参数名，值是参数值（一律字符串）。</summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>端口表：键是端口名，值是下一实例编号。</summary>
        [JsonPropertyName("ports")]
        public Dictionary<string, string> Ports { get; set; } = new Dictionary<string, string>();
    }
}
