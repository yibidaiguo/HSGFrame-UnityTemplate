using System;
using System.IO;
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

        [Fact]
        public void ScanDirectoryFindsCommandsFromHostOutputDirectory()
        {
            // 宿主现在扫自己的输出目录而不是只扫宿主程序集：输出目录里躺着
            // 第三方与无关 dll，能加载的那部分全要数进来，所以只多不少。
            var directoryCommands = CommandRegistry.ScanDirectory(AppContext.BaseDirectory);
            var assemblyCommands = CommandRegistry.ScanAssemblies(typeof(Template.Toolkit.CommandHost.Program).Assembly);

            Assert.True(
                directoryCommands.Count >= assemblyCommands.Count,
                "扫输出目录没找到宿主自带的命令");
            Assert.Contains("gate.doc", directoryCommands.Select(command => command.CommandName).ToList());
        }

        [Fact]
        public void ScanDirectoryReturnsEmptyForMissingDirectory()
        {
            // 不存在的目录返回空列表而不是抛异常：目录扫描是增量手段，
            // 输出目录偶发不存在时命令层不该因此起不来。
            var missingDirectory = Path.Combine(
                Path.GetTempPath(),
                "不存在的目录_" + Guid.NewGuid().ToString("N"));

            var commands = CommandRegistry.ScanDirectory(missingDirectory);

            Assert.Empty(commands);
        }
    }
}
