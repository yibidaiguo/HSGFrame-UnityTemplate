using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>命令注册表的反射扫描与参数 schema 推导测试。</summary>
    public class CommandRegistryTests
    {
        [Fact]
        public void ScanAssembliesFindsAllCommands()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);
            var names = commands.Select(command => command.CommandName).ToList();

            Assert.True(commands.Count >= 2);
            Assert.Contains("compile.check", names);
            Assert.Contains("test.run", names);
        }

        [Fact]
        public void CompileCheckCommandDerivesParameterSchema()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);
            var descriptor = commands.Single(command => command.CommandName == "compile.check");

            Assert.Equal(2, descriptor.ParameterSchemas.Count);

            var solutionPath = descriptor.ParameterSchemas.Single(parameter => parameter.ParameterName == "SolutionPath");
            Assert.Equal("String", solutionPath.ParameterTypeName);
            Assert.True(solutionPath.IsRequired);
            Assert.False(string.IsNullOrEmpty(solutionPath.Description));

            var includeWarnings = descriptor.ParameterSchemas.Single(parameter => parameter.ParameterName == "IncludeWarnings");
            Assert.False(includeWarnings.IsRequired);
        }

        [Fact]
        public void CommandDescriptorSerializesToJson()
        {
            var commands = CommandRegistry.ScanAssemblies(typeof(CompileCheckCommand).Assembly);
            var descriptor = commands.Single(command => command.CommandName == "compile.check");

            var json = CommandRegistry.DescribeAsJson(descriptor);

            Assert.Contains("compile.check", json);
            Assert.Contains("SolutionPath", json);
        }

        [Fact]
        public void CompileCheckCommandFailsOnEmptySolutionPath()
        {
            var result = CompileCheckCommand.Execute(new CompileCheckArguments { SolutionPath = string.Empty });

            Assert.False(result.IsSuccess);
            Assert.Contains("必填", result.Message);
        }
    }
}
