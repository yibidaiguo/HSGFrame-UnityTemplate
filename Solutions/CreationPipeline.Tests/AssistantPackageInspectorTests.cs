using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>AssistantPackageInspector 的供给产物完整性检查测试：未供给、全齐、缺失、空文件与对账接线。</summary>
    public class AssistantPackageInspectorTests
    {
        /// <summary>产物目录整个不存在：判定为未供给，发现为空，但 11 份产物状态照常列出且全缺。</summary>
        [Fact]
        public void InspectReportsNotProvisionedWhenDirectoryMissing()
        {
            using var workspace = new PoolTestWorkspace();

            var inspection = AssistantPackageInspector.Inspect(workspace.Root, "demo");

            Assert.Empty(inspection.Findings);
            Assert.Equal(13, inspection.Artifacts.Count);
            Assert.Equal(13, inspection.MissingCount);
            Assert.Equal(0, inspection.EmptyCount);
            Assert.All(inspection.Artifacts, artifact => Assert.False(artifact.Exists));
        }

        /// <summary>真跑一次供给之后：11 份产物齐全，缺失与空文件都是 0，无发现。</summary>
        [Fact]
        public void InspectReportsAllPresentAfterProvision()
        {
            using var workspace = PrepareProvisionedWorkspace();

            var inspection = AssistantPackageInspector.Inspect(workspace.Root, "测试驱动");

            Assert.Equal(0, inspection.MissingCount);
            Assert.Equal(0, inspection.EmptyCount);
            Assert.Empty(inspection.Findings);
            Assert.All(inspection.Artifacts, artifact => Assert.True(artifact.Exists));
        }

        /// <summary>手工删掉glossary.md：缺失 1 份，出一条原因含「缺失」的发现，位置指向被删产物。</summary>
        [Fact]
        public void InspectReportsMissingArtifact()
        {
            using var workspace = PrepareProvisionedWorkspace();
            File.Delete(Path.Combine(workspace.Root, "_Generated", "Bridges", "测试驱动", "assistant-package", "knowledge", "glossary.md"));

            var inspection = AssistantPackageInspector.Inspect(workspace.Root, "测试驱动");

            Assert.Equal(1, inspection.MissingCount);
            var finding = Assert.Single(inspection.Findings);
            Assert.Contains("缺失", finding.Reason);
            Assert.Contains("glossary.md", finding.Location);
        }

        /// <summary>把table-description.json 清空成 0 字节：空文件 1 份，出一条原因含「空文件」的发现。</summary>
        [Fact]
        public void InspectReportsEmptyArtifact()
        {
            using var workspace = PrepareProvisionedWorkspace();
            File.WriteAllText(
                Path.Combine(workspace.Root, "_Generated", "Bridges", "测试驱动", "table-description.json"),
                "",
                new UTF8Encoding(false));

            var inspection = AssistantPackageInspector.Inspect(workspace.Root, "测试驱动");

            Assert.Equal(1, inspection.EmptyCount);
            var finding = Assert.Single(inspection.Findings);
            Assert.Contains("空文件", finding.Reason);
            Assert.Contains("table-description.json", finding.Location);
        }

        /// <summary>每份产物的导入提示都非空白，人工导入清单不会缺项。</summary>
        [Fact]
        public void EveryArtifactHasImportHint()
        {
            using var workspace = new PoolTestWorkspace();

            var inspection = AssistantPackageInspector.Inspect(workspace.Root, "demo");

            Assert.Equal(13, inspection.Artifacts.Count);
            Assert.All(inspection.Artifacts, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.ImportHint)));
        }

        /// <summary>对账时产物被删一份也能报出来：指纹对得上但产物缺失照样红，证明 Inspector 已接入 Reconcile。</summary>
        [Fact]
        public void ReconcileReportsMissingArtifact()
        {
            using var workspace = PrepareProvisionedWorkspace();
            File.Delete(Path.Combine(workspace.Root, "_Generated", "Bridges", "测试驱动", "assistant-package", "knowledge", "glossary.md"));

            var report = ProvisionReconciler.Reconcile(workspace.Root, workspace.Root);

            var finding = Assert.Single(report.Findings);
            Assert.Contains("缺失", finding.Reason);
            Assert.Contains("glossary.md", finding.Location);
        }

        /// <summary>备一个已供给的工作区：基线 schema、driver 自述与真跑一次供给。</summary>
        private static PoolTestWorkspace PrepareProvisionedWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());

            var driverDirectory = Path.Combine(workspace.Root, "Bridges", "测试驱动");
            Directory.CreateDirectory(driverDirectory);
            File.WriteAllText(Path.Combine(driverDirectory, "driver.json"), DriverJson(), new UTF8Encoding(false));

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);
            return workspace;
        }

        /// <summary>一份字段齐全的 driver 自述，名称用「测试驱动」。</summary>
        private static string DriverJson()
        {
            return """
            {
              "名称": "测试驱动",
              "port": ["需求编辑端", "消息卡片", "助手"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {},
              "密钥字段": [],
              "试跑": "bridge.provision --driver 测试驱动 --dry-run",
              "实现": "bridge-测试驱动",
              "字段类型映射": {
                "string": "文本",
                "number": "数字",
                "bool": "复选框",
                "enum": "单选",
                "array": "多行文本",
                "object": "多行文本"
              },
              "表单分组字段": "类型"
            }
            """;
        }
    }
}
