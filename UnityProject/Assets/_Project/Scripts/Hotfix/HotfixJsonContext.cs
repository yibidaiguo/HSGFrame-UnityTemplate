using System.Text.Json.Serialization;

namespace Template.Hotfix
{
    /// <summary>
    /// System.Text.Json 的源生成上下文。它的实现体由 <c>System.Text.Json.SourceGeneration.dll</c>
    /// 在编译期补全——这个 partial 只写声明，编译过得去本身就是「源生成器跑过了」的证据。
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(HotfixProbeDocument))]
    public partial class HotfixJsonContext : JsonSerializerContext
    {
    }
}
