using System;
using System.IO;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>gate.meta 从非仓库根的工作目录调用时也要能找到 gate-config.json。</summary>
    public class GateCommandConfigurationPathTests
    {
        // 改动前这条会红：ResolveConfigurationPath 见到非空相对路径原样返回，
        // 于是 <AssetsRoot>/Tools/Gates/Config/gate-config.json 按进程工作目录解析，
        // 在临时目录里根本不存在，LoadFromFile 抛 FileNotFoundException。
        [Fact]
        public void MetaGateFindsConfigurationFromAssetsRootWhenWorkingDirectoryIsElsewhere()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GateConfigPathTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var configDirectory = Path.Combine(tempRoot, "Tools", "Gates", "Config");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(
                    Path.Combine(configDirectory, "gate-config.json"),
                    "{\"documentLineLimit\":200,\"sourceScanSkipSegments\":[]}");

                var assetsRoot = Path.Combine(tempRoot, "UnityProject", "Assets");
                Directory.CreateDirectory(assetsRoot);
                File.WriteAllText(Path.Combine(assetsRoot, "示例.txt"), "内容");
                File.WriteAllText(Path.Combine(assetsRoot, "示例.txt.meta"), "meta");

                var result = GateMetaCommand.CheckMeta(new MetaGateArguments
                {
                    // ConfigurationPath 留空不设，正是「按默认值走」的场景。
                    AssetsRootDirectory = assetsRoot,
                });

                Assert.True(result.IsSuccess);
                Assert.Contains("问题 0 条", result.Message);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
