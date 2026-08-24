using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>
    /// 每个 driver 自述里那条「试跑」命令，必填参数**必须一个不缺**。
    ///
    /// 这条断言是真踩出来的：tripo 与 tripocli 都把试跑声明成
    /// <c>bridge.model --Driver … --DryRun true</c>，而 <c>bridge.model</c> 的
    /// <c>OutputDirectory</c> 是必填。于是面板桥接包页上那颗「试跑一次」按钮
    /// **点下去必然失败**，报的还是「必填参数缺失」——
    /// 人看到的是「这个下游连不上」，而真实情况是下游好端端的，只是按钮给错了命令。
    ///
    /// 试跑的语义是「便宜地验一下这条路通不通」，所以它天然该是 probe 那一类；
    /// 真要生成东西的命令带着必填的落点，本来就不该当试跑用。
    /// </summary>
    public class DriverSmokeCommandTests
    {
        /// <summary>自述里那个键名。</summary>
        private const string SmokeKey = "试跑";

        [Fact]
        public void EverySmokeCommandHasAllRequiredArgumentsFilled()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly)
                .ToDictionary(command => command.CommandName, StringComparer.Ordinal);

            var problems = new List<string>();
            foreach (var (driverName, smokeLine) in ReadSmokeCommands())
            {
                var tokens = smokeLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (!commands.TryGetValue(tokens[0], out var descriptor))
                {
                    problems.Add($"{driverName} 的试跑命令「{tokens[0]}」根本不存在");
                    continue;
                }

                var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 1; index < tokens.Length; index++)
                {
                    if (tokens[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        supplied.Add(tokens[index].Substring(2));
                    }
                }

                var missing = descriptor.ParameterSchemas
                    .Where(parameter => parameter.IsRequired && !supplied.Contains(parameter.ParameterName))
                    .Select(parameter => parameter.ParameterName)
                    .ToList();

                if (missing.Count > 0)
                {
                    problems.Add($"{driverName} 的试跑「{smokeLine}」缺必填参数：{string.Join("、", missing)}");
                }
            }

            Assert.True(problems.Count == 0, string.Join("；", problems));
        }

        /// <summary>试跑命令必须真的存在于命令层里（拼错命令名与缺参数是两种错，分开报）。</summary>
        [Fact]
        public void EverySmokeCommandNamesAKnownCommand()
        {
            var names = new HashSet<string>(
                CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly).Select(command => command.CommandName),
                StringComparer.Ordinal);

            foreach (var (driverName, smokeLine) in ReadSmokeCommands())
            {
                var head = smokeLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                Assert.True(names.Contains(head), $"{driverName} 的试跑命令「{head}」不在命令层里");
            }
        }

        /// <summary>至少读到几个 driver，否则这两条断言在空表上「通过」而什么都没验（决策 42）。</summary>
        [Fact]
        public void SmokeCommandsAreActuallyFound()
        {
            Assert.True(ReadSmokeCommands().Count >= 4, "读不到几份 driver 自述，这组断言等于没跑");
        }

        /// <summary>读全部 Bridges/&lt;名&gt;/driver.json 里的「试跑」；没声明的跳过。</summary>
        private static IReadOnlyList<(string Driver, string Smoke)> ReadSmokeCommands()
        {
            var bridgesRoot = FindBridgesRoot();
            var result = new List<(string, string)>();
            if (bridgesRoot == null)
            {
                return result;
            }

            foreach (var directory in Directory.GetDirectories(bridgesRoot).OrderBy(path => path, StringComparer.Ordinal))
            {
                var descriptorPath = Path.Combine(directory, "driver.json");
                if (!File.Exists(descriptorPath))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(descriptorPath));
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty(SmokeKey, out var smoke)
                        && smoke.ValueKind == JsonValueKind.String)
                    {
                        var text = smoke.GetString() ?? "";
                        if (text.Trim().Length > 0)
                        {
                            result.Add((Path.GetFileName(directory), text.Trim()));
                        }
                    }
                }
                catch (JsonException)
                {
                    // 自述坏掉是别的门禁的事（下游边界 / 供给对账），这一组只管试跑那一格。
                }
            }

            return result;
        }

        /// <summary>从测试运行目录往上找 Bridges 目录；找不到给 null。</summary>
        private static string FindBridgesRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Bridges");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
