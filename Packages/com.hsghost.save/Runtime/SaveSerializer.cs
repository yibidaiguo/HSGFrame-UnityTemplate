using System.Text.Encodings.Web;
using System.Text.Json;

namespace HSGhost.Save
{
    /// <summary>存档 JSON 与内存模型的读写，选项与关卡序列化保持一致。</summary>
    public static class SaveSerializer
    {
        // Encoder 用 UnsafeRelaxedJsonEscaping 让中文键与中文值原样输出，不转义成 \uXXXX。
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>把存档序列化成 JSON 文本。</summary>
        public static string ToJson(SaveDocument document) => JsonSerializer.Serialize(document, _options);

        /// <summary>从 JSON 文本反序列化存档。</summary>
        public static SaveDocument FromJson(string json) => JsonSerializer.Deserialize<SaveDocument>(json, _options);
    }
}
