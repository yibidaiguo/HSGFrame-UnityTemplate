using System;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>PoolSchemaLoader 的基线加载与项目扩展合并语义测试。</summary>
    public class PoolSchemaLoaderTests
    {
        /// <summary>只写基线、不写项目扩展时，Load 出来的字段数与基线一致，版本、实体名、id 模式对得上。</summary>
        [Fact]
        public void LoadKeepsBaselineFieldsWhenNoProjectExtension()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());

            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            Assert.Equal(5, schema.Fields.Count);
            Assert.Equal("1.0.0", schema.SchemaVersion);
            Assert.Equal("需求", schema.EntityName);
            Assert.Equal("^REQ-\\d{4}$", schema.IdentifierPattern);
        }

        /// <summary>基线带状态机时，初始状态与转换条数正确，抽查一条转换的三要素。</summary>
        [Fact]
        public void LoadParsesStateMachineFromBaseline()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());

            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            Assert.NotNull(schema.StateMachine);
            Assert.Equal("草稿", schema.StateMachine.InitialState);
            Assert.Equal(6, schema.StateMachine.Transitions.Count);
            Assert.Equal("草稿", schema.StateMachine.Transitions[0].From);
            Assert.Equal("已确认", schema.StateMachine.Transitions[0].To);
            Assert.Equal("确认人", schema.StateMachine.Transitions[0].Actor);
        }

        /// <summary>项目扩展追加字段后 Load 的字段数比基线多一，LoadBaseline 仍看不到该字段。</summary>
        [Fact]
        public void LoadMergesProjectExtensionFieldIntoBaseline()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "实体": "需求",
                  "字段": [ { "名称": "优先级", "类型": "string", "必填": false } ]
                }
                """);

            var merged = PoolSchemaLoader.Load(workspace.Root, "需求");
            var baseline = PoolSchemaLoader.LoadBaseline(workspace.Root, "需求");

            Assert.Equal(6, merged.Fields.Count);
            Assert.NotNull(merged.FindField("优先级"));
            Assert.Null(baseline.FindField("优先级"));
        }

        /// <summary>枚举增补给基线同名字段追加去重后的取值，指向不存在的字段名时静默跳过不抛异常。</summary>
        [Fact]
        public void LoadMergesEnumExtensionAndSkipsUnknownField()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "实体": "需求",
                  "字段": [],
                  "枚举增补": { "类型": ["剧情"], "不存在字段": ["x"] }
                }
                """);

            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var typeField = schema.FindField("类型");
            Assert.NotNull(typeField);
            Assert.Contains("剧情", typeField.EnumValues);
            Assert.Contains("系统", typeField.EnumValues);
            Assert.Contains("修改", typeField.EnumValues);
            Assert.Contains("缺陷", typeField.EnumValues);
            Assert.Equal(4, typeField.EnumValues.Count);
        }

        /// <summary>基线 schema 文件不存在时 LoadBaseline 抛出 FileNotFoundException。</summary>
        [Fact]
        public void LoadBaselineThrowsWhenFileMissing()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.Throws<FileNotFoundException>(() => PoolSchemaLoader.LoadBaseline(workspace.Root, "需求"));
        }
    }
}
