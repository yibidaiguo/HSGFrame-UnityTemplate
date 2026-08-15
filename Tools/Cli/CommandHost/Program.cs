using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;

namespace Template.Toolkit.CommandHost
{
    /// <summary>
    /// 命令宿主入口：在 Unity 编辑器关闭时，用纯 dotnet 方式驱动编辑器命令。
    /// </summary>
    public static class Program
    {
        private static readonly JsonSerializerOptions LogOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            switch (args[0])
            {
                case "list":
                    return ListCommands();
                case "describe":
                    return DescribeCommand(args);
                case "run":
                    return RunCommand(args);
                default:
                    PrintUsage();
                    return 2;
            }
        }

        private static int ListCommands()
        {
            var commands = ScanAllCommands();
            foreach (var command in commands)
            {
                Console.WriteLine($"{command.CommandName}\t{command.Description}");
            }

            return 0;
        }

        private static int DescribeCommand(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            var commandName = args[1];
            var descriptor = FindCommand(commandName);
            if (descriptor == null)
            {
                Console.WriteLine($"未找到命令：{commandName}");
                return 2;
            }

            Console.WriteLine(CommandRegistry.DescribeAsJson(descriptor));
            return 0;
        }

        private static int RunCommand(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            var commandName = args[1];
            var jsonPath = ReadOption(args, "--arguments-file");
            if (jsonPath == null)
            {
                PrintUsage();
                return 2;
            }

            var descriptor = FindCommand(commandName);
            if (descriptor == null)
            {
                var diagnostic = new CommandDiagnostic(
                    commandName,
                    "未找到命令",
                    "核对命令名，或先跑 toolkit-cmd.ps1 list 看有哪些命令",
                    "先跑 toolkit-cmd.ps1 list 看有哪些命令");
                EmitLog(commandName, "错误", diagnostic.ToString(), success: false);
                return 2;
            }

            if (!File.Exists(jsonPath))
            {
                var diagnostic = new CommandDiagnostic(
                    jsonPath,
                    "参数文件不存在",
                    "先把参数写进这个 JSON 文件，再重新执行",
                    "Tools/Cli/CommandHost/Commands/IndexCommands.cs");
                EmitLog(commandName, "错误", diagnostic.ToString(), success: false);
                return 2;
            }

            var json = File.ReadAllText(jsonPath);

            var diagnostics = CommandArgumentValidator.Validate(descriptor, json);
            if (diagnostics.Count > 0)
            {
                foreach (var diagnostic in diagnostics)
                {
                    EmitLog(commandName, "错误", diagnostic.ToString(), success: null);
                }

                EmitLog(commandName, "错误", $"参数校验失败，问题 {diagnostics.Count} 条", success: false);
                return 2;
            }

            var arguments = CommandArgumentBinder.Bind(descriptor, json);

            CommandExecutionContext.ProgressRootDirectory = ReadOption(args, "--progress-root") ?? Environment.CurrentDirectory;
            CommandExecutionContext.ArgumentsJson = json;

            // 命令抛异常时也要吐一条结构化日志再退出：挂机跑时裸栈回溯没人看，
            // 而调用方（gate.ps1 / 流水线）只认退出码与日志流。
            CommandResult result;
            try
            {
                result = descriptor.Invoke(arguments);
            }
            catch (TargetInvocationException exception)
            {
                var inner = exception.InnerException ?? exception;
                EmitLog(commandName, "错误", $"命令执行抛出 {inner.GetType().Name}：{inner.Message}", success: false);
                return 1;
            }

            foreach (var line in result.OutputLines)
            {
                EmitLog(commandName, "信息", line, success: null);
            }

            EmitLog(commandName, result.IsSuccess ? "信息" : "错误", result.Message, result.IsSuccess);

            return result.IsSuccess ? 0 : 1;
        }

        private static CommandDescriptor FindCommand(string commandName)
        {
            var commands = ScanAllCommands();
            return commands.FirstOrDefault(command => command.CommandName == commandName);
        }

        // 扫宿主自己的输出目录，而不是只扫宿主这一个程序集：
        // 工具库把 dll 放在宿主旁边就自带命令，不必为此改 CommandHost.csproj。
        private static IReadOnlyList<CommandDescriptor> ScanAllCommands()
        {
            return CommandRegistry.ScanDirectory(AppContext.BaseDirectory);
        }

        private static string ReadOption(string[] args, string optionName)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == optionName)
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static void EmitLog(string commandName, string level, string content, bool? success)
        {
            // 日志键面向人，按方案原则 1 用中文；但键名走字典而不是匿名类型的属性名，
            // 这样标识符仍然全是英文，两条规矩不打架。字典保序，输出的字段顺序稳定。
            var payload = new Dictionary<string, string>
            {
                ["时间"] = DateTime.Now.ToString("o"),
                ["级别"] = level,
                ["命令"] = commandName
            };

            // 结论行比普通日志行多一个「结果」。
            if (success.HasValue)
            {
                payload["结果"] = success.Value ? "成功" : "失败";
            }

            payload["内容"] = content;

            Console.WriteLine(JsonSerializer.Serialize(payload, LogOptions));
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：");
            Console.WriteLine("  unity-cmd list");
            Console.WriteLine("  unity-cmd describe <命令名>");
            Console.WriteLine("  unity-cmd run <命令名> --arguments-file <json路径>");
        }
    }

    /// <summary>命令执行上下文：把本轮进程的参数 JSON 与断点根目录传给命令实现。</summary>
    internal static class CommandExecutionContext
    {
        /// <summary>断点文件根目录，命令实现从这里推导断点文件路径。</summary>
        internal static string ProgressRootDirectory { get; set; }

        /// <summary>本轮命令的参数 JSON 原文，命令实现用它计算输入哈希。</summary>
        internal static string ArgumentsJson { get; set; }
    }
}
