using System.Collections.Generic;

namespace Template.Toolkit.Scaffold
{
    /// <summary>
    /// 一个可选功能「要摘掉哪些东西」的清单。
    /// 摘除是十来处零散改动（删包、摘 manifest、摘解决方案条目、清门禁配置、摘文档段），
    /// 全写成数据是为了让「这个功能到底占了哪些位置」有一处能读的答案，而不是散在代码里。
    /// </summary>
    public sealed class FeatureRemovalPlan
    {
        /// <summary>功能名，<c>feature.remove --name</c> 匹配的就是它。</summary>
        public string FeatureName { get; set; }

        /// <summary>要整个删掉的目录，模板根相对、正斜杠。</summary>
        public IReadOnlyList<string> Directories { get; set; }

        /// <summary>要删掉的单个文件，模板根相对、正斜杠；同名 .meta 跟着删。</summary>
        public IReadOnlyList<string> Files { get; set; }

        /// <summary>要从 <c>UnityProject/Packages/manifest.json</c> 的 dependencies 里摘掉的键。</summary>
        public IReadOnlyList<string> UnityPackageKeys { get; set; }

        /// <summary>要从 <c>Solutions/Template.sln</c> 里摘掉的工程名，整名匹配。</summary>
        public IReadOnlyList<string> SolutionProjectNames { get; set; }

        /// <summary>要从测试基线里摘掉的路径前缀。</summary>
        public IReadOnlyList<string> TestBaselinePathPrefixes { get; set; }

        /// <summary>要从门禁配置的 sourceScanSkipSegments 里摘掉的段名。</summary>
        public IReadOnlyList<string> SourceScanSkipSegments { get; set; }

        /// <summary>文档标记里的功能名，用来拼「feature:&lt;名&gt; 开始 / 结束」。</summary>
        public string DocumentMarkerName { get; set; }

        /// <summary>本模板认识的可选功能清单。</summary>
        public static IReadOnlyList<FeatureRemovalPlan> ListKnown()
        {
            return new[]
            {
                new FeatureRemovalPlan
                {
                    FeatureName = "hotfix",
                    Directories = new[]
                    {
                        "Packages/com.hsgframe.hotfix",
                        "Tools/Hotfix",
                        "Tools/SourceGenerators/HotfixProbe",
                        "Solutions/Hotfix.Tests",
                        "UnityProject/Assets/HybridCLRGenerate",
                    },
                    Files = new[] { "UnityProject/ProjectSettings/HybridCLRSettings.asset" },
                    UnityPackageKeys = new[] { "com.hsgframe.hotfix", "com.code-philosophy.hybridclr" },
                    SolutionProjectNames = new[] { "Hotfix", "Hotfix.Tests", "HotfixProbeGenerator" },
                    TestBaselinePathPrefixes = new[] { "Solutions/Hotfix.Tests/" },

                    // 只摘 HybridCLRGenerate，HybridCLRData 那条**刻意留着**：
                    // 前者是进仓库的生成物，跟着上面的目录一起删掉了；后者是 800 MB 的本地 il2cpp 数据，
                    // gitignore 掉、不在仓库里，这条命令不该悄悄删掉别人机器上的它。
                    // 它还躺在盘上而跳过项被摘掉的话，命名门禁会去扫那 800 MB 第三方源码，当场红一片——真踩过。
                    SourceScanSkipSegments = new[] { "HybridCLRGenerate" },
                    DocumentMarkerName = "hotfix",
                },
            };
        }
    }
}
