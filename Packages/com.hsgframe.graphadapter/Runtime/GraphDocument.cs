using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSGFrame.GraphAdapter
{
    /// <summary>一张节点图的镜像文档，英文键与对方执行器的公开形状对齐。</summary>
    /// <remarks>
    /// 键名一半英文一半中文是刻意的：graphId / module / instances / entryInstanceIds 这些英文键
    /// 是与对方 Runtime 对齐的跨仓库接口，而中文键（节点类型名、参数名、端口名）是本项目的数据内容。
    /// 将来换成真执行器时，本镜像格式不用改。
    /// </remarks>
    public sealed class GraphDocument
    {
        /// <summary>图编号。</summary>
        [JsonPropertyName("graphId")]
        public string GraphId { get; set; }

        /// <summary>所属模块。</summary>
        [JsonPropertyName("module")]
        public string Module { get; set; }

        /// <summary>图类型。</summary>
        [JsonPropertyName("graphType")]
        public string GraphType { get; set; }

        /// <summary>入口实例编号列表，执行从第一个开始。</summary>
        [JsonPropertyName("entryInstanceIds")]
        public List<string> EntryInstanceIds { get; set; } = new List<string>();

        /// <summary>节点实例列表。</summary>
        [JsonPropertyName("instances")]
        public List<GraphNodeInstance> Instances { get; set; } = new List<GraphNodeInstance>();

        /// <summary>按实例编号查找节点实例，找不到返回 null。</summary>
        public GraphNodeInstance FindInstance(string instanceId)
        {
            foreach (var instance in Instances)
            {
                if (instance.InstanceId == instanceId)
                {
                    return instance;
                }
            }

            return null;
        }
    }
}
