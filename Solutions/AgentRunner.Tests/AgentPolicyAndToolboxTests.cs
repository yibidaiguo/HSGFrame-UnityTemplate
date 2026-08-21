using System;
using System.IO;
using Template.Toolkit.AgentRunner;
using Xunit;

namespace Template.Toolkit.AgentRunnerTests
{
    /// <summary>围栏策略与工具箱测试：路径逃逸、写盘拒绝、命令白名单与截断，全部不碰网络。</summary>
    public class AgentPolicyAndToolboxTests : IDisposable
    {
        private readonly string _root;

        /// <summary>建一棵临时仓库树当工具箱的活动范围。</summary>
        public AgentPolicyAndToolboxTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentRunnerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>清掉临时树。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        private static AgentPolicy MakePolicy()
        {
            return new AgentPolicy(
                commandAllowPrefixes: new[] { "dotnet test", "git status" },
                writeDenyPrefixes: new[] { "Tools/Gates/Config/" },
                writeDenyFiles: new[] { ".gitignore" },
                commandTimeoutSeconds: 60,
                commandOutputLimit: 100,
                fileReadLimit: 50);
        }

        /// <summary>../ 逃出仓库根的路径被拒，read/write 都进不来。</summary>
        [Fact]
        public void PathEscapeIsRejected()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: true);

            Assert.Contains("逃出仓库根", toolbox.ReadFile("../outside.txt"));
            Assert.Contains("逃出仓库根", toolbox.WriteFile("../outside.txt", "x"));
        }

        /// <summary>写盘拒绝清单命中：前缀命中与全字文件名命中都拒，理由里说清是围栏。</summary>
        [Fact]
        public void WriteDenyListBlocksConfiguredPaths()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: true);

            Assert.Contains("围栏拒绝", toolbox.WriteFile("Tools/Gates/Config/gate-config.json", "{}"));
            Assert.Contains("围栏拒绝", toolbox.WriteFile(".gitignore", "junk"));
        }

        /// <summary>正常写入成功且能读回；读超上限截头留尾并注明截断。</summary>
        [Fact]
        public void WriteThenReadRoundTripsAndTruncates()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: true);

            var writeResult = toolbox.WriteFile("sub/sample.txt", new string('a', 80));
            Assert.Contains("已写入", writeResult);

            var readResult = toolbox.ReadFile("sub/sample.txt");
            Assert.Contains("截断", readResult);
            Assert.True(readResult.Length < 90, "截断后仍超出上限太多");
        }

        /// <summary>只读工具箱：write_file 不在工具声明里，直接调用也被拒。</summary>
        [Fact]
        public void ReadOnlyToolboxRefusesWrites()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: false);

            Assert.DoesNotContain("write_file", toolbox.BuildToolDefinitions().ToJsonString());
            Assert.Contains("只读", toolbox.Execute("write_file", "{\"path\":\"a.txt\",\"content\":\"x\"}"));
            Assert.False(File.Exists(Path.Combine(_root, "a.txt")));
        }

        /// <summary>命令白名单：不在前缀清单里的命令被拒且不执行。</summary>
        [Fact]
        public void CommandOutsideWhitelistIsRejected()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: true);

            var result = toolbox.RunCommand("git commit -m x");
            Assert.Contains("围栏拒绝", result);
        }

        /// <summary>未知工具名与坏参数 JSON 都回说明文本，不抛异常。</summary>
        [Fact]
        public void UnknownToolAndBadArgumentsReturnText()
        {
            var toolbox = new AgentToolbox(_root, MakePolicy(), allowWrite: true);

            Assert.Contains("没有叫", toolbox.Execute("format_disk", "{}"));
            Assert.Contains("不是合法 JSON", toolbox.Execute("read_file", "not json"));
        }

        /// <summary>策略文件缺失时 TryLoad 失败并给出路径——没有围栏就不许放执行端出去。</summary>
        [Fact]
        public void PolicyLoadFailsWhenFileMissing()
        {
            var loaded = AgentPolicy.TryLoad(_root, out var policy, out var reason);

            Assert.False(loaded);
            Assert.Null(policy);
            Assert.Contains("agent-policy.json", reason);
        }

        /// <summary>策略文件正常读入：三张清单与三个上限都到位。</summary>
        [Fact]
        public void PolicyLoadReadsConfiguredValues()
        {
            var configDirectory = Path.Combine(_root, "Tools", "AgentRunner", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "agent-policy.json"), """
                {
                  "命令白名单前缀": ["dotnet test"],
                  "写盘拒绝前缀": ["Tools/Gates/Config/"],
                  "写盘拒绝文件": [".gitignore"],
                  "命令超时秒": 42,
                  "命令输出上限字符": 1000,
                  "读文件上限字符": 2000
                }
                """);

            var loaded = AgentPolicy.TryLoad(_root, out var policy, out _);

            Assert.True(loaded);
            Assert.True(policy.IsCommandAllowed("dotnet test Solutions/Template.sln"));
            Assert.False(policy.IsCommandAllowed("dotnet run x"));
            Assert.True(policy.IsWriteDenied("Tools/Gates/Config/test-baseline.json"));
            Assert.True(policy.IsWriteDenied(".gitignore"));
            Assert.False(policy.IsWriteDenied("Tools/AgentRunner/AgentLoop.cs"));
            Assert.Equal(42, policy.CommandTimeoutSeconds);
        }
    }
}
