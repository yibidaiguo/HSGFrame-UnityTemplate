using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>编译校验命令的参数。</summary>
    public sealed class CompileCheckArguments
    {
        /// <summary>要编译的解决方案或工程文件路径。</summary>
        [Summary("要编译的解决方案或工程文件路径")]
        public string SolutionPath { get; set; }

        /// <summary>是否把编译警告也当成问题列出来。</summary>
        [Summary("是否把编译警告也当成问题列出来")]
        [DefaultValue(false)]
        public bool IncludeWarnings { get; set; }
    }

    /// <summary>编译校验命令：调 dotnet build 并把错误行结构化返回。</summary>
    public static class CompileCheckCommand
    {
        /// <summary>
        /// 编译校验，返回结构化的编译错误列表。
        /// </summary>
        /// <param name="arguments">编译参数。</param>
        [EditorCommand("compile.check")]
        [Summary("编译校验，返回结构化的编译错误列表")]
        public static CommandResult Execute(CompileCheckArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.SolutionPath))
            {
                return CommandResult.Failure("参数 SolutionPath 为必填项");
            }

            var (exitCode, outputLines) = ProcessRunner.Run(
                "dotnet",
                $"build \"{arguments.SolutionPath}\" --nologo",
                Environment.CurrentDirectory);

            var errors = outputLines.Where(line => line.Contains(" error ")).ToList();

            // 警告默认不算问题，只有显式要求时才收进输出。
            var problems = new List<string>(errors);
            if (arguments.IncludeWarnings)
            {
                problems.AddRange(outputLines.Where(line => line.Contains(" warning ")));
            }

            if (exitCode == 0 && errors.Count == 0)
            {
                return CommandResult.Success("编译通过，错误 0 条");
            }

            return CommandResult.Failure($"编译失败，错误 {errors.Count} 条", problems);
        }
    }
}
