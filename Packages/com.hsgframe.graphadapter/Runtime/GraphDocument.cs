using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSGFrame.GraphAdapter
{
    /// <summary>旧运行时与测试使用的有损兼容投影，不是节点图创作模型。</summary>
    /// <remarks>
    /// 此类型无法表达完整 Unit、黑板层级、编辑器布局、稳定 authoringKey 与修订向量。
    /// 人工和 AI 创作都必须通过 NodeEditor.EditorUI.GraphAuthoringAssetAccess 读写
    /// NodeGraphAsset/BlackboardAsset；本类型只保留给旧样例与执行器对拍。
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
