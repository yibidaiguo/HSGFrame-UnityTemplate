using System;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板资产页读取器测试：全部用系统临时目录建仓库，跑完自删。</summary>
    public sealed class CreationPanelAssetsTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelAssetsTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板资产读取器测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>_Tasks 目录不存在时返回空列表，不抛。</summary>
        [Fact]
        public void MissingTasksDirectoryReturnsEmptyWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot));
        }

        /// <summary>一个资产请求加三张变体图（两张有边车）：合格变体数按带边车算，请求变体数取请求里写的那个数。</summary>
        [Fact]
        public void QualifiedVariantsCountImagesWithSidecars()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01.json", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001",
                  "资产类型": "图标",
                  "规格": {
                    "宽": 256,
                    "高": 256,
                    "格式": "PNG"
                  },
                  "落点": "UnityProject/Assets/图标",
                  "变体数": 5
                }
                """);
            WriteVariantFile("REQ-0001", "ASSET-0001-01", "v1.png", "image");
            WriteSidecar("REQ-0001", "ASSET-0001-01", "v1.png", "{}");
            WriteVariantFile("REQ-0001", "ASSET-0001-01", "v2.jpg", "image");
            WriteSidecar("REQ-0001", "ASSET-0001-01", "v2.jpg", "{}");
            WriteVariantFile("REQ-0001", "ASSET-0001-01", "v3.webp", "image");

            var rows = CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("ASSET-0001-01", row.AssetIdentifier);
            Assert.Equal("REQ-0001", row.RequirementIdentifier);
            Assert.Equal("图标", row.AssetType);
            Assert.Equal("UnityProject/Assets/图标", row.Destination);
            Assert.Equal(5, row.RequestedVariantCount);
            Assert.Equal(2, row.QualifiedVariantCount);
            Assert.Equal("宽=256 格式=PNG 高=256", row.SpecSummary);
        }

        /// <summary>弃置目录里两个文件，RejectedVariantCount 是 2。</summary>
        [Fact]
        public void RejectedVariantCountCountsRejectedDirectoryFiles()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01.json", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001"
                }
                """);
            WriteFile(Path.Combine(_repositoryRoot, "_Tasks", "REQ-0001", "30-产物", "ASSET-0001-01", "弃", "bad1.png"), "x");
            WriteFile(Path.Combine(_repositoryRoot, "_Tasks", "REQ-0001", "30-产物", "ASSET-0001-01", "弃", "bad2.jpg"), "x");

            var rows = CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal(2, row.RejectedVariantCount);
        }

        /// <summary>预览文件存在 / 不存在，HasPreview 对应 true / false。</summary>
        [Fact]
        public void HasPreviewReflectsPreviewFileExistence()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01.json", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001"
                }
                """);
            WriteRequest("REQ-0001", "ASSET-0001-02.json", """
                {
                  "id": "ASSET-0001-02",
                  "需求id": "REQ-0001"
                }
                """);
            WriteFile(AssetPaths.PreviewFile(_repositoryRoot, "REQ-0001", "ASSET-0001-01"), "x");

            var rows = CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot);

            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].HasPreview);
            Assert.False(rows[1].HasPreview);
        }

        /// <summary>坏 JSON 的资产请求不产行，另一份好的照常读得到。</summary>
        [Fact]
        public void BrokenRequestFileIsSkipped()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteRequest("REQ-0001", "ASSET-0001-01.json", """
                {
                  not valid json at all
                """);
            WriteRequest("REQ-0002", "ASSET-0002-01.json", """
                {
                  "id": "ASSET-0002-01",
                  "需求id": "REQ-0002"
                }
                """);

            var rows = CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("ASSET-0002-01", row.AssetIdentifier);
        }

        /// <summary>两个资产按资产 id 序数序排序。</summary>
        [Fact]
        public void AssetsAreSortedByAssetIdentifier()
        {
            WriteRequest("REQ-0001", "ASSET-0001-02.json", """
                {
                  "id": "ASSET-0001-02",
                  "需求id": "REQ-0001"
                }
                """);
            WriteRequest("REQ-0001", "ASSET-0001-01.json", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001"
                }
                """);
            WriteRequest("REQ-0002", "ASSET-0002-01.json", """
                {
                  "id": "ASSET-0002-01",
                  "需求id": "REQ-0002"
                }
                """);

            var rows = CreationPanelReader.ReadAssets(_repositoryRoot, _poolRoot);

            Assert.Equal(3, rows.Count);
            Assert.Equal("ASSET-0001-01", rows[0].AssetIdentifier);
            Assert.Equal("ASSET-0001-02", rows[1].AssetIdentifier);
            Assert.Equal("ASSET-0002-01", rows[2].AssetIdentifier);
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        private void WriteRequest(string requirementIdentifier, string fileName, string json)
        {
            var directory = AssetPaths.AssetRequestDirectory(_repositoryRoot, requirementIdentifier);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), json);
        }

        private void WriteVariantFile(string requirementIdentifier, string assetIdentifier, string variantName, string content)
        {
            WriteFile(Path.Combine(AssetPaths.VariantDirectory(_repositoryRoot, requirementIdentifier, assetIdentifier), variantName), content);
        }

        private void WriteSidecar(string requirementIdentifier, string assetIdentifier, string variantName, string json)
        {
            WriteFile(AssetPaths.SidecarFile(_repositoryRoot, requirementIdentifier, assetIdentifier, variantName), json);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
