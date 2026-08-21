using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>规范化的幂等性与管线配置文件豁免测试。两者都是端到端跑收件箱归档时才暴露出来的。</summary>
    public class AssetNormalizeIdempotencyTests
    {
        // 归档命令产出的名字，校验命令必须认。这两条命令各自的单元测试都绿着，
        // 而把它们串起来跑才发现：asset.import 产出 T_UiButtonNormal.png，
        // asset.validate 立刻判它不合规、说应该叫 T_TUibuttonnormal.png。
        [Theory]
        [InlineData("ui__button___normal.PNG")]
        [InlineData("hero idle 01.png")]
        [InlineData("BOSS-attack-FX.png")]
        [InlineData("SNOW Ground.TGA")]
        [InlineData("2号宝箱 diffuse.tga")]
        [InlineData("村庄 地面 贴图.png")]
        [InlineData("rock-cliff-albedo.tga")]
        public void NormalizeIsIdempotent(string originalFileName)
        {
            var rule = CreateTextureRule();

            var once = AssetNameNormalizer.Normalize(originalFileName, rule);
            var twice = AssetNameNormalizer.Normalize(once, rule);

            Assert.Equal(once, twice);
        }

        [Fact]
        public void NormalizeKeepsAnAlreadyPascalCaseWordIntact()
        {
            var rule = CreateTextureRule();

            Assert.Equal("T_UiButtonNormal.png", AssetNameNormalizer.Normalize("T_UiButtonNormal.png", rule));
        }

        [Fact]
        public void NormalizeDoesNotStackThePrefixTwice()
        {
            var rule = CreateTextureRule();

            var normalized = AssetNameNormalizer.Normalize("T_SnowGround.tga", rule);

            Assert.Equal("T_SnowGround.tga", normalized);
            Assert.DoesNotContain("T_T", normalized, StringComparison.Ordinal);
        }

        [Fact]
        public void NormalizeStillCompressesAllUpperCaseWords()
        {
            var rule = CreateTextureRule();

            Assert.Equal("T_BossAttackFx.png", AssetNameNormalizer.Normalize("BOSS-attack-FX.png", rule));
        }

        [Fact]
        public void NormalizeStillTitleCasesAllLowerCaseWords()
        {
            var rule = CreateTextureRule();

            Assert.Equal("T_RockCliffAlbedo.tga", AssetNameNormalizer.Normalize("rock-cliff-albedo.tga", rule));
        }

        [Fact]
        public void RoutingTableFileIsTreatedAsAPipelineConfigurationFile()
        {
            Assert.True(AssetNameNormalizer.IsPipelineConfigurationFile("archive-routes.json"));
            Assert.True(AssetNameNormalizer.IsPipelineConfigurationFile("import-rules.json"));
            Assert.False(AssetNameNormalizer.IsPipelineConfigurationFile("T_HeroIdle_01.png"));
        }

        // 收件箱里除了资产还躺着管线自己的两份配置。它们不是资产，
        // 既不该被判「扩展名不允许」，它们的 .meta 也不该被判成孤儿。
        [Fact]
        public void ValidatorIgnoresPipelineConfigurationFilesAndTheirMetaFiles()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "import-rules.json"), "{}");
                File.WriteAllText(Path.Combine(directory, "import-rules.json.meta"), "guid: 0");
                File.WriteAllText(Path.Combine(directory, "archive-routes.json"), "{}");
                File.WriteAllText(Path.Combine(directory, "archive-routes.json.meta"), "guid: 1");

                var findings = AssetValidator.Validate(directory, CreateTextureRule(), Array.Empty<string>());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ValidatorAcceptsWhatTheNormalizerProduces()
        {
            var directory = CreateTempDirectory();
            try
            {
                var rule = CreateTextureRule();
                var originalNames = new[] { "ui__button___normal.PNG", "BOSS-attack-FX.png", "村庄 地面 贴图.png" };

                foreach (var originalName in originalNames)
                {
                    var normalizedName = AssetNameNormalizer.Normalize(originalName, rule);
                    File.WriteAllBytes(Path.Combine(directory, normalizedName), new byte[] { 1, 2, 3 });
                    File.WriteAllText(Path.Combine(directory, normalizedName + ".meta"), "guid: 0");
                }

                var findings = AssetValidator.Validate(directory, rule, Array.Empty<string>());

                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("文件名不符合规范"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static AssetImportRule CreateTextureRule()
        {
            return new AssetImportRule
            {
                DirectoryPurpose = "贴图",
                FileNamePrefix = "T_",
                AllowedExtensions = new List<string> { ".png", ".tga", ".jpg" },
                NamingStyle = "PascalCase",
                MaximumFileBytes = 8388608,
            };
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AssetNormalizeIdempotencyTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
