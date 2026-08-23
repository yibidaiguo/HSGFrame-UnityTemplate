using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>PlanningDocumentSpec 的加载行为测试：基线契约本身，以及项目层的追加。</summary>
    public class PlanningDocumentSpecTests
    {
        /// <summary>模板发的那份基线契约读得出来，三类需求的必填小节与生成区标记都在。</summary>
        [Fact]
        public void LoadsShippedBaselineContract()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();

            var specification = PlanningDocumentSpec.Load(workspace.Root);

            Assert.Contains("需求id", specification.FrontMatterRequiredKeys);
            Assert.Contains("权威侧", specification.FrontMatterRequiredKeys);
            Assert.Equal(new[] { "飞书", "项目" }, specification.AuthorityValues);
            Assert.Equal(new[] { "目标", "玩法", "验收标准", "边界与不做" }, specification.RequiredSectionsFor("系统"));
            Assert.Equal(new[] { "现状", "期望", "验收标准", "边界与不做" }, specification.RequiredSectionsFor("修改"));
            Assert.Equal(new[] { "复现步骤", "期望", "实际", "验收标准", "边界与不做" }, specification.RequiredSectionsFor("缺陷"));
            Assert.Equal("验收标准", specification.AcceptanceSection);
            Assert.StartsWith("<!-- 生成区开始", specification.GeneratedRegionBegin);
            Assert.StartsWith("<!-- 生成区结束", specification.GeneratedRegionEnd);
        }

        /// <summary>没登记的类型没有必填小节，不抛异常。</summary>
        [Fact]
        public void UnknownTypeHasNoRequiredSections()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();

            var specification = PlanningDocumentSpec.Load(workspace.Root);

            Assert.Empty(specification.RequiredSectionsFor("没这个类型"));
        }

        /// <summary>项目层的追加项排在基线小节后面，基线定的一条都还在——那个文件表达不出「删」。</summary>
        [Fact]
        public void ProjectLayerAppendsSectionsAfterBaselineOnes()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();
            workspace.WriteProjectPlanningDocumentSpec("""
            {
              "追加小节": { "系统": ["埋点"] },
              "追加frontmatter必备键": ["负责人"]
            }
            """);

            var specification = PlanningDocumentSpec.Load(workspace.Root);

            Assert.Equal(new[] { "目标", "玩法", "验收标准", "边界与不做", "埋点" }, specification.RequiredSectionsFor("系统"));
            Assert.Contains("负责人", specification.FrontMatterRequiredKeys);
            Assert.Contains("需求id", specification.FrontMatterRequiredKeys);
        }

        /// <summary>基线文件不在时抛 FileNotFoundException，消息里带路径。</summary>
        [Fact]
        public void MissingBaselineThrowsFileNotFound()
        {
            using var workspace = new PoolTestWorkspace();

            var exception = Assert.Throws<FileNotFoundException>(() => PlanningDocumentSpec.Load(workspace.Root));

            Assert.Contains("planning-doc.baseline.md", exception.Message);
        }
    }
}
