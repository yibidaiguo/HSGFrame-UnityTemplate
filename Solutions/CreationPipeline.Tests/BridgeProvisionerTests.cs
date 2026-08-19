using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>BridgeProvisioner 的供给编排测试：干跑、产物落盘、幂等与内容核对。</summary>
    public class BridgeProvisionerTests
    {
        /// <summary>干跑不写任何文件，但 ProducedFiles 非空。</summary>
        [Fact]
        public void DryRunWritesNothingAndListsFiles()
        {
            using var workspace = PrepareWorkspace();

            var outcome = BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: true);

            Assert.True(outcome.IsDryRun);
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, "_Generated")));
            Assert.NotEmpty(outcome.ProducedFiles);
        }

        /// <summary>真跑之后四个产物文件都存在。</summary>
        [Fact]
        public void RunWritesFourCoreArtifacts()
        {
            using var workspace = PrepareWorkspace();

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);

            Assert.True(File.Exists(ProvisionPaths.TableDescriptionFile(workspace.Root, "测试驱动")));
            Assert.True(File.Exists(ProvisionPaths.EpicTableFile(workspace.Root, "测试驱动")));
            Assert.True(File.Exists(ProvisionPaths.ValidationMessageFile(workspace.Root, "测试驱动")));
            Assert.True(File.Exists(ProvisionPaths.FingerprintFile(workspace.Root, "测试驱动")));
        }

        /// <summary>真跑两次，建表描述.json 的内容逐字节相同（幂等）。</summary>
        [Fact]
        public void RunTwiceIsByteIdenticalForTableDescription()
        {
            using var workspace = PrepareWorkspace();
            var filePath = ProvisionPaths.TableDescriptionFile(workspace.Root, "测试驱动");

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);
            var first = File.ReadAllBytes(filePath);

            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);
            var second = File.ReadAllBytes(filePath);

            Assert.Equal(first, second);
        }

        /// <summary>指纹.json 里的 schema哈希 等于按当前 schema 现算的哈希。</summary>
        [Fact]
        public void FingerprintSchemaHashMatchesComputedValue()
        {
            using var workspace = PrepareWorkspace();
            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);

            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");
            var fingerprint = ProvisionFingerprint.Read(ProvisionPaths.FingerprintFile(workspace.Root, "测试驱动"));

            Assert.Equal(ProvisionFingerprint.ComputeSchemaHash(schema), fingerprint.SchemaHash);
        }

        /// <summary>校验错误文案.json 的条目条数等于目录条数。</summary>
        [Fact]
        public void ValidationMessageExportHasAllEntries()
        {
            using var workspace = PrepareWorkspace();
            BridgeProvisioner.Run(workspace.Root, workspace.Root, "测试驱动", isDryRun: false);

            using var document = JsonDocument.Parse(File.ReadAllText(ProvisionPaths.ValidationMessageFile(workspace.Root, "测试驱动")));
            var entries = document.RootElement.GetProperty("条目");

            Assert.Equal(ValidationMessageCatalog.Entries.Count, entries.GetArrayLength());
        }

        /// <summary>备一个池子与 driver 自述：基线 schema 写进池根，Bridges/测试驱动/driver.json 照 feishu 改名称。</summary>
        private static PoolTestWorkspace PrepareWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());

            var driverDirectory = Path.Combine(workspace.Root, "Bridges", "测试驱动");
            Directory.CreateDirectory(driverDirectory);
            File.WriteAllText(Path.Combine(driverDirectory, "driver.json"), DriverJson(), new UTF8Encoding(false));
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
