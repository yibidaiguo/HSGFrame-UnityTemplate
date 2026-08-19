using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>ProvenanceSidecar 的读写往返、文件哈希与人工产出边车行为测试。</summary>
    public class ProvenanceSidecarTests
    {
        /// <summary>把仓库里的真实「溯源」基线 schema 内容写进临时池子并加载。</summary>
        private static PoolSchema LoadSidecarSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("溯源", BaselineText("溯源.schema.json"));
            return PoolSchemaLoader.Load(workspace.Root, "溯源");
        }

        /// <summary>读仓库 Pools/Schema/基线/ 下的真实文件内容。</summary>
        private static string BaselineText(string fileName)
        {
            return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Pools", "Schema", "基线", fileName));
        }

        /// <summary>从测试运行目录逐级向上找仓库根（以基线 schema 文件存在为标志）。</summary>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Pools", "Schema", "基线", "资产请求.schema.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到仓库根目录（缺 Pools/Schema/基线/资产请求.schema.json）。");
        }

        /// <summary>一份 13 个字段全填的溯源边车。</summary>
        private static ProvenanceSidecar FullSidecar()
        {
            return new ProvenanceSidecar(
                "ASSET-0042-01",
                2,
                "生成",
                "comfyui",
                "图标@v5",
                "seed-123",
                new List<string> { "icon", "coinbag" },
                new Dictionary<string, string> { ["定稿"] = "\"UI图标风格@v3\"" },
                "2026-01-01T00:00:00Z",
                "abc123",
                new Dictionary<string, string> { ["尺寸"] = "[256,256]" },
                false,
                "1.0.0");
        }

        /// <summary>WriteTo 后 Read 回来 13 个字段全部一致。</summary>
        [Fact]
        public void WriteThenReadRoundTripsAllFields()
        {
            using var workspace = new PoolTestWorkspace();
            var sidecar = FullSidecar();
            var filePath = Path.Combine(workspace.Root, "变体", "v2.png.溯源.json");

            sidecar.WriteTo(filePath);
            var read = ProvenanceSidecar.Read(filePath);

            Assert.Equal(sidecar.AssetRequestIdentifier, read.AssetRequestIdentifier);
            Assert.Equal(sidecar.VariantIndex, read.VariantIndex);
            Assert.Equal(sidecar.ProductionMethod, read.ProductionMethod);
            Assert.Equal(sidecar.DriverName, read.DriverName);
            Assert.Equal(sidecar.RecipeName, read.RecipeName);
            Assert.Equal(sidecar.RandomSeed, read.RandomSeed);
            Assert.Equal(sidecar.GeneratedAt, read.GeneratedAt);
            Assert.Equal(sidecar.FileHash, read.FileHash);
            Assert.Equal(sidecar.IsChosen, read.IsChosen);
            Assert.Equal(sidecar.ContractVersion, read.ContractVersion);
            Assert.Equal(sidecar.PromptLines, read.PromptLines);
            Assert.Equal(sidecar.StyleAnchors["定稿"], read.StyleAnchors["定稿"]);
            Assert.Equal(sidecar.InspectionResults["尺寸"], read.InspectionResults["尺寸"]);
        }

        /// <summary>ComputeFileHash 对同一内容两次相同、内容变了就变、文件不存在返回空串。</summary>
        [Fact]
        public void ComputeFileHashIsStableSensitiveAndMissingTolerant()
        {
            using var workspace = new PoolTestWorkspace();
            var path = Path.Combine(workspace.Root, "v1.png");
            File.WriteAllText(path, "hello");

            var first = ProvenanceSidecar.ComputeFileHash(path);
            var second = ProvenanceSidecar.ComputeFileHash(path);
            Assert.Equal(first, second);

            File.WriteAllText(path, "hello!");
            Assert.NotEqual(first, ProvenanceSidecar.ComputeFileHash(path));

            Assert.Equal("", ProvenanceSidecar.ComputeFileHash(Path.Combine(workspace.Root, "不存在.png")));
        }

        /// <summary>ForManualProduction 产的边车当选为真、产出方式是「人工产出」、driver 是「人」。</summary>
        [Fact]
        public void ForManualProductionMarksChosenAndManual()
        {
            using var workspace = new PoolTestWorkspace();
            var variantPath = Path.Combine(workspace.Root, "变体", "人工", "v1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(variantPath));
            File.WriteAllText(variantPath, "manual-art");

            var sidecar = ProvenanceSidecar.ForManualProduction("ASSET-0042-01", 1, variantPath);

            Assert.True(sidecar.IsChosen);
            Assert.Equal("人工产出", sidecar.ProductionMethod);
            Assert.Equal("人", sidecar.DriverName);
            Assert.Equal("", sidecar.RecipeName);
            Assert.Equal("", sidecar.RandomSeed);
            Assert.Equal("1.0.0", sidecar.ContractVersion);
            Assert.Equal(ProvenanceSidecar.ComputeFileHash(variantPath), sidecar.FileHash);
        }

        /// <summary>ForManualProduction 产的边车写盘后能过 EntityDocumentValidator。</summary>
        [Fact]
        public void ManualSidecarPassesEntityDocumentValidator()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSidecarSchema(workspace);
            var variantPath = Path.Combine(workspace.Root, "变体", "v1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(variantPath));
            File.WriteAllText(variantPath, "manual-art");

            var sidecar = ProvenanceSidecar.ForManualProduction("ASSET-0042-01", 1, variantPath);
            var sidecarPath = AssetPaths.SidecarFile(workspace.Root, "REQ-0042", "ASSET-0042-01", "v1.png");
            sidecar.WriteTo(sidecarPath);

            var findings = EntityDocumentValidator.Validate(sidecarPath, schema);

            Assert.Empty(findings);
        }
    }
}
