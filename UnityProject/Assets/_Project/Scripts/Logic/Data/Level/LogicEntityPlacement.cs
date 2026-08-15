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

        // Unity 的 Transform 内部存四元数，localEulerAngles 取回来永远落在 [0,360)。
        // 源 JSON 里写 -90 或 450，往场景里构建再导出一次就变成 270 / 90，往返不等价。
        // 把 [0,360) 定成这个字段的规范形式、读入时就归一化，往返才是不动点。
        [JsonPropertyName("朝向角度")]
        public float RotationAngle
        {
            get => _rotationAngle;
            set => _rotationAngle = NormalizeAngle(value);
        }

        private float _rotationAngle;

        // 非有限值原样放行：把 NaN 悄悄改成 0 会让「数据本来就坏」变成「数据看着没问题」，
        // 校验器那一层反而查不出来。
        private static float NormalizeAngle(float angle)
        {
            if (float.IsNaN(angle) || float.IsInfinity(angle))
            {
                return angle;
            }

            var wrapped = angle % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        [JsonPropertyName("参数")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }
}
