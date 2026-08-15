using System.ComponentModel;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Scaffold;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>新建项目命令的参数。</summary>
    public sealed class ProjectCreateArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        [DefaultValue("Template")]
        public string TemplateRoot { get; set; }

        /// <summary>新项目落在哪个目录。</summary>
        [Summary("新项目落在哪个目录")]
        public string TargetDirectory { get; set; }

        /// <summary>新项目名，同时是复制过去之后的目录名。</summary>
        [Summary("新项目名，同时是复制过去之后的目录名")]
        public string ProjectName { get; set; }
    }

    /// <summary>新建项目命令：把模板树复制成一个新项目并改掉项目标识。</summary>
    public static class ProjectCreateCommand
    {
        /// <summary>用模板起一个新项目。</summary>
        /// <param name="arguments">新建参数。</param>
        [EditorCommand("project.create")]
        [Summary("用模板起一个新项目")]
        public static CommandResult Execute(ProjectCreateArguments arguments)
        {
            var options = new ProjectCreationOptions
            {
                TemplateRoot = string.IsNullOrWhiteSpace(arguments.TemplateRoot) ? "Template" : arguments.TemplateRoot,
                TargetDirectory = arguments.TargetDirectory,
                ProjectName = arguments.ProjectName
            };

            var result = ProjectGenerator.Create(options);
            if (!result.IsSuccess)
            {
                return CommandResult.Failure(result.Message);
            }

            return CommandResult.Success($"{result.Message}（{result.CreatedFileCount} 个文件 → {result.TargetPath}）");
        }
    }
}
