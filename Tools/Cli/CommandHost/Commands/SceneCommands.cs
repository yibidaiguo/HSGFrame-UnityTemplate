using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>关卡场景构建命令的参数。</summary>
    public sealed class SceneBuildArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        [DefaultValue(".")]
        public string TemplateRoot { get; set; }

        /// <summary>关卡名，对应 Levels 下的同名目录。</summary>
        [Summary("关卡名，对应 Levels 下的同名目录")]
        public string LevelName { get; set; }

        /// <summary>场景输出路径，相对 UnityProject 写，留空时按关卡名放进 Assets/Game/Scenes/World。</summary>
        // 带 [DefaultValue] 才会被命令框架判成选填：IsRequired 就是「没有 DefaultValue」。
        // 少了这一行，这个说好可以留空的参数会变成必填，命令一次都跑不起来。
        [Summary("场景输出路径，相对 UnityProject 写，留空时按关卡名放进 Assets/Game/Scenes/World")]
        [DefaultValue("")]
        public string ScenePath { get; set; }

        /// <summary>Unity 侧的超时分钟数。</summary>
        [Summary("Unity 侧的超时分钟数")]
        [DefaultValue(15)]
        public int TimeoutMinutes { get; set; }
    }

    /// <summary>关卡场景导出命令的参数。</summary>
    public sealed class SceneExportArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        [DefaultValue(".")]
        public string TemplateRoot { get; set; }

        /// <summary>要导出的场景路径，相对 UnityProject 写。</summary>
        [Summary("要导出的场景路径，相对 UnityProject 写")]
        public string ScenePath { get; set; }

        /// <summary>导出 JSON 的输出目录。</summary>
        [Summary("导出 JSON 的输出目录")]
        public string OutputDirectory { get; set; }

        /// <summary>写进 level.json 的环境名，留空时沿用输出目录里那份旧的 level.json 的取值。</summary>
        [Summary("写进 level.json 的环境名，留空时沿用输出目录里那份旧的 level.json 的取值")]
        [DefaultValue("")]
        public string EnvironmentName { get; set; }

        /// <summary>Unity 侧的超时分钟数。</summary>
        [Summary("Unity 侧的超时分钟数")]
        [DefaultValue(15)]
        public int TimeoutMinutes { get; set; }
    }

    /// <summary>关卡场景构建命令：把关卡 JSON 转交给 Unity 编辑器构建成场景。</summary>
    public static class SceneBuildCommand
    {
        private const string EntryMethod = "Template.Toolkit.Editor.LevelSceneCommandLine.BuildFromCommandLine";

        /// <summary>把关卡 JSON 构建成 Unity 场景。</summary>
        /// <param name="arguments">构建参数。</param>
        [EditorCommand("scene.build")]
        [Summary("把关卡 JSON 构建成 Unity 场景")]
        public static CommandResult Execute(SceneBuildArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.LevelName))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    "参数 LevelName",
                    "关卡名为空",
                    "在参数里填一个 Levels 下存在的关卡目录名",
                    "村庄"));
            }

            var templateRoot = SceneCommandSupport.ResolveTemplateRoot(arguments.TemplateRoot);
            var levelJsonPath = Path.Combine(templateRoot, "Levels", arguments.LevelName, "level.json");
            if (!File.Exists(levelJsonPath))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    levelJsonPath,
                    "关卡文件不存在",
                    "确认关卡名对应 Levels 下的一个目录，且里面有 level.json",
                    "Levels/Village/level.json"));
            }

            var timeoutProblem = SceneCommandSupport.CheckTimeout(arguments.TimeoutMinutes);
            if (timeoutProblem != null)
            {
                return timeoutProblem;
            }

            var argumentsFilePath = SceneCommandSupport.WriteArgumentsFile(templateRoot, "scene-build-arguments.json", arguments);
            return SceneCommandSupport.RunUnity(
                templateRoot,
                argumentsFilePath,
                EntryMethod,
                arguments.TimeoutMinutes,
                $"已把关卡「{arguments.LevelName}」交给 Unity 构建成场景");
        }
    }

    /// <summary>关卡场景导出命令：把 Unity 场景转交给编辑器导出回关卡 JSON。</summary>
    public static class SceneExportCommand
    {
        private const string EntryMethod = "Template.Toolkit.Editor.LevelSceneCommandLine.ExportFromCommandLine";

        /// <summary>把 Unity 场景导出回关卡 JSON。</summary>
        /// <param name="arguments">导出参数。</param>
        [EditorCommand("scene.export")]
        [Summary("把 Unity 场景导出回关卡 JSON")]
        public static CommandResult Execute(SceneExportArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.ScenePath))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    "参数 ScenePath",
                    "场景路径为空",
                    "在参数里填一个 UnityProject 下的场景路径",
                    "Assets/Game/Scenes/World/Village.unity"));
            }

            if (string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    "参数 OutputDirectory",
                    "输出目录为空",
                    "在参数里填导出 JSON 的输出目录",
                    "Levels/Village"));
            }

            var templateRoot = SceneCommandSupport.ResolveTemplateRoot(arguments.TemplateRoot);
            var scenePath = Path.Combine(templateRoot, "UnityProject", arguments.ScenePath);
            if (!File.Exists(scenePath))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    scenePath,
                    "场景文件不存在",
                    "确认场景路径相对 UnityProject 存在",
                    "Assets/Game/Scenes/World/Village.unity"));
            }

            var timeoutProblem = SceneCommandSupport.CheckTimeout(arguments.TimeoutMinutes);
            if (timeoutProblem != null)
            {
                return timeoutProblem;
            }

            var argumentsFilePath = SceneCommandSupport.WriteArgumentsFile(templateRoot, "scene-export-arguments.json", arguments);
            return SceneCommandSupport.RunUnity(
                templateRoot,
                argumentsFilePath,
                EntryMethod,
                arguments.TimeoutMinutes,
                $"已把场景 {arguments.ScenePath} 交给 Unity 导出回关卡 JSON");
        }
    }

    /// <summary>两条关卡场景命令共用的前置检查与 Unity 调度。</summary>
    internal static class SceneCommandSupport
    {
        private static readonly JsonSerializerOptions ArgumentsFileOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 参数文件里有中文关卡名，转义成 \uXXXX 之后人就看不出这一趟跑的是哪个关卡了。
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>把四要素拼成一条失败消息。</summary>
        public static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }

        /// <summary>模板根留空时退回当前目录。</summary>
        public static string ResolveTemplateRoot(string templateRoot)
        {
            return string.IsNullOrWhiteSpace(templateRoot) ? "." : templateRoot;
        }

        /// <summary>超时分钟数为正数时返回 null，否则返回一条四要素失败。</summary>
        public static CommandResult CheckTimeout(int timeoutMinutes)
        {
            if (timeoutMinutes > 0)
            {
                return null;
            }

            return CommandResult.Failure(ComposeError(
                "参数 TimeoutMinutes",
                "超时分钟数小于等于 0",
                "把 TimeoutMinutes 改成一个正整数",
                "15"));
        }

        /// <summary>把这一趟的参数写成 json 文件，返回文件路径。</summary>
        public static string WriteArgumentsFile(string templateRoot, string fileName, object arguments)
        {
            var directory = Path.Combine(templateRoot, "Temp", "EditorCommand");
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, fileName);
            File.WriteAllText(filePath, JsonSerializer.Serialize(arguments, arguments.GetType(), ArgumentsFileOptions));
            return filePath;
        }

        /// <summary>调 unity-cmd.ps1 把活转交给编辑器，按退出码翻译成命令结果。</summary>
        public static CommandResult RunUnity(
            string templateRoot,
            string argumentsFilePath,
            string entryMethod,
            int timeoutMinutes,
            string successMessage)
        {
            var unityCmdPath = Path.Combine(templateRoot, "Tools", "Cli", "unity-cmd.ps1");
            var processArguments =
                $"-NoProfile -File \"{unityCmdPath}\" -ExecuteMethod {entryMethod} -ArgumentsFile \"{argumentsFilePath}\" -TimeoutMinutes {timeoutMinutes}";

            var (exitCode, outputLines) = ProcessRunner.Run("pwsh", processArguments, templateRoot);

            if (exitCode == 0)
            {
                return CommandResult.Success(successMessage);
            }

            // 124 是 unity-cmd.ps1 沿用 GNU timeout 的约定，用来把超时与真失败区分开。
            if (exitCode == 124)
            {
                return CommandResult.Failure($"Unity 侧超时：{timeoutMinutes} 分钟未完成，已按 unity-cmd.ps1 约定强杀");
            }

            var tail = new List<string>(outputLines.Skip(Math.Max(0, outputLines.Count - 20)));
            return CommandResult.Failure($"Unity 侧失败，退出码 {exitCode}", tail);
        }
    }
}
