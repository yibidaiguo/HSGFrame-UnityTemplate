// Unity Shim —— 仅供纯 .NET 工程编译使用；Unity 侧有真的 UnityEngine，因此 Unity 工程目录里保持没有本文件。
// 只提供空实现的「特性」，不提供任何 Unity 类型（Vector3 / Debug / ScriptableObject 等一律不补）：
// 真实类型出现在纯 C# 层本身就是设计错误，让它编译失败正是拦截手段。
namespace UnityEngine
{
    /// <summary>Unity 序列化字段特性的空实现，供纯 .NET 侧编译存量代码。</summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeFieldAttribute : System.Attribute { }

    /// <summary>Unity 多态序列化特性的空实现，供纯 .NET 侧编译存量代码。</summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeReferenceAttribute : System.Attribute { }

    /// <summary>Unity 隐藏字段特性的空实现，供纯 .NET 侧编译存量代码。</summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class HideInInspectorAttribute : System.Attribute { }
}
