using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Logic.Data.Level
{
    /// <summary>关卡 JSON 与内存模型的双向转换。</summary>
    public static class LevelSerializer
    {
        // Encoder 用 UnsafeRelaxedJsonEscaping 让中文键与中文值原样输出，不转义成 \uXXXX；
        // float 交给 System.Text.Json 默认行为处理，往返比较由测试侧用三位小数容差完成。
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>把关卡元信息序列化成 JSON 文本。</summary>
        public static string ToJson(LevelDefinition level) => JsonSerializer.Serialize(level, _options);

        /// <summary>从 JSON 文本反序列化关卡元信息。</summary>
        public static LevelDefinition LevelFromJson(string json) => JsonSerializer.Deserialize<LevelDefinition>(json, _options);

        /// <summary>把区块序列化成 JSON 文本。</summary>
        public static string ToJson(LevelChunk chunk) => JsonSerializer.Serialize(chunk, _options);

        /// <summary>从 JSON 文本反序列化区块。</summary>
        public static LevelChunk ChunkFromJson(string json) => JsonSerializer.Deserialize<LevelChunk>(json, _options);
    }
}
