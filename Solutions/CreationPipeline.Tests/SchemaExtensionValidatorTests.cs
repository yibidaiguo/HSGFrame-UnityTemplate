using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>SchemaExtensionValidator 对项目扩展 schema 的合法性检查测试。</summary>
    public class SchemaExtensionValidatorTests
    {
        /// <summary>没有项目扩展文件时，检查结果为零违规。</summary>
        [Fact]
        public void CheckReturnsEmptyWhenNoProjectSchema()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());

            var findings = SchemaExtensionValidator.Check(workspace.Root, "需求");

            Assert.Empty(findings);
        }

        /// <summary>合法扩展（追加一个新字段加一条枚举增补）零违规。</summary>
        [Fact]
        public void CheckAcceptsLegalExtension()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "实体": "需求",
                  "字段": [ { "名称": "优先级", "类型": "string", "必填": false } ],
                  "枚举增补": { "类型": ["剧情"] }
                }
                """);

            var findings = SchemaExtensionValidator.Check(workspace.Root, "需求");

            Assert.Empty(findings);
        }

        /// <summary>扩展字段与骨架字段重名时恰好一条违规，原因里含「重名」。</summary>
        [Fact]
        public void CheckReportsDuplicateSkeletonField()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "实体": "需求",
                  "字段": [ { "名称": "标题", "类型": "string", "必填": true } ]
                }
                """);

            var findings = SchemaExtensionValidator.Check(workspace.Root, "需求");

            var finding = Assert.Single(findings);
            Assert.Contains("重名", finding.Reason);
        }

        /// <summary>扩展字段与「分类型必填」里的字段名重名时报一条违规。</summary>
        [Fact]
        public void CheckReportsDuplicateRequiredByTypeField()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "实体": "需求",
                  "字段": [ { "名称": "玩法", "类型": "string", "必填": true } ]
                }
                """);

            var findings = SchemaExtensionValidator.Check(workspace.Root, "需求");

            var finding = Assert.Single(findings);
            Assert.Contains("重名", finding.Reason);
        }

        /// <summary>顶层不认识的键与枚举增补指向不存在的字段各报一条；下划线开头的说明键不算违规。</summary>
        [Fact]
        public void CheckReportsUnknownTopLevelKeyAndEnumTarget()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            workspace.WriteProjectSchema("需求", """
                {
                  "_说明": "说明键不该算违规",
                  "实体": "需求",
                  "状态机": { "初始状态": "草稿", "转换": [] },
                  "字段": [],
                  "枚举增补": { "不存在字段": ["x"] }
                }
                """);

            var findings = SchemaExtensionValidator.Check(workspace.Root, "需求");

            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.Reason.Contains("不认识的顶层键"));
            Assert.Contains(findings, f => f.Reason.Contains("不存在"));
        }
    }
}
