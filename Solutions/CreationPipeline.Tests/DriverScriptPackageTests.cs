using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 目录型脚本包自述的测试。这一族守的是「坏包不许冒充好包」：
    /// plugin.json 里的四条判据（名称对得上、宿主落点是相对路径、落点不带 ..、标志文件非空）
    /// 每一条都直接决定我们会往宿主目录的哪个位置写文件、删什么。
    /// 判不了的时候正确行为是 Loaded=false 并说清坏在哪，不是替它猜一个默认值。
    /// </summary>
    public class DriverScriptPackageTests
    {
        /// <summary>scripts/ 目录压根不存在时给空表，不抛。</summary>
        [Fact]
        public void MissingScriptsDirectoryProducesEmptyList()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");

            Assert.Empty(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));
        }

        /// <summary>一份写对的自述解析出全部字段。</summary>
        [Fact]
        public void WellFormedManifestLoads()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "relay_image_node", """
                {
                  "契约版本": "1.0.0",
                  "名称": "relay_image_node",
                  "宿主落点": "custom_nodes/relay_image_node",
                  "标志文件": "__init__.py",
                  "说明": "中转生图",
                  "生效提示": "装完要重启 ComfyUI"
                }
                """);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.True(package.Loaded);
            Assert.Equal("", package.LoadFailureReason);
            Assert.Equal("relay_image_node", package.Name);
            Assert.Equal("custom_nodes/relay_image_node", package.HostRelativePath);
            Assert.Equal("__init__.py", package.MarkerFileName);
            Assert.Equal("中转生图", package.Description);
            Assert.Equal("装完要重启 ComfyUI", package.ActivationNote);
        }

        /// <summary>落点与标志文件拼在一起，就是「装没装」的判据路径。</summary>
        [Fact]
        public void MarkerPathCombinesInstallRootWithHostRelativePath()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "relay_image_node", GoodManifest);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));
            var installRoot = Path.Combine(workspace.Root, "宿主");

            Assert.Equal(
                Path.Combine(installRoot, "custom_nodes", "relay_image_node"),
                package.TargetDirectoryUnder(installRoot));
            Assert.Equal(
                Path.Combine(installRoot, "custom_nodes", "relay_image_node", "__init__.py"),
                package.MarkerPathUnder(installRoot));
        }

        /// <summary>子目录里没有 plugin.json 时也要产出一条「判不了」，不许静默跳过。</summary>
        [Fact]
        public void DirectoryWithoutManifestIsReportedNotSkipped()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            Directory.CreateDirectory(Path.Combine(DriverScriptPackage.ScriptsDirectory(workspace.Root, "comfyui"), "裸目录"));

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Equal("裸目录", package.Name);
            Assert.Contains("plugin.json", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>plugin.json 不是合法 JSON。</summary>
        [Fact]
        public void BrokenJsonIsReportedWithReason()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "坏包", "{ 这不是 JSON");

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Contains("不是合法 JSON", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>「名称」与目录名对不上：装完之后对账会错位，所以当场判坏。</summary>
        [Fact]
        public void NameMismatchingDirectoryIsBroken()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "甲", """
                {
                  "名称": "乙",
                  "宿主落点": "custom_nodes/甲",
                  "标志文件": "__init__.py"
                }
                """);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Contains("对不上", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>「宿主落点」带 ..：那会把文件写到宿主安装目录外面去，判坏。</summary>
        [Fact]
        public void HostRelativePathWithParentSegmentIsBroken()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "穿越包", """
                {
                  "名称": "穿越包",
                  "宿主落点": "custom_nodes/../../别处",
                  "标志文件": "__init__.py"
                }
                """);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Contains("..", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>「宿主落点」是绝对路径同样判坏——相对宿主安装目录是这个字段唯一的含义。</summary>
        [Theory]
        [InlineData("/etc/custom_nodes")]
        [InlineData("C:/别处/custom_nodes")]
        public void AbsoluteHostRelativePathIsBroken(string hostRelativePath)
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "绝对包", $$"""
                {
                  "名称": "绝对包",
                  "宿主落点": "{{hostRelativePath}}",
                  "标志文件": "__init__.py"
                }
                """);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Contains("相对", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>「标志文件」为空时判坏：没有它就没有「装没装」的判据。</summary>
        [Fact]
        public void EmptyMarkerFileIsBroken()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "没判据", """
                {
                  "名称": "没判据",
                  "宿主落点": "custom_nodes/没判据",
                  "标志文件": ""
                }
                """);

            var package = Assert.Single(DriverScriptPackage.LoadAll(workspace.Root, "comfyui"));

            Assert.False(package.Loaded);
            Assert.Contains("标志文件", package.LoadFailureReason, StringComparison.Ordinal);
        }

        /// <summary>多个包按名称序数序，且 Find 按名字精确取。</summary>
        [Fact]
        public void PackagesComeInOrdinalOrderAndFindIsExact()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver("comfyui");
            workspace.WritePackage("comfyui", "beta", GoodManifest.Replace("relay_image_node", "beta"));
            workspace.WritePackage("comfyui", "alpha", GoodManifest.Replace("relay_image_node", "alpha"));

            var names = DriverScriptPackage.LoadAll(workspace.Root, "comfyui").Select(package => package.Name).ToList();

            Assert.Equal(new[] { "alpha", "beta" }, names);
            Assert.NotNull(DriverScriptPackage.Find(workspace.Root, "comfyui", "alpha"));
            Assert.Null(DriverScriptPackage.Find(workspace.Root, "comfyui", "ALPHA"));
        }

        /// <summary>一份写对的自述，包名是 relay_image_node。</summary>
        private const string GoodManifest = """
            {
              "契约版本": "1.0.0",
              "名称": "relay_image_node",
              "宿主落点": "custom_nodes/relay_image_node",
              "标志文件": "__init__.py",
              "说明": "中转生图",
              "生效提示": "装完要重启 ComfyUI"
            }
            """;

        /// <summary>一次性的临时仓库。</summary>
        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "脚本包测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            /// <summary>写一份最小的 driver 自述，带不带「安装目录」这一格由调用方定。</summary>
            public void WriteDriver(string driverName, bool withInstallRoot = true)
            {
                var directory = Path.Combine(Root, "Bridges", driverName);
                Directory.CreateDirectory(directory);

                var schema = withInstallRoot
                    ? """{ "地址": { "类型": "string", "默认": "" }, "安装目录": { "类型": "string", "默认": "" } }"""
                    : """{ "地址": { "类型": "string", "默认": "" } }""";

                File.WriteAllText(Path.Combine(directory, "driver.json"), $$"""
                    {
                      "名称": "{{driverName}}",
                      "port": ["生图"],
                      "形态": "本地",
                      "契约版本": ">=1.0 <2.0",
                      "配置schema": {{schema}},
                      "密钥字段": [],
                      "试跑": "bridge.probe --Driver {{driverName}}",
                      "能力探测": "bridge.probe --Driver {{driverName}}",
                      "实现": "bridge-{{driverName}}",
                      "字段类型映射": {},
                      "表单分组字段": ""
                    }
                    """);
            }

            /// <summary>造一个脚本包目录：一份 plugin.json 加一个标志文件。</summary>
            public void WritePackage(string driverName, string packageName, string manifestJson)
            {
                var directory = Path.Combine(DriverScriptPackage.ScriptsDirectory(Root, driverName), packageName);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, DriverScriptPackage.ManifestFileName), manifestJson);
                File.WriteAllText(Path.Combine(directory, "__init__.py"), "# 测试用\n");
            }

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
