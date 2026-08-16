using System.IO;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.UiScaffold;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>界面骨架生成命令的参数。</summary>
    public sealed class UiScaffoldArguments
    {
        /// <summary>面板定义文件路径。</summary>
        [Summary("面板定义文件路径")]
        public string DefinitionPath { get; set; }

        /// <summary>三件套的输出目录。</summary>
        [Summary("三件套的输出目录")]
        public string OutputDirectory { get; set; }

        /// <summary>模板根目录，用于定位 scriban 模板。</summary>
        [Summary("模板根目录，用于定位 scriban 模板")]
        [System.ComponentModel.DefaultValue("Template")]
        public string TemplateRoot { get; set; }

        /// <summary>为 true 时只比对不落盘。</summary>
        [Summary("为 true 时只比对不落盘")]
        [System.ComponentModel.DefaultValue(false)]
        public bool VerifyOnly { get; set; }
    }

    /// <summary>界面骨架生成命令：从面板定义产出 UXML + USS + C# 三件套。</summary>
    public static class UiScaffoldCommand
    {
        /// <summary>生成一个面板的三件套，返回写出的文件路径。</summary>
        /// <param name="arguments">生成参数。</param>
        [EditorCommand("ui.scaffold")]
        [Summary("从面板定义生成 UXML + USS + C# 三件套")]
        public static CommandResult Execute(UiScaffoldArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.DefinitionPath) || string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                return CommandResult.Failure("参数 DefinitionPath 与 OutputDirectory 均为必填项");
            }

            if (!File.Exists(arguments.DefinitionPath))
            {
                return CommandResult.Failure($"面板定义文件不存在：{arguments.DefinitionPath}");
            }

            var definition = JsonSerializer.Deserialize<UiPanelDefinitionSource>(
                File.ReadAllText(arguments.DefinitionPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var templateRoot = string.IsNullOrWhiteSpace(arguments.TemplateRoot) ? "Template" : arguments.TemplateRoot;

            // 先校验定义再生成：重名标识名之类的毛病，生成出来是编译错误、报在生成物上，
            // 作者顺着报错找不回定义文件。在这里拦，位置就是能改的那一处。
            var definitionProblems = definition.Validate();
            if (definitionProblems.Count > 0)
            {
                return CommandResult.Failure($"面板定义有问题，共 {definitionProblems.Count} 条", definitionProblems);
            }

            if (arguments.VerifyOnly)
            {
                var problems = PanelScaffolder.Verify(templateRoot, definition, arguments.OutputDirectory);
                return problems.Count == 0
                    ? CommandResult.Success("UI 三件套与定义一致")
                    : CommandResult.Failure($"UI 三件套与定义不一致，问题 {problems.Count} 条", problems);
            }

            var writtenPaths = PanelScaffolder.Scaffold(templateRoot, definition, arguments.OutputDirectory);

            return CommandResult.Success($"面板「{definition.PanelName}」三件套已生成，共 {writtenPaths.Count} 个文件", writtenPaths);
        }
    }
}
