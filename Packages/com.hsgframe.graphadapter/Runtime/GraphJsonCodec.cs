using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HSGFrame.GraphAdapter
{
    /// <summary>旧运行时兼容投影的 JSON 编解码，不得用于创作节点图。</summary>
    /// <remarks>
    /// 新的人工和 AI 创作代码应在 Editor 程序集中直接使用
    /// NodeEditor.EditorUI.GraphAuthoringAssetAccess，让 NodeGraphAsset/BlackboardAsset 保持唯一事实源。
    /// </remarks>
    [Obsolete(
        "GraphJsonCodec is a lossy compatibility projection, not an authoring API. " +
        "Use NodeEditor.EditorUI.GraphAuthoringAssetAccess with NodeGraphAsset/BlackboardAsset as the sole source.",
        false)]
    public static class GraphJsonCodec
    {
        // Encoder 用 UnsafeRelaxedJsonEscaping 让中文键与中文值原样输出，不转义成 \uXXXX。
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>把图文档序列化成 JSON 文本。</summary>
        public static string ToJson(GraphDocument graph) => JsonSerializer.Serialize(graph, _options);

        /// <summary>从 JSON 文本反序列化图文档。</summary>
        public static GraphDocument FromJson(string json) => JsonSerializer.Deserialize<GraphDocument>(json, _options);

        /// <summary>从文件读取并反序列化图文档。</summary>
        public static GraphDocument LoadFromFile(string filePath) => FromJson(File.ReadAllText(filePath));

        /// <summary>把图文档序列化并写入文件。</summary>
        public static void SaveToFile(GraphDocument graph, string filePath) => File.WriteAllText(filePath, ToJson(graph));
    }
}
