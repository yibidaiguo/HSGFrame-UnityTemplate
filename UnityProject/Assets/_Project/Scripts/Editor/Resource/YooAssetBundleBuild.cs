using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// YooAsset 的**真**构建：出 AssetBundle 与清单，产物按版本号落在 <c>Bundles/</c> 下。
    /// 与同目录的模拟构建是两回事：模拟构建不打包，运行期直接按 AssetDatabase 取资源，只在编辑器里成立；
    /// 这一条出来的东西才是能发给客户端的。
    /// </summary>
    public static class YooAssetBundleBuild
    {
        private const string BuildMenuPath = "工具链/资源/构建资源包";
        private const string PackageVersionArgumentName = "-packageVersion";

        /// <summary>跑一次真构建，返回一行中文摘要。</summary>
        /// <param name="packageVersion">包裹版本号，进产物目录名与清单。</param>
        public static string Build(string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                throw new ArgumentException(
                    "位置：packageVersion；原因：包裹版本号是空的；" +
                    "修复：传一个版本号，例如 1.0.0；" +
                    "参考：工具链/资源/构建资源包", nameof(packageVersion));
            }

            // 采集规则与模拟构建共用一份，出包前先确保它是配好的，免得两条链路的采集范围对不上。
            YooAssetSimulateBootstrap.ConfigureCollector();

            var buildParameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                BuildBundleType = (int)YooAsset.EBundleType.AssetBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = YooAssetSimulateBootstrap.PackageName,
                PackageVersion = packageVersion,
                PackageNote = "模板默认资源包裹",
                CompressOption = ECompressOption.LZ4,
                FileNameStyle = YooAsset.EFileNameStyle.HashName,
                VerifyBuildingResult = true,
            };

            var result = new ScriptableBuildPipeline().Run(buildParameters, enableLog: true);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"位置：{buildParameters.BuildOutputRoot}；原因：资源包构建失败（{result.ErrorInfo}）；" +
                    "修复：看 buildInfo.log 里失败的那个任务，通常是采集规则指向了空目录或地址重名；" +
                    "参考：工具链/资源/配置资源采集规则");
            }

            var outputDirectory = result.OutputPackageDirectory;
            var files = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var bundleCount = files.Count(path => Path.GetExtension(path).Equals(".bundle", StringComparison.OrdinalIgnoreCase));
            var totalBytes = files.Sum(path => new FileInfo(path).Length);

            return $"资源包构建完成：包裹 {buildParameters.PackageName} 版本 {packageVersion}，" +
                   $"产物目录 {outputDirectory}，文件 {files.Length} 个（资源包 {bundleCount} 个），合计 {totalBytes} 字节";
        }

        /// <summary>batchmode 入口：版本号从命令行的 -packageVersion 读，没给就用 1.0.0。</summary>
        public static void BuildFromCommandLine()
        {
            Debug.Log(Build(ReadArgument(PackageVersionArgumentName) ?? "1.0.0"));
        }

        /// <summary>菜单入口：用 1.0.0 跑一次真构建。</summary>
        [MenuItem(BuildMenuPath)]
        public static void BuildFromMenu()
        {
            Debug.Log(Build("1.0.0"));
        }

        private static string ReadArgument(string argumentName)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == argumentName)
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
