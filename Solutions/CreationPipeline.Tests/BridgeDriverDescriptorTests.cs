using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>BridgeDriverDescriptor 的读取、校验与类型映射测试。</summary>
    public class BridgeDriverDescriptorTests
    {
        /// <summary>完整自述文件读出来每个属性都对得上。</summary>
        [Fact]
        public void LoadReadsAllProperties()
        {
            using var workspace = new PoolTestWorkspace();
            var descriptor = WriteDriver(workspace, MinimalDriverJson());

            Assert.Equal("feishu", descriptor.Name);
            Assert.Equal(new[] { "需求编辑端", "消息卡片", "助手" }, descriptor.Ports);
            Assert.Equal("线上", descriptor.Form);
            Assert.Equal(">=1.0 <2.0", descriptor.ContractRange);
            Assert.Equal(new[] { "飞书应用密钥" }, descriptor.SecretFieldNames);
            Assert.Equal("bridge.provision --driver feishu --dry-run", descriptor.TrialCommand);
            Assert.Equal("bridge-feishu", descriptor.ImplementationName);
            Assert.Equal("文本", descriptor.FieldTypeMapping["string"]);
            Assert.Equal("单选", descriptor.FieldTypeMapping["enum"]);
            Assert.Equal("类型", descriptor.FormGroupingField);
            Assert.Equal(new[] { "多维表格标识", "应用标识", "超时秒" }, descriptor.ConfigurationFieldNames);
        }

        /// <summary>自述文件不存在时抛 InvalidOperationException，消息带完整路径。</summary>
        [Fact]
        public void LoadThrowsWhenFileMissing()
        {
            using var workspace = new PoolTestWorkspace();

            var exception = Assert.Throws<InvalidOperationException>(
                () => BridgeDriverDescriptor.Load(workspace.Root, "feishu"));
            Assert.Contains("找不到 driver 自述文件", exception.Message);
            Assert.Contains(Path.Combine(workspace.Root, "Bridges", "feishu"), exception.Message);
        }

        /// <summary>自述里的名称与目录名不一致时抛。</summary>
        [Fact]
        public void LoadThrowsWhenNameMismatchesDirectory()
        {
            using var workspace = new PoolTestWorkspace();
            WriteDriverFile(workspace, MinimalDriverJson().Replace("\"名称\": \"feishu\"", "\"名称\": \"其他\"", StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(
                () => BridgeDriverDescriptor.Load(workspace.Root, "feishu"));
            Assert.Contains("与目录名", exception.Message);
        }

        /// <summary>缺 实现 字段时抛，消息带字段名。</summary>
        [Fact]
        public void LoadThrowsWhenImplementationMissing()
        {
            using var workspace = new PoolTestWorkspace();
            var json = MinimalDriverJson().Replace("\"实现\": \"bridge-feishu\",", "", StringComparison.Ordinal);
            WriteDriverFile(workspace, json);

            var exception = Assert.Throws<InvalidOperationException>(
                () => BridgeDriverDescriptor.Load(workspace.Root, "feishu"));
            Assert.Contains("实现", exception.Message);
        }

        /// <summary>形态写成 云端 时抛，消息带合法形态提示。</summary>
        [Fact]
        public void LoadThrowsWhenFormIsUnknown()
        {
            using var workspace = new PoolTestWorkspace();
            WriteDriverFile(workspace, MinimalDriverJson().Replace("\"形态\": \"线上\"", "\"形态\": \"云端\"", StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(
                () => BridgeDriverDescriptor.Load(workspace.Root, "feishu"));
            Assert.Contains("形态只能是「线上」或「本地」", exception.Message);
        }

        /// <summary>JSON 语法坏掉时抛，消息带「不是合法 JSON」。</summary>
        [Fact]
        public void LoadThrowsWhenJsonIsBroken()
        {
            using var workspace = new PoolTestWorkspace();
            WriteDriverFile(workspace, "{ 这不是 JSON");

            var exception = Assert.Throws<InvalidOperationException>(
                () => BridgeDriverDescriptor.Load(workspace.Root, "feishu"));
            Assert.Contains("不是合法 JSON", exception.Message);
        }

        /// <summary>未知逻辑类型退化到 string 的映射值；映射表连 string 都没有时返回原样。</summary>
        [Fact]
        public void MapFieldTypeFallsBackToStringMappingThenToInput()
        {
            using var workspace = new PoolTestWorkspace();
            WriteDriverFile(workspace, MinimalDriverJson());
            var descriptor = BridgeDriverDescriptor.Load(workspace.Root, "feishu");

            Assert.Equal("文本", descriptor.MapFieldType("boolean"));
            Assert.Equal("单选", descriptor.MapFieldType("enum"));
            Assert.Equal("文本", descriptor.MapFieldType("string"));

            var json = MinimalDriverJson().Replace(
                "\"string\": \"文本\",",
                "\"number\": \"数字\",",
                StringComparison.Ordinal);
            WriteDriverFile(workspace, json);
            var descriptorWithoutString = BridgeDriverDescriptor.Load(workspace.Root, "feishu");
            Assert.Equal("zzz", descriptorWithoutString.MapFieldType("zzz"));
        }

        /// <summary>把自述 JSON 写进临时池根的 Bridges/feishu/。</summary>
        private static void WriteDriverFile(PoolTestWorkspace workspace, string json)
        {
            var directory = Path.Combine(workspace.Root, "Bridges", "feishu");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "driver.json"), json, new UTF8Encoding(false));
        }

        /// <summary>正常读取测试用的快捷入口：写文件后直接 Load。</summary>
        private static BridgeDriverDescriptor WriteDriver(PoolTestWorkspace workspace, string json)
        {
            WriteDriverFile(workspace, json);
            return BridgeDriverDescriptor.Load(workspace.Root, "feishu");
        }

        /// <summary>一份字段齐全的 driver 自述，供测试当范本。</summary>
        private static string MinimalDriverJson()
        {
            return """
            {
              "名称": "feishu",
              "port": ["需求编辑端", "消息卡片", "助手"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {
                "应用标识": { "类型": "string", "默认": "" },
                "多维表格标识": { "类型": "string", "默认": "" },
                "超时秒": { "类型": "number", "默认": 60 }
              },
              "密钥字段": ["飞书应用密钥"],
              "试跑": "bridge.provision --driver feishu --dry-run",
              "能力探测": "",
              "实现": "bridge-feishu",
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
