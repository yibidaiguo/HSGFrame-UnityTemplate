using System;
using System.ComponentModel;
using System.Linq;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>跑测试命令的参数。</summary>
    public sealed class TestRunArguments
    {
        /// <summary>要跑测试的解决方案或工程文件路径。</summary>
        [Summary("要跑测试的解决方案或工程文件路径")]
        public string SolutionPath { get; set; }

        /// <summary>传给 dotnet test 的筛选表达式。</summary>
        [Summary("传给 dotnet test 的筛选表达式")]
        [DefaultValue("")]
        public string TestFilter { get; set; }
    }

    /// <summary>跑测试命令：调 dotnet test 并把结论结构化返回。</summary>
    public static class TestRunCommand
    {
        /// <summary>
        /// 跑测试，返回结构化的测试结论。
        /// </summary>
        /// <param name="arguments">测试参数。</param>
        [EditorCommand("test.run")]
        [Summary("跑测试，返回结构化的测试结论")]
        public static CommandResult Execute(TestRunArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.SolutionPath))
            {
                return CommandResult.Failure("参数 SolutionPath 为必填项");
            }

            var argumentsText = $"test \"{arguments.SolutionPath}\" --nologo";
            if (!string.IsNullOrEmpty(arguments.TestFilter))
            {
                argumentsText += $" --filter \"{arguments.TestFilter}\"";
            }

            var (exitCode, outputLines) = ProcessRunner.Run(
                "dotnet",
                argumentsText,
                Environment.CurrentDirectory);

            // 只保留能反映结论的行：失败 / 通过 的中英文关键词。
            var summaryLines = outputLines
                .Where(line => line.Contains("失败") || line.Contains("Failed")
                    || line.Contains("已通过") || line.Contains("Passed"))
                .ToList();

            if (exitCode == 0)
            {
                return CommandResult.Success("测试通过", summaryLines);
            }

            return CommandResult.Failure("测试失败", summaryLines);
        }
    }
}
