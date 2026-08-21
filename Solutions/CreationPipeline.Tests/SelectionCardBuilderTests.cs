using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>选片卡片装配的测试：变体扫描、边车校验、弃置计数、按钮与提示语。</summary>
    public sealed class SelectionCardBuilderTests
    {
        private const string Requirement = "REQ-0001";
        private const string Asset = "ASSET-0001-01";

        /// <summary>变体目录不存在 → Card 为 null 且一条 finding。</summary>
        [Fact]
        public void MissingVariantDirectoryGivesNoCard()
        {
            using var workspace = new PoolTestWorkspace();

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 1);

            Assert.Null(result.Card);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("变体目录不存在", finding.Reason);
        }

        /// <summary>三张图各带边车 → 三个合格变体、按钮是 1 2 3 换一批 我自己来。</summary>
        [Fact]
        public void QualifiedVariantsProduceExpectedButtons()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");
            WriteVariant(workspace.Root, "v2.jpg");
            WriteSidecar(workspace.Root, "v2.jpg");
            WriteVariant(workspace.Root, "v3.webp");
            WriteSidecar(workspace.Root, "v3.webp");

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 1);

            Assert.NotNull(result.Card);
            Assert.Equal(new[] { "v1.png", "v2.jpg", "v3.webp" }, result.Card.QualifiedVariants);
            Assert.Equal(new[] { "1", "2", "3", "换一批", "我自己来" }, result.Card.Buttons);
            Assert.Empty(result.Findings);
        }

        /// <summary>一张图没边车 → 它不进合格列表且出一条 finding，另外两张照常。</summary>
        [Fact]
        public void MissingSidecarExcludesVariantWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");
            WriteVariant(workspace.Root, "v2.png");
            WriteVariant(workspace.Root, "v3.png");
            WriteSidecar(workspace.Root, "v3.png");

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 1);

            Assert.NotNull(result.Card);
            Assert.Equal(new[] { "v1.png", "v3.png" }, result.Card.QualifiedVariants);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("v2.png", finding.Reason);
            Assert.Contains("溯源边车", finding.Reason);
        }

        /// <summary>人工/ 子目录里的图不被收进来，证明只扫顶层。</summary>
        [Fact]
        public void ManualSubdirectoryIsNotScanned()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");
            var manualDirectory = Path.Combine(VariantDirectory(workspace.Root), "manual");
            Directory.CreateDirectory(manualDirectory);
            File.WriteAllText(Path.Combine(manualDirectory, "hand.png"), "placeholder");
            File.WriteAllText(Path.Combine(manualDirectory, "hand.png.provenance.json"), SidecarContent);

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 1);

            Assert.NotNull(result.Card);
            Assert.Equal(new[] { "v1.png" }, result.Card.QualifiedVariants);
        }

        /// <summary>round = 3 → Hint 是那句接管提示。</summary>
        [Fact]
        public void RoundThreeGivesTakeoverHint()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 3);

            Assert.NotNull(result.Card);
            Assert.Equal("已 3 轮，考虑接管或调锚点", result.Card.Hint);
        }

        /// <summary>round = 2 → Hint 空串。</summary>
        [Fact]
        public void RoundTwoHasNoHint()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 2);

            Assert.NotNull(result.Card);
            Assert.Equal("", result.Card.Hint);
        }

        /// <summary>弃置目录有两个文件 → RejectedCount 是 2。</summary>
        [Fact]
        public void RejectedCountCountsRejectedFiles()
        {
            using var workspace = new PoolTestWorkspace();
            WriteVariant(workspace.Root, "v1.png");
            WriteSidecar(workspace.Root, "v1.png");
            var rejectedDirectory = AssetPaths.RejectedDirectory(workspace.Root, Requirement, Asset);
            Directory.CreateDirectory(rejectedDirectory);
            File.WriteAllText(Path.Combine(rejectedDirectory, "bad1.png"), "placeholder");
            File.WriteAllText(Path.Combine(rejectedDirectory, "bad2.png"), "placeholder");

            var result = SelectionCardBuilder.Build(workspace.Root, Requirement, Asset, 1);

            Assert.NotNull(result.Card);
            Assert.Equal(2, result.Card.RejectedCount);
        }

        private static string VariantDirectory(string root)
        {
            return AssetPaths.VariantDirectory(root, Requirement, Asset);
        }

        private static void WriteVariant(string root, string fileName)
        {
            Directory.CreateDirectory(VariantDirectory(root));
            File.WriteAllText(Path.Combine(VariantDirectory(root), fileName), "placeholder");
        }

        private static void WriteSidecar(string root, string variantFileName)
        {
            Directory.CreateDirectory(VariantDirectory(root));
            File.WriteAllText(AssetPaths.SidecarFile(root, Requirement, Asset, variantFileName), SidecarContent);
        }

        private const string SidecarContent = """
            { "来源": { "渠道": "外部" } }
            """;
    }
}
