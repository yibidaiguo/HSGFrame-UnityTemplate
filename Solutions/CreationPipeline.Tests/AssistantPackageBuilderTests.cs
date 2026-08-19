using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>AssistantPackageBuilder 的助手配置包生成测试：六个文件、降级文案与内容核对。</summary>
    public class AssistantPackageBuilderTests
    {
        /// <summary>六个文件都写出来了，且返回顺序即路径列表。</summary>
        [Fact]
        public void BuildWritesAllSixFiles()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动");

            Assert.Equal(6, files.Count);
            Assert.All(files, file => Assert.True(File.Exists(file)));
        }

        /// <summary>设计池汇总目录为空时，设计池摘要.md 含占位文案。</summary>
        [Fact]
        public void DesignSummaryFallsBackWhenNoFiles()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动");
            var designSummary = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "设计池摘要.md"));

            Assert.Contains("暂无设计汇总。", designSummary);
        }

        /// <summary>放一份术语表.json 后，术语表.md 里含那个词。</summary>
        [Fact]
        public void GlossaryRendersTermsFromJson()
        {
            using var workspace = PrepareWorkspace();
            var knowledgeDirectory = Path.Combine(workspace.Root, "知识");
            Directory.CreateDirectory(knowledgeDirectory);
            File.WriteAllText(
                Path.Combine(knowledgeDirectory, "术语表.json"),
                GlossaryJson(),
                new UTF8Encoding(false));

            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");
            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动");
            var glossary = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "术语表.md"));

            Assert.Contains("签到", glossary);
            Assert.Contains("每日登录领取奖励的系统", glossary);
        }

        /// <summary>系统提示.md 含价值排序，且 schema 摘要表里含「验收标准」这一行。</summary>
        [Fact]
        public void SystemPromptContainsValueOrderAndSchemaTable()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动");
            var systemPrompt = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "系统提示.md"));

            Assert.Contains("设计一致性把关", systemPrompt);
            Assert.Contains("| 验收标准 |", systemPrompt);
        }

        /// <summary>导入说明.md 里搜不到任何下游平台的名字。</summary>
        [Fact]
        public void ImportGuideMentionsNoPlatformName()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动");
            var importGuide = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "导入说明.md"));

            Assert.DoesNotContain("feishu", importGuide);
            Assert.DoesNotContain("测试驱动", importGuide);
            Assert.Contains("下游平台", importGuide);
        }

        /// <summary>备一个池子：基线 schema 写进池根，知识与设计池汇总目录都不预建。</summary>
        private static PoolTestWorkspace PrepareWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            return workspace;
        }

        /// <summary>一份含「签到」词条的术语表 JSON。</summary>
        private static string GlossaryJson()
        {
            return """
            {
              "条目": [
                { "词": "签到", "释义": "每日登录领取奖励的系统" }
              ]
            }
            """;
        }
    }
}
