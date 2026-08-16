using System.Collections.Generic;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;

namespace HSGFrame.Hotfix.Editor
{
    /// <summary>热更程序集的编译入口：调 HybridCLR 编译当前构建目标，再把热更那几个 dll 单独挑出来放到打包目录。</summary>
    public static class HotfixBuildEntry
    {
        private const string BuildMenuPath = "工具链/热更/编译热更程序集";

        /// <summary>
        /// 编译热更程序集并把产物拷进打包目录，返回一行中文摘要。
        /// </summary>
        /// <param name="outputDirectory">打包目录，热更 dll 会被拷到这里。</param>
        public static string CompileAndCollect(string outputDirectory)
        {
            CompileDllCommand.CompileDllActiveBuildTarget();

            // HybridCLR 那一步会把工程里所有程序集都编出来，热更的只是其中几个。
            // 打包目录里只放热更程序集：把 YooAsset、Unity.Mathematics 这些 AOT 程序集也发下去，
            // 客户端加载时会与本体里已有的那份撞车。
            var hotUpdateNames = SettingsUtil.HotUpdateAssemblyNamesIncludePreserved;
            var compiledDirectory = Path.Combine(
                SettingsUtil.ProjectDir,
                SettingsUtil.HotUpdateDllsRootOutputDir,
                EditorUserBuildSettings.activeBuildTarget.ToString());

            Directory.CreateDirectory(outputDirectory);

            var copied = new List<string>();
            foreach (var assemblyName in hotUpdateNames)
            {
                var sourcePath = Path.Combine(compiledDirectory, assemblyName + ".dll");
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"位置：{sourcePath}；原因：热更程序集编译产物缺失；" +
                        "修复：确认该程序集已登记进 HybridCLR 设置的热更程序集清单，再重跑一次编译；" +
                        "参考：工具链/热更/编译热更程序集");
                }

                var targetPath = Path.Combine(outputDirectory, assemblyName + ".dll");
                File.Copy(sourcePath, targetPath, overwrite: true);
                copied.Add(assemblyName + ".dll");
            }

            return $"热更程序集已编译并归集到 {outputDirectory}：{copied.Count} 个（{string.Join("、", copied)}）";
        }

        /// <summary>batchmode 入口：编译热更程序集并归集到 Build/HotfixPackages。</summary>
        public static void CompileFromCommandLine()
        {
            Debug.Log(CompileAndCollect(DefaultOutputDirectory()));
        }

        /// <summary>菜单入口：编译热更程序集并归集到 Build/HotfixPackages。</summary>
        [MenuItem(BuildMenuPath)]
        public static void CompileFromMenu()
        {
            Debug.Log(CompileAndCollect(DefaultOutputDirectory()));
        }

        // 打包目录取模板根下的 Build/HotfixPackages，与 pack-hotfix.ps1 的默认值保持一致。
        private static string DefaultOutputDirectory()
        {
            var templateRoot = HotfixOutputRootLocator.Find();
            if (string.IsNullOrEmpty(templateRoot))
            {
                throw new DirectoryNotFoundException(
                    "位置：模板根；原因：未定位到 Tools/Gates/Config/gate-config.json；" +
                    "修复：确认 Unity 工程位于模板目录之内；参考：Tools/Gates/Config/gate-config.json");
            }

            return Path.Combine(templateRoot, "Build", "HotfixPackages");
        }
    }
}
