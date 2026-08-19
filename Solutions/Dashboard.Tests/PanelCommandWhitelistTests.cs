using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板命令白名单判定测试：只验证判定，不起任何进程。</summary>
    public class PanelCommandWhitelistTests
    {
        /// <summary>pool.validate 在池子命令族里，放行。</summary>
        [Fact]
        public void PoolCommandIsAllowed()
        {
            Assert.True(PanelCommandWhitelist.IsAllowed("pool.validate --PoolRoot Pools", out var commandName, out var reason));

            Assert.Equal("pool.validate", commandName);
            Assert.Equal("", reason);
        }

        /// <summary>rm -rf / 拒绝。</summary>
        [Fact]
        public void RmCommandIsRejected()
        {
            Assert.False(PanelCommandWhitelist.IsAllowed("rm -rf /", out _, out var reason));
            Assert.NotEqual("", reason);
        }

        /// <summary>git status 拒绝且原因提到白名单。</summary>
        [Fact]
        public void GitCommandIsRejectedWithWhitelistMention()
        {
            Assert.False(PanelCommandWhitelist.IsAllowed("git status", out _, out var reason));
            Assert.Contains("白名单", reason);
        }

        /// <summary>空字符串拒绝。</summary>
        [Fact]
        public void EmptyCommandLineIsRejected()
        {
            Assert.False(PanelCommandWhitelist.IsAllowed("", out _, out var reason));
            Assert.Equal("命令行为空", reason);
        }

        /// <summary>命令名里带竖线拒绝。</summary>
        [Fact]
        public void CommandNameWithPipeIsRejected()
        {
            Assert.False(PanelCommandWhitelist.IsAllowed("task.|anything", out _, out var reason));
            Assert.Contains("|", reason);
        }

        /// <summary>501 个字符的命令行拒绝。</summary>
        [Fact]
        public void OverlongCommandLineIsRejected()
        {
            var commandLine = "task." + new string('a', 496);

            Assert.False(PanelCommandWhitelist.IsAllowed(commandLine, out _, out var reason));
            Assert.Equal("命令行超过 500 字符", reason);
        }
    }
}
