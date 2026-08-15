using System;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>热更四条命令的参数与前置检查测试。</summary>
    public class HotfixCommandsTests
    {
        // 这一条盯的是一个真出过的缺陷：[DefaultValue] 只让命令框架把参数判成选填，
        // 并不会把默认值填进参数对象。命令体里忘了自己兜底时，清单地址会退化成基地址本身，
        // 于是服务器上明明有清单，客户端却取不到。
        [Fact]
        public void UpdateFallsBackToTheDefaultManifestFileNameWhenTheArgumentIsMissing()
        {
            var result = HotfixCommands.Update(new HotfixUpdateArguments
            {
                // 端口指向一个不会有人监听的地址，命令会在取清单这一步失败，
                // 而失败消息里带着它拼出来的清单地址——那正是这条测试要看的东西。
                BaseUrl = "http://127.0.0.1:9/",
                LocalRoot = CreateTempDirectory(),
                ManifestPath = null,
            });

            Assert.False(result.IsSuccess);
            Assert.Contains(Uri.EscapeDataString(HotfixUpdateArguments.DefaultManifestFileName), result.Message);
        }

        [Fact]
        public void UpdateUsesTheGivenManifestFileNameWhenItIsProvided()
        {
            var result = HotfixCommands.Update(new HotfixUpdateArguments
            {
                BaseUrl = "http://127.0.0.1:9/",
                LocalRoot = CreateTempDirectory(),
                ManifestPath = "另一份清单.json",
            });

            Assert.False(result.IsSuccess);
            Assert.Contains(Uri.EscapeDataString("另一份清单.json"), result.Message);
        }

        [Fact]
        public void ManifestPathIsOptionalAndTheOthersAreRequired()
        {
            var command = CommandRegistry
                .ScanAssemblies(typeof(HotfixCommands).Assembly)
                .Single(candidate => candidate.CommandName == "hotfix.update");

            var required = command.ParameterSchemas
                .Where(parameter => parameter.IsRequired)
                .Select(parameter => parameter.ParameterName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(new[] { "BaseUrl", "LocalRoot" }, required);
        }

        [Fact]
        public void ManifestFailsWithFourElementsWhenThePackageDirectoryIsMissing()
        {
            var result = HotfixCommands.Manifest(new HotfixManifestArguments
            {
                PackageDirectory = Path.Combine(Path.GetTempPath(), "没有这个目录-" + Guid.NewGuid().ToString("N")),
                VersionText = "1.0.0",
                OutputPath = Path.Combine(Path.GetTempPath(), "清单.json"),
            });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void ManifestFailsWithFourElementsWhenTheVersionShapeIsWrong()
        {
            var result = HotfixCommands.Manifest(new HotfixManifestArguments
            {
                PackageDirectory = CreateTempDirectory(),
                VersionText = "一点二",
                OutputPath = Path.Combine(Path.GetTempPath(), "清单.json"),
            });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void StatusOnAFreshRootReportsNoInstalledVersion()
        {
            var result = HotfixCommands.Status(new HotfixStatusArguments { LocalRoot = CreateTempDirectory() });

            Assert.True(result.IsSuccess);
            Assert.Contains("历史版本 0 个", result.Message);
        }

        [Fact]
        public void RollbackOnAFreshRootFailsInsteadOfThrowing()
        {
            var result = HotfixCommands.Rollback(new HotfixRollbackArguments { LocalRoot = CreateTempDirectory() });

            Assert.False(result.IsSuccess);
            Assert.Contains("回退", result.Message);
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "HotfixCommandsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void AssertFailureWithFourElements(CommandResult result)
        {
            Assert.False(result.IsSuccess);
            Assert.Contains("位置", result.Message);
            Assert.Contains("原因", result.Message);
            Assert.Contains("修复", result.Message);
            Assert.Contains("参考", result.Message);
        }
    }
}
