using System.Text.Json.Serialization;

namespace Template.Logic.Data.Level
{
    /// <summary>纯 C# 三分量结构，表示逻辑实体的位置，与 Unity.Mathematics 保持无关，以便在服务器侧运行。</summary>
    public readonly struct LevelVector3
    {
        [JsonPropertyName("x")]
        public float X { get; }

        [JsonPropertyName("y")]
        public float Y { get; }

        [JsonPropertyName("z")]
        public float Z { get; }

        [JsonConstructor]
        public LevelVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
