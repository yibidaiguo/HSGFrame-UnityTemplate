using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>模型机检的测试：五项检查、规格缺键不静默、度量缺键带出来。</summary>
    public class ModelInspectorTests
    {
        private const string FullBaselineJson = """
            {
              "资产类型": {
                "道具模型": {
                  "域": "资产.模型",
                  "规格": {
                    "最大面数": 3000,
                    "格式": "FBX",
                    "单位": "米",
                    "轴心": "中心",
                    "最大材质数": 2,
                    "贴图尺寸": 1024,
                    "包围盒上限米": 5,
                    "最大骨骼数": 0
                  },
                  "落点": "Assets/Game/ResourceArt/Models/Props/",
                  "命名模式": "^prop_[a-z0-9_]+$",
                  "可覆盖": []
                }
              }
            }
            """;

        private const string MissingTextureBaselineJson = """
            {
              "资产类型": {
                "道具模型": {
                  "域": "资产.模型",
                  "规格": {
                    "最大面数": 3000,
                    "格式": "FBX",
                    "单位": "米",
                    "轴心": "中心",
                    "最大材质数": 2,
                    "包围盒上限米": 5,
                    "最大骨骼数": 0
                  },
                  "落点": "Assets/Game/ResourceArt/Models/Props/",
                  "命名模式": "^prop_[a-z0-9_]+$",
                  "可覆盖": []
                }
              }
            }
            """;

        /// <summary>五项全过：零 finding。</summary>
        [Fact]
        public void AllChecksPassProduceNoFindings()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var metrics = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 1.2m, y: 0.8m, z: 1.1m, boneCount: 0);

            var findings = ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, "");

            Assert.Empty(findings);
        }

        /// <summary>面数超标：一条 finding，文案含实际值与上限。</summary>
        [Fact]
        public void FaceCountExceedIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var metrics = Metrics(triangleCount: 5000, materialCount: 1, textureSize: 1024, x: 1.2m, y: 0.8m, z: 1.1m, boneCount: 0);

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, ""));

            Assert.Contains("5000", finding.Reason);
            Assert.Contains("3000", finding.Reason);
        }

        /// <summary>包围盒某一轴超标报一条；两轴超标报两条。</summary>
        [Fact]
        public void BoundingBoxAxisExceedIsReportedPerAxis()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var oneAxis = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 6m, y: 0.8m, z: 1.1m, boneCount: 0);
            var oneFinding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), oneAxis, ""));
            Assert.Contains("x", oneFinding.Reason);

            var twoAxes = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 6m, y: 6m, z: 1.1m, boneCount: 0);
            Assert.Equal(2, ModelInspector.Inspect(workspace.Root, PropRequest(), twoAxes, "").Count);
        }

        /// <summary>包围盒三轴全零：一条 finding，文案含「全零」。</summary>
        [Fact]
        public void ZeroBoundingBoxIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var metrics = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 0m, y: 0m, z: 0m, boneCount: 0);

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, ""));

            Assert.Contains("全零", finding.Reason);
        }

        /// <summary>骨骼数超标：一条 finding。</summary>
        [Fact]
        public void BoneCountExceedIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var metrics = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 1.2m, y: 0.8m, z: 1.1m, boneCount: 5);

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, ""));

            Assert.Contains("5", finding.Reason);
            Assert.Contains("0", finding.Reason);
        }

        /// <summary>规格缺贴图尺寸：一条 finding 说「这一项没查」，不是静默跳过。</summary>
        [Fact]
        public void MissingTextureSpecKeyIsReportedNotSkipped()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, MissingTextureBaselineJson);

            var metrics = Metrics(triangleCount: 2000, materialCount: 1, textureSize: 1024, x: 1.2m, y: 0.8m, z: 1.1m, boneCount: 0);

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, ""));

            Assert.Contains("贴图尺寸", finding.Reason);
            Assert.Contains("这一项没查", finding.Reason);
        }

        /// <summary>度量缺材质数键：MaterialCount 是 0、MissingFieldNames 含它，机检出一条 finding。</summary>
        [Fact]
        public void MissingMetricFieldIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var metrics = new ModelMetrics(2000, 0, 1024, 1.2m, 0.8m, 1.1m, 0, new[] { "材质数" });

            Assert.Equal(0, metrics.MaterialCount);
            Assert.Contains("材质数", metrics.MissingFieldNames);

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, PropRequest(), metrics, ""));
            Assert.Contains("材质数", finding.Reason);
        }

        /// <summary>资产类型不认识：一条 finding 并直接返回。</summary>
        [Fact]
        public void UnknownAssetTypeAbortsInspection()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var request = new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.模型", "立绘",
                new Dictionary<string, string>(), "Assets/Game/Models/", "portrait_hero",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, request, Metrics(), ""));
            Assert.Contains("不在资产规格数据里", finding.Reason);
        }

        /// <summary>域不是资产.模型：一条 finding 并直接返回。</summary>
        [Fact]
        public void WrongDomainAbortsInspection()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var request = new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.生图", "道具模型",
                new Dictionary<string, string>(), "Assets/Game/ResourceArt/Models/Props/", "prop_coinbag",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");

            var finding = Assert.Single(ModelInspector.Inspect(workspace.Root, request, Metrics(), ""));
            Assert.Contains("机检只对模型域有意义", finding.Reason);
        }

        /// <summary>LoadFromFile：缺键就那一项取 0，MissingFieldNames 把键名带出来。</summary>
        [Fact]
        public void LoadFromFileMissingFieldTakesZero()
        {
            using var workspace = new Workspace();
            var filePath = Path.Combine(workspace.Root, "度量.json");
            WriteFile(filePath, """
                {
                  "面数": 2400,
                  "贴图尺寸": 1024,
                  "包围盒米": { "x": 1.2, "y": 0.8, "z": 1.1 },
                  "骨骼数": 0
                }
                """);

            var metrics = ModelMetrics.LoadFromFile(filePath);

            Assert.Equal(0, metrics.MaterialCount);
            Assert.Contains("材质数", metrics.MissingFieldNames);
        }

        /// <summary>LoadFromFile：文件不存在抛 InvalidOperationException，文案带绝对路径。</summary>
        [Fact]
        public void LoadFromFileMissingFileThrows()
        {
            using var workspace = new Workspace();
            var filePath = Path.Combine(workspace.Root, "不存在的度量.json");

            var exception = Assert.Throws<InvalidOperationException>(() => ModelMetrics.LoadFromFile(filePath));

            Assert.Contains(filePath, exception.Message);
        }

        private static AssetRequest PropRequest()
        {
            return new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.模型", "道具模型",
                new Dictionary<string, string>(), "Assets/Game/ResourceArt/Models/Props/", "prop_coinbag",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");
        }

        private static ModelMetrics Metrics(
            int triangleCount = 2000,
            int materialCount = 1,
            int textureSize = 1024,
            decimal x = 1.2m,
            decimal y = 0.8m,
            decimal z = 1.1m,
            int boneCount = 0)
        {
            return new ModelMetrics(triangleCount, materialCount, textureSize, x, y, z, boneCount, Array.Empty<string>());
        }

        private static void WriteBaseline(string root, string json)
        {
            WriteFile(SpecificationPaths.BaselineAssetSpecFile(root), json);
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

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "模型机检测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
