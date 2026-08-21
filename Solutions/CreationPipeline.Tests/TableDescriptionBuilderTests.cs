using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>TableDescriptionBuilder 与 EpicTableBuilder 的建表描述生成测试。</summary>
    public class TableDescriptionBuilderTests
    {
        /// <summary>字段条数等于 schema 字段条数。</summary>
        [Fact]
        public void BuildProducesFieldCountEqualToSchema()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            // schema 声明的 4 个字段 + 分类型必填的 4 个（目标/玩法/现状/期望）= 8 列。
            // 断言从 4 改成 8 是**故意的**：分类型必填的字段原来只进表单、没有列，
            // 结果一条合法的「系统」需求根本写不进下游表（真跑撞出来的）。
            // 表单引用一个不存在的列本身就是坏的，所以补列，断言跟着改。
            Assert.Equal(8, description.Fields.Count);
            Assert.Equal("需求", description.TableName);
            Assert.Contains(description.Fields, field => field.Name == "目标");
            Assert.Contains(description.Fields, field => field.Name == "期望");
        }

        /// <summary>字段带得上 schema 里的逻辑类型——数组存进文本列之后，只有它能说清该不该切回数组。</summary>
        [Fact]
        public void BuildCarriesLogicalTypeFromSchema()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            Assert.Equal("array", description.Fields.Single(field => field.Name == "验收标准").LogicalType);
            Assert.Equal("string", description.Fields.Single(field => field.Name == "标题").LogicalType);
            Assert.Equal("string", description.Fields.Single(field => field.Name == "目标").LogicalType);
        }

        /// <summary>枚举字段的下游类型是「单选」。</summary>
        [Fact]
        public void BuildMapsEnumFieldToSingleSelect()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            var typeField = description.Fields.Single(field => field.Name == "类型");
            Assert.Equal("单选", typeField.DownstreamType);
            Assert.Equal(new[] { "系统", "修改", "缺陷" }, typeField.EnumValues);
        }

        /// <summary>数组字段的下游类型是「多行文本」。</summary>
        [Fact]
        public void BuildMapsArrayFieldToMultilineText()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            var acceptanceField = description.Fields.Single(field => field.Name == "验收标准");
            Assert.Equal("多行文本", acceptanceField.DownstreamType);
        }

        /// <summary>表单个数等于分组字段「类型」的枚举值个数。</summary>
        [Fact]
        public void BuildCreatesOneFormPerGroupingValue()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            Assert.Equal(3, description.Forms.Count);
            Assert.Equal(new[] { "系统", "修改", "缺陷" }, description.Forms.Select(form => form.TypeName));
        }

        /// <summary>「系统」表单的字段含 目标 与 玩法，且不重复。</summary>
        [Fact]
        public void BuildSystemFormContainsTargetAndGameplayWithoutDuplicates()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = TableDescriptionBuilder.Build(CreateSchema(), driver);

            var systemForm = description.Forms.Single(form => form.TypeName == "系统");
            Assert.Contains("目标", systemForm.FieldNames);
            Assert.Contains("玩法", systemForm.FieldNames);
            Assert.Equal(systemForm.FieldNames.Count, systemForm.FieldNames.Distinct().Count());
        }

        /// <summary>EpicTableBuilder 产出的列里含「认领.美术」，表名是 专项，表单为空。</summary>
        [Fact]
        public void EpicTableBuilderProducesDutyColumns()
        {
            using var workspace = new PoolTestWorkspace();
            var driver = WriteDriver(workspace);

            var description = EpicTableBuilder.Build(workspace.Root, driver);

            Assert.Equal("专项", description.TableName);
            Assert.Contains(description.Fields, field => field.Name == "认领.美术");
            Assert.Equal("人员多选", description.Fields.Single(field => field.Name == "认领.美术").DownstreamType);
            Assert.Equal("下游成员", description.Fields.Single(field => field.Name == "认领.美术").Ownership);
            Assert.Empty(description.Forms);
        }

        /// <summary>把自述 JSON 写进临时池根的 Bridges/feishu/ 并读出来。</summary>
        private static BridgeDriverDescriptor WriteDriver(PoolTestWorkspace workspace)
        {
            var directory = Path.Combine(workspace.Root, "Bridges", "feishu");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "driver.json"), DriverJson(), new UTF8Encoding(false));
            return BridgeDriverDescriptor.Load(workspace.Root, "feishu");
        }

        /// <summary>一份字段齐全的 driver 自述，供测试当范本。</summary>
        private static string DriverJson()
        {
            return """
            {
              "名称": "feishu",
              "port": ["需求编辑端", "消息卡片", "助手"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {},
              "密钥字段": [],
              "试跑": "bridge.provision --driver feishu --dry-run",
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

        /// <summary>造一份固定的需求 schema：四字段、分类型必填两类、无状态机。</summary>
        private static PoolSchema CreateSchema()
        {
            var fields = new List<PoolSchemaField>
            {
                new PoolSchemaField("id", "string", true, null, "", 0, "工程", false, false),
                new PoolSchemaField("类型", "enum", true, new[] { "系统", "修改", "缺陷" }, "", 0, "策划端", false, false),
                new PoolSchemaField("标题", "string", true, null, "", 0, "策划端", false, true),
                new PoolSchemaField("验收标准", "array", false, null, "string", 0, "策划端", false, true)
            };

            var requiredByType = new Dictionary<string, IReadOnlyList<string>>
            {
                ["系统"] = new[] { "目标", "玩法" },
                ["修改"] = new[] { "现状", "期望" }
            };

            return new PoolSchema("1.0.0", "需求", "^REQ-\\d{4}$", fields, requiredByType, null);
        }
    }
}
