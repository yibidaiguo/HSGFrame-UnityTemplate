// System.Text.Json 的最小 Shim —— 与 Solutions/UnityShim/ 是同一手法的镜像：
// 纯 .NET 侧有真的 System.Text.Json（BCL 自带），Unity 6000.3 里没有，
// 同一批 Logic 源码两边编译时 Unity 侧会因为找不到这几个特性而编译失败（16a 实测）。
//
// 这里只补「特性」，不补 JsonSerializer 本身：
// 序列化行为留在纯 C# 侧与服务器侧，Unity 侧只需要这些标注能编译过。
// 要在 Unity 运行时真做 System.Text.Json 序列化，得另外引入 dll 或换序列化器——那是人回来后的决策。
//
// 本文件刻意放在 Logic/ 树之外：Logic.Core.csproj 用通配符 link Logic/**，
// 放进去会与 BCL 的真类型撞名。
namespace System.Text.Json.Serialization
{
    /// <summary>指定 JSON 键名的特性，Unity 侧空实现。</summary>
    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonPropertyNameAttribute : System.Attribute
    {
        /// <summary>用键名构造。</summary>
        /// <param name="name">JSON 里的键名。</param>
        public JsonPropertyNameAttribute(string name)
        {
            Name = name;
        }

        /// <summary>JSON 里的键名。</summary>
        public string Name { get; }
    }

    /// <summary>标记参数化构造函数的特性，Unity 侧空实现。</summary>
    [System.AttributeUsage(System.AttributeTargets.Constructor, AllowMultiple = false)]
    public sealed class JsonConstructorAttribute : System.Attribute
    {
    }

    /// <summary>标记忽略成员的特性，Unity 侧空实现。</summary>
    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonIgnoreAttribute : System.Attribute
    {
    }
}
