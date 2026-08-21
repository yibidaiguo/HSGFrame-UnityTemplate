using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>EntityDocumentValidator 对任意实体文档的通用校验行为测试，样本用真实「资产请求」基线 schema。</summary>
    public class EntityDocumentValidatorTests
    {
        /// <summary>返回一份完整合法、必填项齐全的资产请求 JSON 对象，供各测试删字段或改值后使用。</summary>
        private static JsonObject ValidAssetRequestJson()
        {
            return new JsonObject
            {
                ["id"] = "ASSET-0042-01",
                ["需求id"] = "REQ-0042",
                ["工作项id"] = "WI-0042-03",
                ["域"] = "资产.生图",
                ["资产类型"] = "图标",
                ["规格"] = new JsonObject { ["尺寸"] = new JsonArray(256, 256), ["格式"] = "PNG" },
                ["落点"] = "Assets/Game/ResourceArt/Icons/",
                ["命名"] = "icon_signin",
                ["描述"] = "签到图标",
                ["风格锚点"] = new JsonObject { ["定稿"] = "UI图标风格@v3" },
                ["变体数"] = 6,
                ["预算"] = new JsonObject { ["调用上限"] = 20 },
                ["依赖"] = new JsonArray(),
                ["人工产出"] = false,
                ["契约版本"] = "1.0.0"
            };
        }

        /// <summary>把仓库里的真实基线 schema 内容写进临时池子并加载。</summary>
        private static PoolSchema LoadRequestSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("asset-requests", BaselineText("asset-request.schema.json"));
            return PoolSchemaLoader.Load(workspace.Root, "asset-requests");
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

        /// <summary>把 JSON 文本写到资产请求目录下某文件，返回完整路径。</summary>
        private static string WriteRequestFile(PoolTestWorkspace workspace, string fileName, string json)
        {
            var path = AssetPaths.AssetRequestFile(workspace.Root, "REQ-0042", fileName.Replace(".json", ""));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>文件不存在时报一条，原因是「文件不存在」。</summary>
        [Fact]
        public void MissingFileReportsSingleFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);

            var findings = EntityDocumentValidator.Validate(Path.Combine(workspace.Root, "不存在.json"), schema);

            var finding = Assert.Single(findings);
            Assert.Contains("文件不存在", finding.Reason);
        }

        /// <summary>JSON 语法坏掉时报一条，原因含「JSON 语法错误」。</summary>
        [Fact]
        public void BrokenJsonReportsSingleFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", "{ this is not json");

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("JSON 语法错误", finding.Reason);
        }

        /// <summary>顶层是数组时报一条，原因是「顶层不是 JSON 对象」。</summary>
        [Fact]
        public void ArrayRootReportsSingleFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", "[1, 2, 3]");

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("顶层不是 JSON 对象", finding.Reason);
        }

        /// <summary>删掉两个必填字段时逐条报必填缺失，原因里能看到字段名。</summary>
        [Fact]
        public void MissingRequiredFieldsReportEachField()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var json = ValidAssetRequestJson();
            json.Remove("落点");
            json.Remove("契约版本");
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", json.ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            Assert.Equal(2, findings.Count);
            Assert.Contains(findings, f => f.Reason.Contains("落点"));
            Assert.Contains(findings, f => f.Reason.Contains("契约版本"));
        }

        /// <summary>枚举字段写枚举外的值时报一条，原因里能看到完整枚举值。</summary>
        [Fact]
        public void OutOfRangeEnumReportsFindingWithAllowedValues()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var json = ValidAssetRequestJson();
            json["域"] = "资产.视频";
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", json.ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("资产.生图、资产.模型、资产.动画", finding.Reason);
        }

        /// <summary>数组字段给了字符串时报「不是数组」。</summary>
        [Fact]
        public void ArrayFieldGivenStringReportsNotArray()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var json = ValidAssetRequestJson();
            json["依赖"] = "not-an-array";
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", json.ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("不是数组", finding.Reason);
        }

        /// <summary>对象字段给了数字时报「不是对象」。</summary>
        [Fact]
        public void ObjectFieldGivenNumberReportsNotObject()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var json = ValidAssetRequestJson();
            json["规格"] = 42;
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", json.ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("不是对象", finding.Reason);
        }

        /// <summary>文档里出现 schema 未声明的字段时报一条。</summary>
        [Fact]
        public void UndeclaredFieldReportsSingleFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var json = ValidAssetRequestJson();
            json["多写字段"] = "x";
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", json.ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            var finding = Assert.Single(findings);
            Assert.Contains("未在合并 schema 中声明", finding.Reason);
        }

        /// <summary>一份完全合法的资产请求零发现。</summary>
        [Fact]
        public void FullyValidAssetRequestHasNoFindings()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequestSchema(workspace);
            var path = WriteRequestFile(workspace, "ASSET-0042-01.json", ValidAssetRequestJson().ToJsonString());

            var findings = EntityDocumentValidator.Validate(path, schema);

            Assert.Empty(findings);
        }
    }
}
