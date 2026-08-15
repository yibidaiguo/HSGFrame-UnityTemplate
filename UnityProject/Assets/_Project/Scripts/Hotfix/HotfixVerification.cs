using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace Template.Hotfix
{
    /// <summary>
    /// 出包验收的两条探针，都写在热更程序集内部：跑起来时这段代码是被 HybridCLR 解释执行的，
    /// 而不是 IL2CPP 编出来的本机代码。调用方在主程序集里靠反射调这两个方法。
    /// </summary>
    public static class HotfixVerification
    {
        private const string ProbeTypeFullName = "Template.Hotfix.Generated.HotfixSourceGeneratorProbe";

        /// <summary>验证 3：源生成器的产物在不在这个热更程序集里。返回一行以「通过：」或「未通过：」开头的结论。</summary>
        public static string ProbeSourceGenerator()
        {
            var hotfixAssembly = typeof(HotfixVerification).Assembly;
            var assemblyName = hotfixAssembly.GetName().Name;

            // 刻意走反射而不是直接引用：生成器万一没跑，这里返回一行「未通过」，
            // 而不是让整个热更程序集编译失败、把出包也一起拖垮。
            var probeType = hotfixAssembly.GetType(ProbeTypeFullName, throwOnError: false);
            if (probeType == null)
            {
                return $"未通过：热更程序集 {assemblyName} 内找不到 {ProbeTypeFullName}，" +
                       "说明源生成器的产物没有随热更程序集编译进去";
            }

            var describeMethod = probeType.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            if (describeMethod == null)
            {
                return $"未通过：{ProbeTypeFullName} 在 {assemblyName} 内，但没有 Describe() 方法";
            }

            var description = describeMethod.Invoke(null, null) as string;
            var hostAssemblyField = probeType.GetField("HostAssemblyName", BindingFlags.Public | BindingFlags.Static);
            var hostAssemblyName = hostAssemblyField?.GetRawConstantValue() as string;

            if (hostAssemblyName != assemblyName)
            {
                return $"未通过：探针自述的宿主程序集是 {hostAssemblyName}，" +
                       $"而它实际所在的程序集是 {assemblyName}，两者对不上";
            }

            return $"通过：{description}；探针所在程序集 {assemblyName}，" +
                   $"由 {probeType.Assembly.GetName().Name} 加载";
        }

        /// <summary>验证 7：System.Text.Json 的源生成上下文在 IL2CPP × HybridCLR 下能不能真往返。</summary>
        public static string ProbeJsonSourceGeneration()
        {
            var original = new HotfixProbeDocument
            {
                Title = "热更存档 · 中文标题",
                Count = 3,
                Tags = new List<string> { "甲", "乙", "丙" },
            };

            var typeInfo = HotfixJsonContext.Default.HotfixProbeDocument;
            if (typeInfo == null)
            {
                return "未通过：源生成上下文里取不到 HotfixProbeDocument 的 JsonTypeInfo";
            }

            string json;
            HotfixProbeDocument restored;
            try
            {
                // 用 JsonTypeInfo 这一个重载是关键：它完全不走反射解析器，
                // 序列化元数据只能来自源生成的产物，所以这条通了就证明源生成这条路在 AOT 下真能用。
                json = JsonSerializer.Serialize(original, typeInfo);
                restored = JsonSerializer.Deserialize(json, typeInfo);
            }
            catch (Exception exception)
            {
                return $"未通过：源生成往返抛异常 {exception.GetType().Name}：{exception.Message}";
            }

            if (restored == null)
            {
                return "未通过：反序列化回来的对象是 null";
            }

            if (restored.Title != original.Title || restored.Count != original.Count)
            {
                return $"未通过：标量往返对不上，回来的是 Title={restored.Title}、Count={restored.Count}";
            }

            if (restored.Tags == null || restored.Tags.Count != original.Tags.Count)
            {
                return $"未通过：集合往返对不上，回来的元素个数是 {restored.Tags?.Count.ToString() ?? "null"}";
            }

            for (var index = 0; index < original.Tags.Count; index++)
            {
                if (restored.Tags[index] != original.Tags[index])
                {
                    return $"未通过：集合第 {index} 个元素往返对不上，回来的是 {restored.Tags[index]}";
                }
            }

            var contextAssemblyName = HotfixJsonContext.Default.GetType().Assembly.GetName().Name;
            return $"通过：源生成上下文在 {contextAssemblyName} 内，JSON 为 {json}，" +
                   $"往返后 Title={restored.Title}、Count={restored.Count}、Tags={string.Join("|", restored.Tags)}";
        }

        /// <summary>顺带记一行：不带源生成的反射序列化在同一个包里是什么下场，供对照，不参与判定。</summary>
        public static string DescribeReflectionFallback()
        {
            try
            {
                var text = JsonSerializer.Serialize(new HotfixProbeDocument { Title = "反射路径", Count = 1 });
                return $"对照：反射序列化也跑得通，产出 {text}";
            }
            catch (Exception exception)
            {
                return $"对照：反射序列化不可用（{exception.GetType().Name}：{exception.Message}）";
            }
        }
    }
}
