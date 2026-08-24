using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 下游配置判据的测试：**全仓只有这一处判「配没配」**，桥接包页、下游页、
    /// bridge.inventory 都调它。盯的是那次真出过的错——
    /// 台账里已经有的对象被报成「未配」，而人照提示手填进 local.json 还不生效。
    /// </summary>
    public class DriverConfigurationViewTests
    {
        /// <summary>本机配置里有值 → 已配，且不是台账托管。</summary>
        [Fact]
        public void LocalValueIsConfiguredAndNotLedgerOwned()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{"应用标识":"cli_x"}}}""");

            var cell = DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "应用标识");

            Assert.Equal("cli_x", cell.Value);
            Assert.True(cell.IsConfigured);
            Assert.False(cell.IsLedgerOwned);
        }

        /// <summary>
        /// 只在台账里的对象 id 也算**已配**，并标成台账托管。
        /// 这一条就是那次真出的错：bridge.ensure 建好回填了，两个页面却都报「未配」。
        /// </summary>
        [Fact]
        public void LedgerOnlyValueIsConfiguredAndLedgerOwned()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{}}}""");
            WriteLedger(workspace.RepositoryRoot, """{"对象":{"feishu":{"任务表标识":"tblABC"}}}""");

            var cell = DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "任务表标识");

            Assert.Equal("tblABC", cell.Value);
            Assert.True(cell.IsConfigured);
            Assert.True(cell.IsLedgerOwned);
        }

        /// <summary>
        /// 两处都有时**以台账为准**——必须与 BridgeInvoker 的取值顺序一致。
        /// 面板显示的值与真正调用时用的值不是同一个，面板就是在骗人。
        /// </summary>
        [Fact]
        public void LedgerOverridesLocalValue()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{"任务表标识":"tbl旧的"}}}""");
            WriteLedger(workspace.RepositoryRoot, """{"对象":{"feishu":{"任务表标识":"tbl台账的"}}}""");

            var cell = DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "任务表标识");

            Assert.Equal("tbl台账的", cell.Value);
            Assert.True(cell.IsLedgerOwned);
        }

        /// <summary>两处都没有 → 未配。</summary>
        [Fact]
        public void MissingEverywhereIsNotConfigured()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{}}}""");

            var cell = DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "任务表标识");

            Assert.Equal("", cell.Value);
            Assert.False(cell.IsConfigured);
            Assert.Equal(DriverConfigurationView.NotConfigured, DriverConfigurationView.StateOf(cell));
        }

        /// <summary>空串与键缺失同判「未配」——留空串会让页面显示「已配」，那是假绿（决策 78）。</summary>
        [Fact]
        public void EmptyStringCountsAsNotConfigured()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{"任务表标识":""}}}""");

            Assert.False(DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "任务表标识").IsConfigured);
        }

        /// <summary>密钥只判键在不在，**值恒为空串**——一次都不许往外带。</summary>
        [Fact]
        public void SecretReportsPresenceButNeverTheValue()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"飞书应用密钥":"绝不该出现在返回里"}""");

            var cell = DriverConfigurationView.ResolveSecret(workspace.RepositoryRoot, "飞书应用密钥");

            Assert.True(cell.IsConfigured);
            Assert.Equal("", cell.Value);
            Assert.False(cell.IsLedgerOwned);
        }

        /// <summary>本机配置文件不存在时一律「未配」，不抛。</summary>
        [Fact]
        public void MissingLocalFileIsNotConfigured()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.False(DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "应用标识").IsConfigured);
            Assert.False(DriverConfigurationView.ResolveSecret(workspace.RepositoryRoot, "飞书应用密钥").IsConfigured);
        }

        /// <summary>数字与布尔也算值（转成字符串判非空），不因为类型不是字符串就判成没配。</summary>
        [Fact]
        public void NumberAndBooleanCountAsConfigured()
        {
            using var workspace = new PoolTestWorkspace();
            WriteLocal(workspace.RepositoryRoot, """{"下游配置":{"feishu":{"超时秒":60,"开关":true}}}""");

            Assert.True(DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "超时秒").IsConfigured);
            Assert.True(DriverConfigurationView.Resolve(workspace.RepositoryRoot, "feishu", "开关").IsConfigured);
        }

        /// <summary>写一份本机配置。</summary>
        private static void WriteLocal(string repositoryRoot, string json)
        {
            var filePath = Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }

        /// <summary>写一份下游对象台账。</summary>
        private static void WriteLedger(string repositoryRoot, string json)
        {
            var filePath = DownstreamObjectLedger.LedgerFile(repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }
    }
}
