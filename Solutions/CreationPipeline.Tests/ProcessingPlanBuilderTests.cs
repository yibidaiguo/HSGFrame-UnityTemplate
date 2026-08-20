using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>加工计划构建的测试：八步形状、幂等、缺键禁用与域/类型拦截。</summary>
    public class ProcessingPlanBuilderTests
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

        private const string MissingFaceLimitBaselineJson = """
            {
              "资产类型": {
                "道具模型": {
                  "域": "资产.模型",
                  "规格": {
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

        /// <summary>正常的道具模型请求：八个步骤都在、顺序恒定、烘法线默认禁用。</summary>
        [Fact]
        public void NormalPropRequestProducesEightSteps()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var plan = ProcessingPlanBuilder.Build(workspace.Root, PropRequest(), "");

            Assert.Equal(8, plan.Steps.Count);
            Assert.Equal(
                new[] { "导入", "统一单位", "pivot归位", "减面", "UV", "烘法线", "命名", "导出" },
                plan.Steps.Select(step => step.Name).ToArray());

            var baking = Assert.Single(plan.Steps.Where(step => step.Name == "烘法线"));
            Assert.False(baking.IsEnabled);
            Assert.NotEmpty(baking.SkipReason);
            Assert.Empty(baking.Parameters);
            Assert.Empty(plan.Findings);
        }

        /// <summary>幂等：同一输入连跑两次，ToJsonText 逐字节相同——本批最要紧的断言。</summary>
        [Fact]
        public void SameInputProducesByteIdenticalJson()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var first = ProcessingPlanBuilder.Build(workspace.Root, PropRequest(), "").ToJsonText();
            var second = ProcessingPlanBuilder.Build(workspace.Root, PropRequest(), "").ToJsonText();

            Assert.Equal(first, second);
        }

        /// <summary>规格里抽掉目标面数：减面禁用并出一条 finding，其余步骤照常。</summary>
        [Fact]
        public void MissingFaceLimitDisablesReductionStep()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, MissingFaceLimitBaselineJson);

            var plan = ProcessingPlanBuilder.Build(workspace.Root, PropRequest(), "");

            var reduction = Assert.Single(plan.Steps.Where(step => step.Name == "减面"));
            Assert.False(reduction.IsEnabled);
            Assert.Contains("最大面数", reduction.SkipReason);
            Assert.Empty(reduction.Parameters);

            var finding = Assert.Single(plan.Findings);
            Assert.Contains("最大面数", finding.Reason);

            // 减面与默认禁用的烘法线之外，其余六步照常启用。
            Assert.Equal(6, plan.Steps.Count(step => step.IsEnabled));
            Assert.Equal(2, plan.Steps.Count(step => !step.IsEnabled));
        }

        /// <summary>资产类型不认识：八步全禁用并出一条 finding。</summary>
        [Fact]
        public void UnknownAssetTypeDisablesAllSteps()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var request = new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.模型", "立绘",
                new Dictionary<string, string>(), "Assets/Game/Models/", "portrait_hero",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");

            var plan = ProcessingPlanBuilder.Build(workspace.Root, request, "");

            Assert.Equal(8, plan.Steps.Count(step => !step.IsEnabled));
            var finding = Assert.Single(plan.Findings);
            Assert.Contains("不在资产规格数据里", finding.Reason);
        }

        /// <summary>域不是资产.模型：八步全禁用并出一条 finding。</summary>
        [Fact]
        public void ImageDomainDisablesAllSteps()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var request = new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.生图", "道具模型",
                new Dictionary<string, string>(), "Assets/Game/ResourceArt/Models/Props/", "prop_coinbag",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");

            var plan = ProcessingPlanBuilder.Build(workspace.Root, request, "");

            Assert.Equal(8, plan.Steps.Count(step => !step.IsEnabled));
            var finding = Assert.Single(plan.Findings);
            Assert.Contains("加工计划只对模型域有意义", finding.Reason);
        }

        /// <summary>ToJsonText 不含当前年份、机器名或临时目录路径：没混进时间戳/绝对路径。</summary>
        [Fact]
        public void JsonContainsNoTimestampOrAbsolutePath()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            var text = ProcessingPlanBuilder.Build(workspace.Root, PropRequest(), "").ToJsonText();

            Assert.DoesNotContain(DateTime.Now.Year.ToString(), text);
            Assert.DoesNotContain(Environment.MachineName, text);
            Assert.DoesNotContain(workspace.Root, text);
        }

        /// <summary>请求里收紧过的规格值必须压过规格目录的默认值，否则收紧链路在加工这里断掉。</summary>
        [Fact]
        public void RequestSpecificationOverridesCatalogValue()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root, FullBaselineJson);

            // 基线的道具模型是 最大面数 3000、轴心 中心；brief 把面数收紧到 1500、轴心改成 底部。
            var request = new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.模型", "道具模型",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["最大面数"] = "1500",
                    ["轴心"] = "\"底部\""
                },
                "Assets/Game/ResourceArt/Models/Props/", "prop_coinbag",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");

            var plan = ProcessingPlanBuilder.Build(workspace.Root, request, "");

            var reduction = Assert.Single(plan.Steps.Where(step => step.Name == "减面"));
            Assert.Equal("1500", reduction.Parameters["目标面数"]);

            var pivot = Assert.Single(plan.Steps.Where(step => step.Name == "pivot归位"));
            Assert.Equal("底部", pivot.Parameters["pivot"]);
        }

        private static AssetRequest PropRequest()
        {
            return new AssetRequest(
                "ASSET-0001-01", "REQ-0001", "WI-0001-01", "资产.模型", "道具模型",
                new Dictionary<string, string>(), "Assets/Game/ResourceArt/Models/Props/", "prop_coinbag",
                "desc", new Dictionary<string, string>(), 1, 0, Array.Empty<string>(), false, "1.0.0");
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
                Root = Path.Combine(Path.GetTempPath(), "加工计划测试-" + Guid.NewGuid().ToString("N"));
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
