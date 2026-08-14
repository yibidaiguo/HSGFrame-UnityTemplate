using System;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipelineTests
{
    /// <summary>资产命名规范化测试。</summary>
    public class AssetNameNormalizerTests
    {
        [Fact]
        public void NormalizeKeepsChineseWordsAndLowercasesExtension()
        {
            var rule = NewRule(prefix: "T_");

            var result = AssetNameNormalizer.Normalize("英雄 贴图-01.PNG", rule);

            Assert.Equal("T_英雄_贴图_01.png", result);
        }

        [Fact]
        public void NormalizeJoinsAsciiWordsIntoPascalCase()
        {
            var rule = NewRule(prefix: "T_");

            var result = AssetNameNormalizer.Normalize("hero_texture.png", rule);

            Assert.Equal("T_HeroTexture.png", result);
        }

        [Fact]
        public void NormalizeIsIdempotentForAlreadyNormalizedName()
        {
            var rule = NewRule(prefix: "T_");

            var result = AssetNameNormalizer.Normalize("T_英雄_贴图_01.png", rule);

            Assert.Equal("T_英雄_贴图_01.png", result);
        }

        [Fact]
        public void PlanDirectoryOnlyProducesPlansForNonNormalizedFiles()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "hero_texture.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "怪物 模型-02.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "T_英雄_贴图_01.png"), string.Empty);

                var plans = AssetNameNormalizer.PlanDirectory(directory, NewRule(prefix: "T_"));

                Assert.Equal(2, plans.Count);
                Assert.DoesNotContain(plans, plan => Path.GetFileName(plan.OriginalPath) == "T_英雄_贴图_01.png");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void PlanDirectorySuffixesCollidingNormalizedNames()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "a b.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "a_b.png"), string.Empty);

                var plans = AssetNameNormalizer.PlanDirectory(directory, NewRule(prefix: string.Empty));

                Assert.Equal(2, plans.Count);
                var names = plans.Select(plan => plan.NormalizedFileName).ToHashSet();
                Assert.Contains("AB.png", names);
                Assert.Contains("AB_2.png", names);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static AssetImportRule NewRule(string prefix)
        {
            return new AssetImportRule
            {
                DirectoryPurpose = "测试",
                FileNamePrefix = prefix,
                AllowedExtensions = new[] { ".png", ".jpg" },
                NamingStyle = "PascalCase",
                MaximumFileBytes = 8388608
            };
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "AssetPipelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
