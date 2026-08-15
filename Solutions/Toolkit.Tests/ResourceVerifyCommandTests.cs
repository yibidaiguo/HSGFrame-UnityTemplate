using System;
using System.IO;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>资源热更验收命令的前置检查：两个路径参数各自缺失时都要给四要素，而不是等到起完服务器才崩。</summary>
    public class ResourceVerifyCommandTests
    {
        [Fact]
        public void MissingBundlesDirectoryFailsWithAllFourParts()
        {
            var result = ResourceVerifyCommand.Execute(new ResourceVerifyArguments
            {
                BundlesDirectory = Path.Combine(Path.GetTempPath(), "没有这个目录-" + Guid.NewGuid().ToString("N")),
                PlayerPath = "C:/不存在/客户端.exe",
            });

            Assert.False(result.IsSuccess);
            Assert.Contains("位置：", result.Message);
            Assert.Contains("原因：", result.Message);
            Assert.Contains("修复：", result.Message);
            Assert.Contains("参考：", result.Message);
        }

        [Fact]
        public void MissingPlayerPathFailsBeforeStartingTheServer()
        {
            var bundlesDirectory = CreateTempDirectory();
            try
            {
                var result = ResourceVerifyCommand.Execute(new ResourceVerifyArguments
                {
                    BundlesDirectory = bundlesDirectory,
                    PlayerPath = Path.Combine(bundlesDirectory, "没出过包.exe"),
                });

                Assert.False(result.IsSuccess);
                Assert.Contains("客户端可执行文件不存在", result.Message);
            }
            finally
            {
                Directory.Delete(bundlesDirectory, true);
            }
        }

        [Fact]
        public void MissingBundlesDirectoryNamesTheBuildEntryInTheFix()
        {
            var result = ResourceVerifyCommand.Execute(new ResourceVerifyArguments
            {
                BundlesDirectory = null,
                PlayerPath = null,
            });

            Assert.False(result.IsSuccess);
            Assert.Contains("YooAssetBundleBuild", result.Message);
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "resource-verify-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
