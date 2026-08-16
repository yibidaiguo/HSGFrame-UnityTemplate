using System;
using System.IO;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipelineTests
{
    /// <summary>资产四类校验测试。</summary>
    public class AssetValidatorTests
    {
        [Fact]
        public void ValidateReportsExtensionOutsideAllowedSet()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "Enemy.obj"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "Enemy.obj.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.Contains(findings, finding => finding.Reason.Contains(".obj"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ValidateReportsFileOverMaximumBytes()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "Hero.png"), "hello");
                File.WriteAllText(Path.Combine(directory, "Hero.png.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }, maximumFileBytes: 4), Array.Empty<string>());

                Assert.Contains(findings, finding => finding.Reason.Contains("字节"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ValidateReportsOrphanMetaFile()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "Ghost.png.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.Contains(findings, finding => finding.Reason.Contains("孤儿"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        // 子目录的 .meta 不是孤儿：目录在 Unity 里也是资产，一样有 .meta。
        // 漏掉这条时，任何一个带子目录的资产目录都会被报成孤儿 .meta——
        // 而资产树按「类型 → 功能 → 模块」分层，带子目录才是常态。
        [Fact]
        public void ValidateTreatsSubdirectoryMetaAsCovered()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(directory, "Level"));
                File.WriteAllText(Path.Combine(directory, "Level.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("孤儿"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ValidateSkipsOrphanReportWhenReferenceSetIsEmpty()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "Hero.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "Hero.png.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("无人引用"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AssetFileNameStartingWithUnderscoreIsReported()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "_临时贴图.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "_临时贴图.png.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.Contains(findings, finding => finding.Reason == "资产文件名以下划线开头");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AssetFileNameStartingWithLetterIsNotReported()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "T_背包格子.png"), string.Empty);
                File.WriteAllText(Path.Combine(directory, "T_背包格子.png.meta"), string.Empty);

                var findings = AssetValidator.Validate(directory, NewRule(new[] { ".png" }), Array.Empty<string>());

                Assert.DoesNotContain(findings, finding => finding.Reason == "资产文件名以下划线开头");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadForDirectoryFindsRuleInAncestorDirectory()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var childDirectory = Path.Combine(directory, "child");
                Directory.CreateDirectory(childDirectory);
                File.WriteAllText(
                    Path.Combine(directory, "导入规则.json"),
                    "{\"目录用途\":\"贴图\",\"文件名前缀\":\"T_\",\"允许扩展名\":[\".png\"],\"命名风格\":\"PascalCase\",\"最大文件字节\":8388608}");

                var rule = AssetImportRuleSet.LoadForDirectory(childDirectory, directory);

                Assert.NotNull(rule);
                Assert.Equal("T_", rule.FileNamePrefix);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static AssetImportRule NewRule(string[] allowedExtensions, long maximumFileBytes = 8388608)
        {
            return new AssetImportRule
            {
                DirectoryPurpose = "测试",
                FileNamePrefix = string.Empty,
                AllowedExtensions = allowedExtensions,
                NamingStyle = "PascalCase",
                MaximumFileBytes = maximumFileBytes
            };
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "AssetValidatorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
