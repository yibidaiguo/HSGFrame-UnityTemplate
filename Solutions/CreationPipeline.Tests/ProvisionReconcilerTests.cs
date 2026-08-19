using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 供给对账测试：Bridges 缺失、指纹对上、哈希失配、自述损坏不中断。
    /// setup 一律真跑一次 BridgeProvisioner——供给是最后才写指纹的，
    /// 「有指纹却一份产物都没有」这种状态现实中不存在，拿它当前提测出来的绿是假绿。
    /// </summary>
    public class ProvisionReconcilerTests
    {
        /// <summary>Bridges/ 不存在时返回空报告且不抛——新项目还没接 driver 是正常状态。</summary>
        [Fact]
        public void ReconcileReturnsEmptyReportWhenBridgesDirectoryMissing()
        {
            using var workspace = new PoolTestWorkspace();

            var report = ProvisionReconciler.Reconcile(workspace.Root, workspace.Root);

            Assert.Empty(report.DriverNames);
            Assert.Equal(0, report.ProvisionedCount);
            Assert.Empty(report.Findings);
        }

        /// <summary>一个 driver 有合法自述与对上哈希的指纹 → 零发现、ProvisionedCount 为 1。</summary>
        [Fact]
        public void ReconcileReportsCleanWhenFingerprintMatches()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            WriteDriverDescriptor(workspace.Root, "demo", true);

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "demo", false);

            var report = ProvisionReconciler.Reconcile(workspace.Root, workspace.Root);

            Assert.Equal(new[] { "demo" }, report.DriverNames.ToArray());
            Assert.Equal(1, report.ProvisionedCount);
            Assert.Empty(report.Findings);
        }

        /// <summary>指纹里的 schema 哈希改坏 → 报一条失配，ProvisionedCount 仍为 1。</summary>
        [Fact]
        public void ReconcileReportsSchemaHashMismatch()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            WriteDriverDescriptor(workspace.Root, "demo", true);

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "demo", false);

            var digestHash = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);
            ProvisionFingerprint.Create("demo", ">=1.0 <2.0", "改坏了", digestHash)
                .WriteTo(ProvisionPaths.FingerprintFile(workspace.Root, "demo"));

            var report = ProvisionReconciler.Reconcile(workspace.Root, workspace.Root);

            var finding = Assert.Single(report.Findings);
            Assert.Contains("schema 哈希", finding.Reason);
            Assert.Equal(1, report.ProvisionedCount);
        }

        /// <summary>driver.json 缺必填字段 → 报一条且位置指向该 driver.json，其他 driver 仍被扫到。</summary>
        [Fact]
        public void ReconcileContinuesWhenDescriptorIsInvalid()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            WriteDriverDescriptor(workspace.Root, "broken", false);

            WriteDriverDescriptor(workspace.Root, "good", true);
            BridgeProvisioner.Run(workspace.Root, workspace.Root, "good", false);

            var report = ProvisionReconciler.Reconcile(workspace.Root, workspace.Root);

            Assert.Equal(new[] { "broken", "good" }, report.DriverNames.ToArray());
            Assert.Equal(1, report.ProvisionedCount);
            var finding = Assert.Single(report.Findings);
            Assert.Contains("实现", finding.Reason);
            Assert.Contains("Bridges/broken/driver.json", finding.Location);
        }

        /// <summary>写一份 driver 自述；缺实现字段时必填校验会拦下来。</summary>
        private static void WriteDriverDescriptor(string repositoryRoot, string driverName, bool includeImplementation)
        {
            var directory = Path.Combine(repositoryRoot, "Bridges", driverName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "driver.json"),
                DriverDescriptorJson(driverName, includeImplementation),
                new UTF8Encoding(false));
        }

        /// <summary>拼一份 driver 自述 JSON：占位符替换避开内插 raw string 的花括号转义。</summary>
        private static string DriverDescriptorJson(string driverName, bool includeImplementation)
        {
            var template = """
                {
                  "名称": "__NAME__",
                  "port": ["需求编辑端"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "__IMPLEMENTATION__"
                  "字段类型映射": { "string": "文本" }
                }
                """;
            var implementation = includeImplementation ? "  \"实现\": \"bridge-demo\"," : "";
            return template
                .Replace("__NAME__", driverName)
                .Replace("  \"__IMPLEMENTATION__\"", implementation);
        }
    }
}
