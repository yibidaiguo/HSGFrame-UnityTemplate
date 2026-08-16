using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 把热更程序集与 AOT 补充元数据拷进 <c>StreamingAssets/HotfixShip/</c>，让它们随包发出去。
    /// 必须在 <c>HybridCLR/Generate/All</c> 之后、<c>BuildPlayer</c> 之前跑：
    /// 前者产出这些 dll，后者才会把 StreamingAssets 复制进包。
    /// </summary>
    public static class HotfixShipStaging
    {
        private const string StageMenuPath = "工具链/热更/热更资产随包";
        private const string ShipDirectoryName = "HotfixShip";
        private const string AotMetadataDirectoryName = "AotMetadata";

        // 热更代码用到的 AOT 泛型实例要靠补充元数据补回来。这份保底清单覆盖基础库与
        // System.Text.Json 那条链路；Generate/All 生成的 AOTGenericReferences 若在，则以它为准再并上这几个。
        private static readonly string[] FallbackAotAssemblyNames =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "System.Text.Json.dll",
            "System.Text.Encodings.Web.dll",
        };

        /// <summary>把热更 dll 与 AOT 补充元数据拷进 StreamingAssets，返回一行中文摘要。</summary>
        public static string Stage()
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var shipDirectory = Path.Combine(Application.dataPath, "StreamingAssets", ShipDirectoryName);
            var metadataDirectory = Path.Combine(shipDirectory, AotMetadataDirectoryName);
            Directory.CreateDirectory(metadataDirectory);

            var hotUpdateDirectory = Path.Combine(
                SettingsUtil.ProjectDir,
                SettingsUtil.HotUpdateDllsRootOutputDir,
                buildTarget.ToString());
            var strippedDirectory = Path.Combine(
                SettingsUtil.ProjectDir,
                SettingsUtil.AssembliesPostIl2CppStripDir,
                buildTarget.ToString());

            // 先把上一轮留下的 .dll.bytes 清掉：热更程序集改名之后，旧名字那份不会被覆盖，
            // 会一路躺进包里发出去。真踩过——热更清单从 Hotfix.Logic 换成 Game.Logic/Game.View 之后，
            // 包里同时躺着三份，其中一份是已经不存在的程序集。
            var retired = new List<string>();
            foreach (var stalePath in Directory.GetFiles(shipDirectory, "*.dll.bytes"))
            {
                File.Delete(stalePath);
                var stalePathMeta = stalePath + ".meta";
                if (File.Exists(stalePathMeta))
                {
                    File.Delete(stalePathMeta);
                }

                retired.Add(Path.GetFileName(stalePath));
            }

            var stagedHotUpdate = new List<string>();
            foreach (var assemblyName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
            {
                var sourcePath = Path.Combine(hotUpdateDirectory, assemblyName + ".dll");
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"位置：{sourcePath}；原因：热更程序集编译产物缺失；" +
                        "修复：先跑 HybridCLR/CompileDll 或 HybridCLR/Generate/All，再跑这一步；" +
                        "参考：工具链/热更/编译热更程序集");
                }

                File.Copy(sourcePath, Path.Combine(shipDirectory, assemblyName + ".dll.bytes"), overwrite: true);
                stagedHotUpdate.Add(assemblyName);
            }

            var stagedMetadata = new List<string>();
            var missingMetadata = new List<string>();
            foreach (var assemblyFileName in ResolveAotAssemblyFileNames())
            {
                var sourcePath = Path.Combine(strippedDirectory, assemblyFileName);
                if (!File.Exists(sourcePath))
                {
                    missingMetadata.Add(assemblyFileName);
                    continue;
                }

                File.Copy(sourcePath, Path.Combine(metadataDirectory, assemblyFileName + ".bytes"), overwrite: true);
                stagedMetadata.Add(assemblyFileName);
            }

            AssetDatabase.Refresh();

            var summary = $"热更资产随包完成：热更程序集 {stagedHotUpdate.Count} 个（{string.Join("、", stagedHotUpdate)}），" +
                          $"AOT 补充元数据 {stagedMetadata.Count} 个，落点 {shipDirectory}";
            if (missingMetadata.Count > 0)
            {
                summary += $"；下列 AOT 程序集在 {strippedDirectory} 下没有，已跳过：{string.Join("、", missingMetadata)}";
            }

            var retiredNames = retired.Where(name => !stagedHotUpdate.Contains(
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(name)))).ToList();
            if (retiredNames.Count > 0)
            {
                summary += $"；顺带清掉已不在热更清单里的旧产物 {retiredNames.Count} 个：{string.Join("、", retiredNames)}";
            }

            return summary;
        }

        /// <summary>batchmode 入口。</summary>
        public static void StageFromCommandLine()
        {
            Debug.Log(Stage());
        }

        /// <summary>菜单入口。</summary>
        [MenuItem(StageMenuPath)]
        public static void StageFromMenu()
        {
            Debug.Log(Stage());
        }

        // Generate/All 会生成一个没有命名空间的 AOTGenericReferences 类，里面的 PatchedAOTAssemblyList
        // 才是这个工程真正需要补元数据的那一份清单。它是生成物，所以只能反射着取。
        private static IEnumerable<string> ResolveAotAssemblyFileNames()
        {
            var names = new List<string>(FallbackAotAssemblyNames);

            var referenceType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("AOTGenericReferences", throwOnError: false))
                .FirstOrDefault(type => type != null);
            var listField = referenceType?.GetField("PatchedAOTAssemblyList");
            if (listField?.GetValue(null) is IEnumerable<string> generatedNames)
            {
                names.AddRange(generatedNames);
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
