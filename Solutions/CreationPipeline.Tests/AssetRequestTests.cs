using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>AssetRequest 的读写往返、取号与自校验行为测试。</summary>
    public class AssetRequestTests
    {
        /// <summary>把仓库里的真实「资产请求」基线 schema 内容写进临时池子并加载。</summary>
        private static PoolSchema LoadRequestSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("资产请求", BaselineText("asset-request.schema.json"));
            return PoolSchemaLoader.Load(workspace.Root, "资产请求");
        }

        /// <summary>读仓库 Pools/Schema/Baseline/ 下的真实文件内容。</summary>
        private static string BaselineText(string fileName)
        {
            return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Pools", "Schema", "Baseline", fileName));
        }

        /// <summary>从测试运行目录逐级向上找仓库根（以基线 schema 文件存在为标志）。</summary>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Pools", "Schema", "Baseline", "asset-request.schema.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到仓库根目录（缺 Pools/Schema/Baseline/asset-request.schema.json）。");
        }

        /// <summary>一份 15 个字段全填的资产请求，规格与风格锚点用紧凑 JSON 文本。</summary>
        private static AssetRequest FullRequest()
        {
            return new AssetRequest(
                "ASSET-0042-01",
                "REQ-0042",
                "WI-0042-03",
                "资产.生图",
                "图标",
                new Dictionary<string, string> { ["尺寸"] = "[256,256]", ["格式"] = "\"PNG\"" },
                "Assets/Game/ResourceArt/Icons/",
                "icon_signin",
                "签到图标",
                new Dictionary<string, string> { ["定稿"] = "\"UI图标风格@v3\"" },
                6,
                20,
                new List<string> { "ASSET-0042-02" },
                true,
                "1.0.0");
        }

        /// <summary>WriteTo 后 Read 回来 15 个字段全部一致。</summary>
        [Fact]
        public void WriteThenReadRoundTripsAllFields()
        {
            using var workspace = new PoolTestWorkspace();
            var request = FullRequest();
            var filePath = Path.Combine(workspace.Root, "资产请求", "ASSET-0042-01.json");

            request.WriteTo(filePath);
            var read = AssetRequest.Read(filePath);

            Assert.Equal(request.Identifier, read.Identifier);
            Assert.Equal(request.RequirementIdentifier, read.RequirementIdentifier);
            Assert.Equal(request.WorkItemIdentifier, read.WorkItemIdentifier);
            Assert.Equal(request.Domain, read.Domain);
            Assert.Equal(request.AssetType, read.AssetType);
            Assert.Equal(request.Destination, read.Destination);
            Assert.Equal(request.NamingText, read.NamingText);
            Assert.Equal(request.Description, read.Description);
            Assert.Equal(request.VariantCount, read.VariantCount);
            Assert.Equal(request.BudgetCallLimit, read.BudgetCallLimit);
            Assert.Equal(request.IsManual, read.IsManual);
            Assert.Equal(request.ContractVersion, read.ContractVersion);
            Assert.Equal(request.Dependencies, read.Dependencies);
            Assert.Equal(request.Specification["尺寸"], read.Specification["尺寸"]);
            Assert.Equal(request.Specification["格式"], read.Specification["格式"]);
            Assert.Equal(request.StyleAnchors["定稿"], read.StyleAnchors["定稿"]);
        }

        /// <summary>取号在空目录下给 ASSET-0042-01。</summary>
        [Fact]
        public void AllocateIdentifierOnEmptyDirectoryGivesFirst()
        {
            using var workspace = new PoolTestWorkspace();

            var identifier = AssetRequest.AllocateIdentifier(workspace.Root, "REQ-0042");

            Assert.Equal("ASSET-0042-01", identifier);
        }

        /// <summary>已有 -01 -02 时取号给 -03，且不受其他需求号文件影响。</summary>
        [Fact]
        public void AllocateIdentifierSkipsExistingSequences()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = AssetPaths.AssetRequestDirectory(workspace.Root, "REQ-0042");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ASSET-0042-01.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "ASSET-0042-02.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "ASSET-0099-01.json"), "{}");

            var identifier = AssetRequest.AllocateIdentifier(workspace.Root, "REQ-0042");

            Assert.Equal("ASSET-0042-03", identifier);
        }

        /// <summary>需求 id 里抠不出四位数字时抛 ArgumentException。</summary>
        [Fact]
        public void AllocateIdentifierThrowsWhenNoFourDigitNumber()
        {
            using var workspace = new PoolTestWorkspace();

            var exception = Assert.Throws<ArgumentException>(
                () => AssetRequest.AllocateIdentifier(workspace.Root, "REQ-abc"));

            Assert.Contains("REQ-abc", exception.Message);
        }

        /// <summary>写出来的文件能过 EntityDocumentValidator。</summary>
        [Fact]
        public void WrittenFilePassesEntityDocumentValidator()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var filePath = Path.Combine(workspace.Root, "资产请求", "ASSET-0042-01.json");

            FullRequest().WriteTo(filePath);
            var findings = EntityDocumentValidator.Validate(filePath, schema);

            Assert.Empty(findings);
        }
    }
}
