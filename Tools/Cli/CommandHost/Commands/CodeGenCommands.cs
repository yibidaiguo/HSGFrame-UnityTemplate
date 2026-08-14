using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CodeGen;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>代码生成命令的参数。</summary>
    public sealed class CodeGenerationArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        [DefaultValue("Template")]
        public string TemplateRoot { get; set; }

        /// <summary>为 true 时只比对不落盘。</summary>
        [Summary("为 true 时只比对不落盘")]
        [DefaultValue(false)]
        public bool VerifyOnly { get; set; }
    }

    /// <summary>代码生成命令：按清单重新生成全部产物。</summary>
    public static class CodeGenerationCommand
    {
        /// <summary>重新生成或校验全部代码生成目标。</summary>
        /// <param name="arguments">生成参数。</param>
        [EditorCommand("codegen.run")]
        [Summary("按清单重新生成配置表访问代码，或只校验产物是否最新")]
        public static CommandResult Execute(CodeGenerationArguments arguments)
        {
            var templateRoot = string.IsNullOrWhiteSpace(arguments.TemplateRoot) ? "Template" : arguments.TemplateRoot;
            var configurationPath = Path.Combine(templateRoot, "Tools", "CodeGen", "Config", "codegen-config.json");
            if (!File.Exists(configurationPath))
            {
                return CommandResult.Failure($"生成清单不存在：{configurationPath}");
            }

            var configuration = CodeGenerationConfiguration.LoadFromFile(configurationPath);

            if (arguments.VerifyOnly)
            {
                var problems = CodeGenerator.Verify(templateRoot, configuration);
                return problems.Count == 0
                    ? CommandResult.Success("生成物全部是最新的")
                    : CommandResult.Failure($"生成物有 {problems.Count} 处过期", problems);
            }

            var changedTargets = new List<string>();
            foreach (var target in configuration.Targets)
            {
                if (CodeGenerator.WriteIfChanged(templateRoot, target))
                {
                    changedTargets.Add($"{target.TargetName} → {target.OutputPath}");
                }
            }

            return changedTargets.Count == 0
                ? CommandResult.Success($"生成完成，{configuration.Targets.Count} 个目标内容均未变化")
                : CommandResult.Success($"生成完成，{changedTargets.Count} 个目标有更新", changedTargets);
        }
    }
}
