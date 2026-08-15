using System;
using System.IO;
using System.Text.Json;
using Template.Logic.Data.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Toolkit.Editor
{
    /// <summary>关卡场景构建与导出的两个入口：batchmode 命令行，以及编辑器菜单。</summary>
    public static class LevelSceneCommandLine
    {
        private const string ArgumentsFileArgumentName = "-argumentsFile";
        private const string BuildMenuPath = "工具链/关卡/把 JSON 构建成场景";
        private const string ExportMenuPath = "工具链/关卡/把当前场景导出成 JSON";

        /// <summary>batchmode 入口：从 -argumentsFile 指的 json 读参数，构建场景。</summary>
        public static void BuildFromCommandLine()
        {
            var arguments = ReadArguments<BuildArguments>();
            var templateRoot = string.IsNullOrEmpty(arguments.TemplateRoot)
                ? TemplateRootLocator.Find()
                : arguments.TemplateRoot;
            if (string.IsNullOrEmpty(templateRoot))
            {
                throw new InvalidOperationException("找不到模板根：TemplateRoot 参数为空，且未定位到 Tools/Gates/Config/gate-config.json");
            }

            var scenePath = string.IsNullOrEmpty(arguments.ScenePath)
                ? $"Assets/_Project/Scenes/{arguments.LevelName}.unity"
                : arguments.ScenePath;
            var levelDirectory = Path.Combine(templateRoot, "Levels", arguments.LevelName);

            var summary = LevelSceneBuilder.Build(levelDirectory, scenePath);
            Debug.Log(summary);
        }

        /// <summary>batchmode 入口：从 -argumentsFile 指的 json 读参数，导出场景。</summary>
        public static void ExportFromCommandLine()
        {
            var arguments = ReadArguments<ExportArguments>();

            // 环境名不在场景结构里。参数没给时退回读输出目录里那份旧的 关卡.json，
            // 免得往原地导出一次就把环境名清空。
            var environmentName = string.IsNullOrEmpty(arguments.EnvironmentName)
                ? ReadEnvironmentName(arguments.OutputDirectory)
                : arguments.EnvironmentName;

            var summary = LevelSceneExporter.Export(arguments.ScenePath, arguments.OutputDirectory, environmentName);
            Debug.Log(summary);
        }

        /// <summary>菜单入口：把模板根 Levels 下的每个关卡目录逐个构建成场景。</summary>
        [MenuItem(BuildMenuPath)]
        public static void BuildAllLevelsFromMenu()
        {
            var templateRoot = TemplateRootLocator.Find();
            if (string.IsNullOrEmpty(templateRoot))
            {
                Debug.LogError("找不到模板根：未定位到 Tools/Gates/Config/gate-config.json，无法确定 Levels 目录位置");
                return;
            }

            var levelsDirectory = Path.Combine(templateRoot, "Levels");
            foreach (var levelDirectory in Directory.GetDirectories(levelsDirectory))
            {
                if (!File.Exists(Path.Combine(levelDirectory, "关卡.json")))
                {
                    continue;
                }

                var levelName = Path.GetFileName(levelDirectory);
                var scenePath = $"Assets/_Project/Scenes/{levelName}.unity";
                var summary = LevelSceneBuilder.Build(levelDirectory, scenePath);
                Debug.Log(summary);
            }
        }

        /// <summary>菜单入口：把当前打开的场景导出回关卡 JSON。</summary>
        [MenuItem(ExportMenuPath)]
        public static void ExportActiveSceneFromMenu()
        {
            var scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError("当前没有已保存的场景，无法导出");
                return;
            }

            var templateRoot = TemplateRootLocator.Find();
            if (string.IsNullOrEmpty(templateRoot))
            {
                Debug.LogError("找不到模板根：未定位到 Tools/Gates/Config/gate-config.json，无法确定导出目录");
                return;
            }

            var levelName = Path.GetFileNameWithoutExtension(scenePath);
            var outputDirectory = Path.Combine(templateRoot, "Levels", levelName);
            var summary = LevelSceneExporter.Export(scenePath, outputDirectory, ReadEnvironmentName(outputDirectory));
            Debug.Log(summary);
        }

        private static T ReadArguments<T>()
        {
            var argumentsFile = ReadArgument(ArgumentsFileArgumentName);
            if (string.IsNullOrEmpty(argumentsFile))
            {
                throw new InvalidOperationException($"缺少 {ArgumentsFileArgumentName} 参数，无法读取构建/导出参数");
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(argumentsFile));
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

        // 环境名不在场景结构里，导出时得从原关卡 JSON 读回来，避免写回时把环境名清空。
        private static string ReadEnvironmentName(string outputDirectory)
        {
            var levelJsonPath = Path.Combine(outputDirectory, "关卡.json");
            if (!File.Exists(levelJsonPath))
            {
                return string.Empty;
            }

            return LevelSerializer.LevelFromJson(File.ReadAllText(levelJsonPath)).EnvironmentName ?? string.Empty;
        }

        // 超时字段由 unity-cmd.ps1 消费，C# 侧只负责把参数文件的形状声明完整，不读它的值。
        private sealed class BuildArguments
        {
            public string TemplateRoot { get; set; }
            public string LevelName { get; set; }
            public string ScenePath { get; set; }
            public int TimeoutMinutes { get; set; }
        }

        private sealed class ExportArguments
        {
            public string TemplateRoot { get; set; }
            public string ScenePath { get; set; }
            public string OutputDirectory { get; set; }
            public string EnvironmentName { get; set; }
            public int TimeoutMinutes { get; set; }
        }
    }
}
