using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Dashboard;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>列出流水线命令的参数。</summary>
    public sealed class PipelineListArguments
    {
        /// <summary>模板根目录，定义文件路径以它为基准。</summary>
        [Summary("模板根目录，定义文件路径以它为基准")]
        public string TemplateRoot { get; set; }

        /// <summary>定义文件路径，留空时取模板根下的 Pipelines/流水线定义.json。</summary>
        [Summary("定义文件路径，留空时取模板根下的 Pipelines/流水线定义.json")]
        [DefaultValue("")]
        public string DefinitionPath { get; set; }
    }

    /// <summary>跑流水线命令的参数。</summary>
    public sealed class PipelineRunArguments
    {
        /// <summary>模板根目录，定义文件与各步骤的路径都以它为基准。</summary>
        [Summary("模板根目录，定义文件与各步骤的路径都以它为基准")]
        public string TemplateRoot { get; set; }

        /// <summary>要跑的流水线名称。</summary>
        [Summary("要跑的流水线名称")]
        public string PipelineName { get; set; }

        /// <summary>为 true 时跳过需要 Unity 的步骤，默认 true。</summary>
        [Summary("为 true 时跳过需要 Unity 的步骤，默认 true")]
        [DefaultValue(true)]
        public bool? SkipUnitySteps { get; set; }

        /// <summary>定义文件路径，留空时取模板根下的 Pipelines/流水线定义.json。</summary>
        [Summary("定义文件路径，留空时取模板根下的 Pipelines/流水线定义.json")]
        [DefaultValue("")]
        public string DefinitionPath { get; set; }
    }

    /// <summary>流水线命令：列出全部流水线，或跑其中一条。</summary>
    public static class PipelineCommands
    {
        /// <summary>列出定义文件里的全部流水线与各自的步骤数。</summary>
        /// <param name="arguments">列出命令参数。</param>
        [EditorCommand("pipeline.list")]
        [Summary("列出全部流水线与各自的步骤数")]
        public static CommandResult List(PipelineListArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.TemplateRoot))
            {
                return CommandResult.Failure("位置：参数 TemplateRoot；原因：模板根为空；修复：传模板根目录；参考：先跑 pipeline.list 看有哪些");
            }

            if (!Directory.Exists(arguments.TemplateRoot))
            {
                return CommandResult.Failure($"位置：{arguments.TemplateRoot}；原因：模板根不存在；修复：传实际存在的模板根目录；参考：模板根目录（含 Tools/Gates/Config/gate-config.json 的那一级）");
            }

            var definitionPath = ResolveDefinitionPath(arguments.TemplateRoot, arguments.DefinitionPath);

            PipelineCatalog catalog;
            try
            {
                catalog = PipelineCatalog.LoadFromFile(definitionPath);
            }
            catch (PipelineDefinitionException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var lines = new List<string>();
            foreach (var pipeline in catalog.Pipelines ?? Array.Empty<PipelineDefinition>())
            {
                var stepCount = pipeline?.Steps?.Count ?? 0;
                lines.Add($"{pipeline?.Name}\t{stepCount} 步");
            }

            return CommandResult.Success($"共 {lines.Count} 条流水线", lines);
        }

        /// <summary>跑一条流水线，逐行把输出打到标准输出。</summary>
        /// <param name="arguments">跑流水线参数。</param>
        [EditorCommand("pipeline.run")]
        [Summary("跑一条流水线，逐行把输出打到标准输出")]
        public static CommandResult Run(PipelineRunArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.TemplateRoot))
            {
                return CommandResult.Failure("位置：参数 TemplateRoot；原因：模板根为空；修复：传模板根目录；参考：模板根目录（含 Tools/Gates/Config/gate-config.json 的那一级）");
            }

            if (!Directory.Exists(arguments.TemplateRoot))
            {
                return CommandResult.Failure($"位置：{arguments.TemplateRoot}；原因：模板根不存在；修复：传实际存在的模板根目录；参考：模板根目录（含 Tools/Gates/Config/gate-config.json 的那一级）");
            }

            if (string.IsNullOrWhiteSpace(arguments.PipelineName))
            {
                return CommandResult.Failure("位置：参数 PipelineName；原因：流水线名为空；修复：传要跑的流水线名称；参考：先跑 pipeline.list 看有哪些");
            }

            var definitionPath = ResolveDefinitionPath(arguments.TemplateRoot, arguments.DefinitionPath);

            PipelineCatalog catalog;
            try
            {
                catalog = PipelineCatalog.LoadFromFile(definitionPath);
            }
            catch (PipelineDefinitionException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var pipeline = catalog.Find(arguments.PipelineName);
            if (pipeline == null)
            {
                return CommandResult.Failure($"位置：{arguments.PipelineName}；原因：找不到这条流水线；修复：核对流水线名称；参考：先跑 pipeline.list 看有哪些");
            }

            // [DefaultValue] 只让框架把参数判成选填，不会把默认值填进参数对象，这里自己兜底。
            var skipUnitySteps = arguments.SkipUnitySteps ?? true;

            // 逐行回调直接打标准输出：看板与调用方要的是实时日志流，不是进程结束后的汇总。
            var runner = new PipelineRunner(arguments.TemplateRoot, line => Console.WriteLine(line));
            var result = runner.Run(pipeline, skipUnitySteps);

            return result.IsSuccess ? CommandResult.Success(result.Message) : CommandResult.Failure(result.Message);
        }

        private static string ResolveDefinitionPath(string templateRoot, string definitionPath)
        {
            if (!string.IsNullOrWhiteSpace(definitionPath))
            {
                return definitionPath;
            }

            return Path.Combine(templateRoot, "Pipelines", "流水线定义.json");
        }
    }
}
