using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>依赖方向校验测试：违规报告、方向放行、目录边界、前缀规范化与多规则命中。</summary>
    public class AssetDependencyDirectionCheckerTests
    {
        [Fact]
        public void ForbiddenDirectionIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string toolGuid = "c0000000000000000000000000000002";
                WriteAsset(assetsRoot, "Presentation/界面.prefab", $"  m_Script: {{fileID: 11, guid: {toolGuid}, type: 3}}");
                WriteMeta(assetsRoot, "Presentation/界面.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "Editor/工具.asset", "内容无所谓");
                WriteMeta(assetsRoot, "Editor/工具.asset", toolGuid);

                var rule = new AssetDependencyRule("Presentation/", "Editor/", "表现层资产依赖了编辑器专用资产");
                var violations = AssetDependencyDirectionChecker.Check(assetsRoot, new[] { rule });

                Assert.Single(violations);
                Assert.Equal("Presentation/界面.prefab", violations[0].ReferencingAssetPath);
                Assert.Equal("Editor/工具.asset", violations[0].ReferencedAssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void AllowedDirectionIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string uiGuid = "c0000000000000000000000000000001";
                WriteAsset(assetsRoot, "Editor/工具.asset", $"  m_Script: {{fileID: 11, guid: {uiGuid}, type: 3}}");
                WriteMeta(assetsRoot, "Editor/工具.asset", "c0000000000000000000000000000002");
                WriteAsset(assetsRoot, "Presentation/界面.prefab", "内容无所谓");
                WriteMeta(assetsRoot, "Presentation/界面.prefab", uiGuid);

                var rule = new AssetDependencyRule("Presentation/", "Editor/", "表现层资产依赖了编辑器专用资产");
                var violations = AssetDependencyDirectionChecker.Check(assetsRoot, new[] { rule });

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void PrefixMatchStopsAtDirectoryBoundary()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string toolGuid = "c0000000000000000000000000000012";
                WriteAsset(assetsRoot, "ArtOld/贴图.asset", $"  m_Script: {{fileID: 11, guid: {toolGuid}, type: 3}}");
                WriteMeta(assetsRoot, "ArtOld/贴图.asset", "c0000000000000000000000000000011");
                WriteAsset(assetsRoot, "Editor/工具.asset", "内容无所谓");
                WriteMeta(assetsRoot, "Editor/工具.asset", toolGuid);

                var rule = new AssetDependencyRule("Art/", "Editor/", "美术资产依赖了编辑器专用资产");
                var violations = AssetDependencyDirectionChecker.Check(assetsRoot, new[] { rule });

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void PrefixIsNormalizedWhenRuleOmitsTrailingSlash()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string toolGuid = "c0000000000000000000000000000022";
                WriteAsset(assetsRoot, "Presentation/界面.prefab", $"  m_Script: {{fileID: 11, guid: {toolGuid}, type: 3}}");
                WriteMeta(assetsRoot, "Presentation/界面.prefab", "c0000000000000000000000000000021");
                WriteAsset(assetsRoot, "Editor/工具.asset", "内容无所谓");
                WriteMeta(assetsRoot, "Editor/工具.asset", toolGuid);

                var rule = new AssetDependencyRule("Presentation", "Editor", "表现层资产依赖了编辑器专用资产");
                var violations = AssetDependencyDirectionChecker.Check(assetsRoot, new[] { rule });

                Assert.Single(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void OneEdgeHitByTwoRulesYieldsTwoViolations()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string toolGuid = "c0000000000000000000000000000032";
                WriteAsset(assetsRoot, "Presentation/界面.prefab", $"  m_Script: {{fileID: 11, guid: {toolGuid}, type: 3}}");
                WriteMeta(assetsRoot, "Presentation/界面.prefab", "c0000000000000000000000000000031");
                WriteAsset(assetsRoot, "Editor/工具.asset", "内容无所谓");
                WriteMeta(assetsRoot, "Editor/工具.asset", toolGuid);

                var rules = new[]
                {
                    new AssetDependencyRule("Presentation/", "Editor/", "理由甲"),
                    new AssetDependencyRule("", "Editor/", "理由乙")
                };
                var violations = AssetDependencyDirectionChecker.Check(assetsRoot, rules);

                Assert.Equal(2, violations.Count);
                Assert.Equal(
                    new HashSet<string> { "理由甲", "理由乙" },
                    violations.Select(v => v.Rule.Reason).ToHashSet());
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void EmptyRulesAndMissingRootYieldEmptyResult()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                Assert.Empty(AssetDependencyDirectionChecker.Check(assetsRoot, Array.Empty<AssetDependencyRule>()));

                var missing = Path.Combine(Path.GetTempPath(), "没有这个目录-" + Guid.NewGuid().ToString("N"));
                var rule = new AssetDependencyRule("Presentation/", "Editor/", "表现层资产依赖了编辑器专用资产");
                Assert.Empty(AssetDependencyDirectionChecker.Check(missing, new[] { rule }));
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void DisplayTextCarriesAllFourParts()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string toolGuid = "c0000000000000000000000000000042";
                WriteAsset(assetsRoot, "Presentation/界面.prefab", $"  m_Script: {{fileID: 11, guid: {toolGuid}, type: 3}}");
                WriteMeta(assetsRoot, "Presentation/界面.prefab", "c0000000000000000000000000000041");
                WriteAsset(assetsRoot, "Editor/工具.asset", "内容无所谓");
                WriteMeta(assetsRoot, "Editor/工具.asset", toolGuid);

                var rule = new AssetDependencyRule("Presentation/", "Editor/", "表现层资产依赖了编辑器专用资产");
                var violation = Assert.Single(AssetDependencyDirectionChecker.Check(assetsRoot, new[] { rule }));
                var text = violation.ToDisplayText();

                Assert.Contains("位置：", text);
                Assert.Contains("原因：", text);
                Assert.Contains("修复：", text);
                Assert.Contains("参考：", text);
                Assert.Contains("表现层资产依赖了编辑器专用资产", text);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void ScanReferenceEdgesMapsReferencerToReferenced()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string imageGuid = "c00000000000000000000000000000a2";
                WriteAsset(
                    assetsRoot,
                    "场景.unity",
                    $"  m_Script: {{fileID: 11, guid: {imageGuid}, type: 3}}\n  m_Script: {{fileID: 11, guid: ffffffffffffffffffffffffffffffff, type: 3}}");
                WriteMeta(assetsRoot, "场景.unity", "c00000000000000000000000000000a1");
                WriteAsset(assetsRoot, "贴图.asset", "内容无所谓");
                WriteMeta(assetsRoot, "贴图.asset", imageGuid);

                var edges = AssetReferenceScanner.ScanReferenceEdges(assetsRoot);

                Assert.Equal(new[] { "贴图.asset" }, edges["场景.unity"]);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static void WriteAsset(string root, string fileName, string content)
        {
            var fullPath = Path.Combine(root, fileName);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        private static void WriteMeta(string root, string fileName, string guid)
        {
            File.WriteAllText(Path.Combine(root, fileName + ".meta"), $"fileFormatVersion: 2\nguid: {guid}\n");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AssetDependencyDirectionCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
