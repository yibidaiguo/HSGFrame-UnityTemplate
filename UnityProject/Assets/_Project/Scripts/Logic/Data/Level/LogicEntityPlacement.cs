using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Template.Logic.Data.Level
{
    /// <summary>单个逻辑实体的摆放：编号、类别、位置、朝向角度与自由参数。</summary>
    public class LogicEntityPlacement
    {
        [JsonPropertyName("编号")]
        public string EntityId { get; set; }

        // 本轮保持字符串（NPC / 触发器 / 刷怪点 / 可交互物 / 传送点 / 任务物件），将来要收紧再换枚举。
        [JsonPropertyName("类别")]
        public string EntityKind { get; set; }

        [JsonPropertyName("位置")]
        public LevelVector3 Position { get; set; }

        [JsonPropertyName("朝向角度")]
        public float RotationAngle { get; set; }

        [JsonPropertyName("参数")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }
}
