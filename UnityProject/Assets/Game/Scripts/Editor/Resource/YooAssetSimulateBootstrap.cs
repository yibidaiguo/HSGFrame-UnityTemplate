using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Template.Toolkit.Editor
{
    /// <summary>YooAsset 的编辑器侧链路：配采集规则、跑模拟构建、核对产出的包裹清单。运行期的加载在 PlayMode 那侧。</summary>
    public static class YooAssetSimulateBootstrap
    {
        /// <summary>本模板使用的资源包裹名。</summary>
        public const string PackageName = "DefaultPackage";

        /// <summary>资源采集根目录，相对 Unity 工程写。</summary>
        public const string CollectRoot = "Assets/Game/Art";

        private const string ConfigureMenuPath = "工具链/资源/配置资源采集规则";
        private const string BuildMenuPath = "工具链/资源/模拟构建资源包";

        /// <summary>把采集规则配成「Art 目录整个收进一个包裹」，已经配过就原地更新。</summary>
        [MenuItem(ConfigureMenuPath)]
        public static void ConfigureCollector()
        {
            var setting = BundleCollectorSettingData.Setting;
            var package = setting.Packages.FirstOrDefault(candidate => candidate.PackageName == PackageName);
            if (package == null)
            {
                package = new BundleCollectorPackage { PackageName = PackageName };
                setting.Packages.Add(package);
            }

            package.PackageDesc = "模板默认资源包裹";

            // 开可寻址：不开的话资源定位地址是完整资源路径，AddressByFileName 那条规则形同虚设，
            // 调用方得写 "Assets/Game/Art/Texture/T_HeroIdle_01.png" 才取得到。
            package.EnableAddressable = true;
            package.Groups.Clear();

            var group = new BundleCollectorGroup
            {
                GroupName = "美术资产",
                GroupDesc = "Art 目录下的贴图、音频与预制体",
            };
            group.Collectors.Add(new BundleCollector
            {
                CollectPath = CollectRoot,
                CollectorGUID = AssetDatabase.AssetPathToGUID(CollectRoot),
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAssetsExceptPipelineConfig),
            });
            package.Groups.Add(group);

            BundleCollectorSettingData.SaveFile();
            Debug.Log($"[资源] 采集规则已配好：包裹 {PackageName}，采集根 {CollectRoot}");
        }

        /// <summary>跑一次模拟构建，返回包裹根目录。</summary>
        public static string SimulateBuild()
        {
            var parameters = new PackageBuildParameters(PackageName)
            {
                BuildPipelineName = EBuildPipeline.EditorSimulateBuildPipeline.ToString(),

                // 模拟构建管线只收 Virtual 这一族：它不真打 AssetBundle，
                // 而是让运行期直接按 AssetDatabase 取资源，所以编辑器下不必等真打包就能跑通寻址。
                BuildBundleType = (int)EBundleType.VirtualAssetBundle,
            };

            var result = BundleSimulateBuilder.SimulateBuild(parameters);
            if (result == null || string.IsNullOrEmpty(result.PackageRootDirectory))
            {
                throw new InvalidOperationException(
                    "位置：模拟构建；原因：YooAsset 没有返回包裹根目录；" +
                    $"修复：先跑一次「{ConfigureMenuPath}」，确认采集根下有资产；" +
                    $"参考：{CollectRoot}");
            }

            return result.PackageRootDirectory;
        }

        /// <summary>菜单入口：跑模拟构建并把产出的清单信息打出来。</summary>
        [MenuItem(BuildMenuPath)]
        public static void SimulateBuildFromMenu()
        {
            Debug.Log(BuildAndDescribe());
        }

        /// <summary>跑模拟构建并返回一行中文摘要，含包裹根目录与清单文件数量。</summary>
        public static string BuildAndDescribe()
        {
            var packageRoot = SimulateBuild();
            var files = Directory.Exists(packageRoot)
                ? Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            var manifestNames = files
                .Select(Path.GetFileName)
                .Where(name => name.EndsWith(".bytes", StringComparison.Ordinal)
                            || name.EndsWith(".json", StringComparison.Ordinal)
                            || name.EndsWith(".hash", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            return $"包裹 {PackageName} 模拟构建完成：根目录 {packageRoot}，" +
                   $"产出文件 {files.Length} 个，清单类 {manifestNames.Count} 个（{string.Join("、", manifestNames)}）";
        }

        /// <summary>
        /// 供 PlayMode 用的初始化选项：编辑器模拟模式指向刚构建出来的包裹根目录。
        /// 编辑模式下驱动不到 YooAsset 的异步操作（它的 Update 是内部方法、由运行期的驱动组件调），
        /// 所以真正的加载验收放在 PlayMode。
        /// </summary>
        /// <param name="packageRoot">模拟构建产出的包裹根目录。</param>
        public static EditorSimulateModeOptions CreateSimulateModeOptions(string packageRoot)
        {
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot),
            };
        }
    }
}
