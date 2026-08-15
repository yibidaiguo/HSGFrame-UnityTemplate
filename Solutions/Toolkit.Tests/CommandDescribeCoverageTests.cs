using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>命令注册表覆盖测试：每条命令都能 describe 且说明齐全。</summary>
    public class CommandDescribeCoverageTests
    {
        [Fact]
        public void ScanAssembliesFindsAtLeastSixteenCommands()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);

            Assert.True(commands.Count >= 16);
        }

        [Fact]
        public void EveryCommandDescribesToParseableJson()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);

            foreach (var command in commands)
            {
                var json = CommandRegistry.DescribeAsJson(command);
                using var document = JsonDocument.Parse(json);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            }
        }

        [Fact]
        public void EveryCommandHasDescription()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);

            foreach (var command in commands)
            {
                Assert.False(string.IsNullOrWhiteSpace(command.Description), $"命令 {command.CommandName} 缺 Summary");
            }
        }

        [Fact]
        public void EveryParameterHasDescription()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);

            foreach (var command in commands)
            {
                foreach (var parameter in command.ParameterSchemas)
                {
                    Assert.False(
                        string.IsNullOrWhiteSpace(parameter.Description),
                        $"命令 {command.CommandName} 的参数 {parameter.ParameterName} 缺 Summary");
                }
            }
        }
    }
}
