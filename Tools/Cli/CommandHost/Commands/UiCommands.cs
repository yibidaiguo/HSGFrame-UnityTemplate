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
            var writtenPaths = PanelScaffolder.Scaffold(templateRoot, definition, arguments.OutputDirectory);

            return CommandResult.Success($"面板「{definition.PanelName}」三件套已生成，共 {writtenPaths.Count} 个文件", writtenPaths);
        }
    }
}
