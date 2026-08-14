// Unity Shim —— 仅供纯 .NET 工程编译使用；Unity 侧有真的 UnityEngine，因此 Unity 工程目录里保持没有本文件。
// 只提供空实现的「特性」，不提供任何 Unity 类型（Vector3 / Debug / ScriptableObject 等一律不补）。
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeFieldAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeReferenceAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class HideInInspectorAttribute : System.Attribute { }
}
