using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>资产规格门禁判定逻辑的测试：类型、落点、命名与规格逐条核对。</summary>
    public class AssetSpecInspectorTests
    {
        private const string BaselineJson = """
            {
              "资产类型": {
                "图标": {
                  "域": "资产.生图",
                  "规格": { "宽": 256, "最大面数": 3000, "需要透明": true },
                  "落点": "Assets/Game/Icons/",
                  "命名模式": "^icon_[a-z0-9_]+$",
                  "可覆盖": ["落点", "规格.宽"]
                }
              }
            }
            """;

        private const string ValidRequestJson = """
            {
              "id": "ASSET-0001-01",
              "需求id": "REQ-0001",
              "工作项id": "WI-0001-01",
              "域": "资产.生图",
              "资产类型": "图标",
              "规格": { "宽": 256, "最大面数": 2000, "需要透明": true },
              "落点": "Assets/Game/Icons/",
              "命名": "icon_coinbag",
              "描述": "desc",
              "风格锚点": {},
              "变体数": 6,
              "预算": { "调用上限": 20 },
              "依赖": [],
              "人工产出": false,
              "契约版本": "1.0.0"
            }
            """;

        /// <summary>资产请求目录不存在时返回空列表、不抛异常。</summary>
        [Fact]
        public void MissingRequestDirectoryReturnsEmpty()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-9999", "");

            Assert.Empty(findings);
        }

        /// <summary>一份符合资产规格数据的合法请求：零发现。</summary>
        [Fact]
        public void ValidRequestHasNoFindings()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01", ValidRequestJson);

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            Assert.Empty(findings);
        }

        /// <summary>资产类型在数据里找不到时报 1 条。</summary>
        [Fact]
        public void UnknownAssetTypeIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"资产类型\": \"图标\"", "\"资产类型\": \"立绘\""));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            var finding = Assert.Single(findings);
            Assert.Contains("不在资产规格数据里", finding.Reason);
        }

        /// <summary>落点与数据不一致时报 1 条。</summary>
        [Fact]
        public void WrongDestinationIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"落点\": \"Assets/Game/Icons/\"", "\"落点\": \"Assets/Wrong/\""));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            var finding = Assert.Single(findings);
            Assert.Contains("不一致", finding.Reason);
        }

        /// <summary>命名不匹配该类型的命名模式时报 1 条。</summary>
        [Fact]
        public void NamingMismatchIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"命名\": \"icon_coinbag\"", "\"命名\": \"bad_name\""));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            var finding = Assert.Single(findings);
            Assert.Contains("命名模式", finding.Reason);
        }

        /// <summary>规格把数字从数据值放宽时报 1 条，理由含「只能收紧不能放宽」。</summary>
        [Fact]
        public void WidenedSpecValueIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"最大面数\": 2000", "\"最大面数\": 5000"));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            var finding = Assert.Single(findings);
            Assert.Contains("只能收紧不能放宽", finding.Reason);
        }

        /// <summary>
        /// 「可覆盖」清单里的键改大**不算放宽**——那正是「可覆盖」这三个字的意思。
        ///
        /// 宽高是**目标值**不是上限：1920 不比 1080 宽松，它就是另一个尺寸。
        /// 当上限比大小的话，会得出「PC 端横屏界面比手机竖屏界面宽松」这种荒唐结论，
        /// 而人明明在可覆盖清单里写了「规格.宽」。真跑撞过这一脚。
        /// </summary>
        [Fact]
        public void OverridableSpecValueMayBeChangedFreely()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"宽\": 256", "\"宽\": 1920"));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            Assert.Empty(findings);
        }

        /// <summary>规格里出现数据没有的键时报 1 条。</summary>
        [Fact]
        public void UnknownSpecKeyIsReported()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteRequest(workspace.Root, "REQ-0001", "ASSET-0001-01",
                ValidRequestJson.Replace("\"需要透明\": true", "\"需要透明\": true, \"透明度\": 0.5"));

            var findings = AssetSpecInspector.Inspect(workspace.Root, "REQ-0001", "");

            var finding = Assert.Single(findings);
            Assert.Contains("不在该类型的规格数据里", finding.Reason);
        }

        private static void WriteBaseline(string root)
        {
            WriteFile(SpecificationPaths.BaselineAssetSpecFile(root), BaselineJson);
        }

        private static void WriteRequest(string root, string requirementIdentifier, string assetIdentifier, string json)
        {
            WriteFile(AssetPaths.AssetRequestFile(root, requirementIdentifier, assetIdentifier), json);
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
                Root = Path.Combine(Path.GetTempPath(), "资产规格门禁测试-" + Guid.NewGuid().ToString("N"));
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
