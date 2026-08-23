using System;
using System.Collections.Generic;
using System.IO;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipelineTests
{
    /// <summary>预审规范裁剪测试：diff 路径解析与按裁剪表过滤，三条回退线逐条锁住。</summary>
    public class PreReviewPromptPruningTests : IDisposable
    {
        private readonly string _root;

        /// <summary>建一棵临时树放裁剪表。</summary>
        public PreReviewPromptPruningTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PreReviewPruningTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>清掉临时树。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        private const string SampleDiff = """
            diff --git a/Tools/CreationPipeline/PreReviewPrompt.cs b/Tools/CreationPipeline/PreReviewPrompt.cs
            index 1111111..2222222 100644
            --- a/Tools/CreationPipeline/PreReviewPrompt.cs
            +++ b/Tools/CreationPipeline/PreReviewPrompt.cs
            @@ -1,3 +1,4 @@
            +using System;
            diff --git a/Tools/Gates/gate.ps1 b/Tools/Gates/gate.ps1
            +++ b/Tools/Gates/gate.ps1
            """;

        /// <summary>标准 diff 解析出全部 b/ 侧路径并去重。</summary>
        [Fact]
        public void ParseChangedPathsReadsAllTargets()
        {
            var paths = PreReviewPrompt.ParseChangedPaths(SampleDiff);

            Assert.Equal(2, paths.Count);
            Assert.Contains("Tools/CreationPipeline/PreReviewPrompt.cs", paths);
            Assert.Contains("Tools/Gates/gate.ps1", paths);
        }

        /// <summary>非 diff 文本解析出空列表（调用方据此回退全量）。</summary>
        [Fact]
        public void ParseChangedPathsReturnsEmptyForPlainText()
        {
            Assert.Empty(PreReviewPrompt.ParseChangedPaths("这只是一段普通的话，不是 diff。"));
        }

        /// <summary>只碰 .cs 的 diff：资产与策划文档规范被裁掉，代码规范与没进表的文件保留。</summary>
        [Fact]
        public void FilterKeepsRelevantAndUnlistedSpecs()
        {
            WriteRelevanceRules();
            var specTexts = new List<string>
            {
                SpecText("Specifications/structure-code.md"),
                SpecText("Specifications/structure-assets.md"),
                SpecText("Specifications/Baseline/planning-doc.baseline.md"),
                SpecText("Specifications/structure-overview.md")
            };
            var changedPaths = new List<string> { "Tools/CreationPipeline/PreReviewPrompt.cs" };

            var kept = PreReviewPrompt.FilterSpecTexts(specTexts, changedPaths, _root);

            Assert.Contains(specTexts[0], kept);
            Assert.DoesNotContain(specTexts[1], kept);
            Assert.DoesNotContain(specTexts[2], kept);
            Assert.Contains(specTexts[3], kept);
        }

        /// <summary>改动路径为空时原样返回全量（解析失败不许悄悄裁）。</summary>
        [Fact]
        public void FilterFallsBackWhenNoChangedPaths()
        {
            WriteRelevanceRules();
            var specTexts = new List<string> { SpecText("Specifications/structure-assets.md") };

            var kept = PreReviewPrompt.FilterSpecTexts(specTexts, Array.Empty<string>(), _root);

            Assert.Equal(specTexts, kept);
        }

        /// <summary>裁剪表不存在时原样返回全量。</summary>
        [Fact]
        public void FilterFallsBackWhenRulesFileMissing()
        {
            var specTexts = new List<string> { SpecText("Specifications/structure-assets.md") };
            var changedPaths = new List<string> { "Tools/Sample.cs" };

            var kept = PreReviewPrompt.FilterSpecTexts(specTexts, changedPaths, _root);

            Assert.Equal(specTexts, kept);
        }

        /// <summary>diff 命中路径前缀时对应规范保留（前缀线，与扩展名线分开验）。</summary>
        [Fact]
        public void FilterKeepsSpecWhenPathPrefixMatches()
        {
            WriteRelevanceRules();
            var specTexts = new List<string> { SpecText("Specifications/structure-assets.md") };
            var changedPaths = new List<string> { "UnityProject/Assets/Game/Art/Texture/T_Sample.png" };

            var kept = PreReviewPrompt.FilterSpecTexts(specTexts, changedPaths, _root);

            Assert.Equal(specTexts, kept);
        }

        private static string SpecText(string relativePath)
        {
            return "### 文件：" + relativePath + "\n（规范内容占位）";
        }

        private void WriteRelevanceRules()
        {
            var configDirectory = Path.Combine(_root, "Tools", "CreationPipeline", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "spec-relevance.json"), """
                {
                  "规则": [
                    { "规范路径前缀": "Specifications/Baseline/planning-doc.baseline.md", "diff路径前缀": ["Pools/", "Doc/"] },
                    { "规范路径前缀": "Specifications/structure-assets.md", "diff路径前缀": ["UnityProject/", "Pools/"] },
                    { "规范路径前缀": "Specifications/structure-code.md", "diff扩展名": [".cs", ".csproj"] }
                  ]
                }
                """);
        }
    }
}
