using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>供 Jenkins 走 batchmode 调用的出包入口，参数从命令行读。</summary>
    public static class PlayerBuildCommandLine
    {
        private const string OutputRootArgumentName = "-buildOutputRoot";
        private const string BuildNameArgumentName = "-buildName";
        private const string BuildVersionArgumentName = "-buildVersion";

        // 包名取工程设置里的产品名，别写死：写死之后模板生成出来的每个新项目
        // 打出来的包都会顶着模板宿主的名字。
        /// <summary>出 Windows 包。</summary>
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, PlayerSettings.productName + ".exe");
        }

        /// <summary>出 Android 包。</summary>
        public static void BuildAndroid()
        {
            Build(BuildTarget.Android, PlayerSettings.productName + ".apk");
        }

        private static void Build(BuildTarget buildTarget, string defaultBuildName)
        {
            var outputRoot = ReadArgument(OutputRootArgumentName) ?? Path.Combine(Directory.GetCurrentDirectory(), "Build");
            var buildName = ReadArgument(BuildNameArgumentName) ?? defaultBuildName;
            var buildVersion = ReadArgument(BuildVersionArgumentName);

            if (!string.IsNullOrEmpty(buildVersion))
            {
                PlayerSettings.bundleVersion = buildVersion;
            }

            Directory.CreateDirectory(outputRoot);

            // Windows 侧有一个「生成 Visual Studio 解决方案」的开关，它开着的时候 BuildPlayer 产出的是
            // 一份待编译的 C++ 工程而不是可执行文件——构建报告仍然是 Succeeded，很容易被当成出包成功。
            // 这个入口的产物必须是能直接跑的包，所以在这里显式关掉它。
            if (buildTarget == BuildTarget.StandaloneWindows64 || buildTarget == BuildTarget.StandaloneWindows)
            {
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
            }

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.Combine(outputRoot, buildName),
                target = buildTarget,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);

            // batchmode 下抛异常才会让 Unity 以非零码退出，Jenkins 靠这个判成败。
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception($"出包失败：{report.summary.result}，错误 {report.summary.totalErrors} 条");
            }

            // 报告说成功还不够：产物在不在磁盘上要自己看一眼。
            // 「构建成功但没有可执行文件」是真发生过的一种失败（见上面 CreateSolution 那一段）。
            if (!File.Exists(options.locationPathName))
            {
                throw new Exception(
                    $"位置：{options.locationPathName}；原因：构建报告为成功，但产物文件不存在；" +
                    "修复：确认 Windows 的「生成 Visual Studio 解决方案」开关处于关闭状态后重跑；" +
                    "参考：EditorUserBuildSettings.SetPlatformSettings(\"Standalone\", \"CreateSolution\", …)");
            }

            Debug.Log($"出包成功：{options.locationPathName}（{new FileInfo(options.locationPathName).Length} 字节）");
        }

        private static string ReadArgument(string argumentName)
        {
            var commandLineArguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < commandLineArguments.Length - 1; index++)
            {
                if (commandLineArguments[index] == argumentName)
                {
                    return commandLineArguments[index + 1];
                }
            }

            return null;
        }
    }
}
