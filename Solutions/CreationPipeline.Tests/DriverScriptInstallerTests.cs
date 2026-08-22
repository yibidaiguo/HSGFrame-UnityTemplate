using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 把脚本包装进宿主的测试。这一族守着三条最贵的红线：
    ///
    /// 一、**不猜安装目录**：没配就说没配、指路去填，绝不拿常见路径试探。
    /// 二、**不删没认出来的目录**：覆盖装之前必须先在目标里认出本包的 plugin.json，
    ///     认不出来就原封不动退回来——递归删一个陌生目录是这条链路上最贵的错。
    /// 三、**link.json 里只有仓库根路径**：地址、密钥一个字都不许跟着装进宿主目录（决策 5）。
    ///
    /// 全部用临时目录，**一个具体机器路径都不写死**——本机 ComfyUI 装在哪只存在于 local.json。
    /// </summary>
    public class DriverScriptInstallerTests
    {
        /// <summary>正常装一次：文件落到宿主落点，link.json 写上仓库根，标志文件在。</summary>
        [Fact]
        public void InstallsPackageIntoConfiguredRoot()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot);

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.True(outcome.Succeeded, outcome.Message);
            var expected = Path.Combine(installRoot, "custom_nodes", "relay_image_node");
            Assert.Equal(Path.GetFullPath(expected), outcome.TargetDirectory);
            Assert.True(File.Exists(Path.Combine(expected, "__init__.py")));
            Assert.True(File.Exists(Path.Combine(expected, DriverScriptPackage.ManifestFileName)));
        }

        /// <summary>link.json 里**只有仓库根**：没有地址、没有密钥、没有别的键。</summary>
        [Fact]
        public void LinkFileCarriesOnlyRepositoryRoot()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot, secret: "这是一把不该外流的密钥");

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);
            Assert.True(outcome.Succeeded, outcome.Message);

            var linkText = File.ReadAllText(Path.Combine(outcome.TargetDirectory, DriverScriptPackage.LinkFileName));
            using var document = JsonDocument.Parse(linkText);
            var root = document.RootElement;

            Assert.Equal(
                Path.GetFullPath(workspace.Root).Replace('\\', '/'),
                root.GetProperty("仓库根").GetString());
            Assert.False(root.TryGetProperty("生图密钥", out _));
            Assert.False(root.TryGetProperty("地址", out _));
            Assert.DoesNotContain("不该外流", linkText, StringComparison.Ordinal);
        }

        /// <summary>字节码缓存不跟着装过去。</summary>
        [Fact]
        public void PycacheIsNotCopied()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var packageDirectory = Path.Combine(
                DriverScriptPackage.ScriptsDirectory(workspace.Root, "comfyui"), "relay_image_node");
            Directory.CreateDirectory(Path.Combine(packageDirectory, "__pycache__"));
            File.WriteAllText(Path.Combine(packageDirectory, "__pycache__", "nodes.cpython-311.pyc"), "x");
            File.WriteAllText(Path.Combine(packageDirectory, "nodes.pyc"), "x");

            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot);

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);
            Assert.True(outcome.Succeeded, outcome.Message);

            Assert.False(Directory.Exists(Path.Combine(outcome.TargetDirectory, "__pycache__")));
            Assert.False(File.Exists(Path.Combine(outcome.TargetDirectory, "nodes.pyc")));
        }

        /// <summary>没配安装目录时**不装、不猜**，并指路去填。</summary>
        [Fact]
        public void MissingInstallRootFailsWithDirections()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            workspace.WriteLocalSettings(installRoot: "");

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.False(outcome.Succeeded);
            Assert.Contains(DriverScriptPackage.InstallRootFieldName, outcome.Message, StringComparison.Ordinal);
            Assert.Equal("", outcome.TargetDirectory);
            Assert.Contains(outcome.Lines, line => line.Contains("bridge.config.set", StringComparison.Ordinal));
        }

        /// <summary>driver 自述里没有「安装目录」这一格时，说清该去哪儿加，而不是含糊报个失败。</summary>
        [Fact]
        public void DriverWithoutInstallRootFieldFails()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver(withInstallRoot: false);
            workspace.WritePackage();
            workspace.WriteLocalSettings(workspace.CreateInstallRoot());

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.False(outcome.Succeeded);
            Assert.Contains("driver.json", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>安装目录指向一个不存在的地方时失败，**且不替人把它建出来**。</summary>
        [Fact]
        public void NonexistentInstallRootIsNotCreated()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var missing = Path.Combine(workspace.Root, "并不存在的宿主");
            workspace.WriteLocalSettings(missing);

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.False(outcome.Succeeded);
            Assert.False(Directory.Exists(missing));
        }

        /// <summary>包名找不到时把现有的包列出来，别让人对着一个空失败猜。</summary>
        [Fact]
        public void UnknownPackageListsAvailableOnes()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            workspace.WriteLocalSettings(workspace.CreateInstallRoot());

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "没这个包", false, false);

            Assert.False(outcome.Succeeded);
            Assert.Contains(outcome.Lines, line => line.Contains("relay_image_node", StringComparison.Ordinal));
        }

        /// <summary>坏包不装，并把坏在哪原样带出来。</summary>
        [Fact]
        public void BrokenPackageIsNotInstalled()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage(manifestJson: "{ 坏掉的 JSON");
            workspace.WriteLocalSettings(workspace.CreateInstallRoot());

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.False(outcome.Succeeded);
            Assert.Contains("不是合法 JSON", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>目标已存在、又没带 --Force 时拒绝，并说清带什么参数才覆盖。</summary>
        [Fact]
        public void ExistingTargetWithoutForceIsRefused()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot);
            Assert.True(DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false).Succeeded);

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.False(outcome.Succeeded);
            Assert.Contains(outcome.Lines, line => line.Contains("--Force", StringComparison.Ordinal));
        }

        /// <summary>
        /// 目标已存在、带了 --Force，但那个目录**不是本包**时必须拒绝，
        /// 并且**原封不动**——这条是这一族里最要紧的一条。
        /// </summary>
        [Fact]
        public void ForceRefusesToDeleteUnrecognizedDirectory()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot);

            var target = Path.Combine(installRoot, "custom_nodes", "relay_image_node");
            Directory.CreateDirectory(target);
            var strangerFile = Path.Combine(target, "别人的重要文件.txt");
            File.WriteAllText(strangerFile, "别删我");

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, true);

            Assert.False(outcome.Succeeded);
            Assert.True(File.Exists(strangerFile));
            Assert.Equal("别删我", File.ReadAllText(strangerFile));
        }

        /// <summary>目标已存在、带了 --Force、且认出那就是本包时，覆盖装成功，旧的残留文件被清掉。</summary>
        [Fact]
        public void ForceOverwritesRecognizedInstall()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            var installRoot = workspace.CreateInstallRoot();
            workspace.WriteLocalSettings(installRoot);

            var first = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);
            Assert.True(first.Succeeded, first.Message);
            var staleFile = Path.Combine(first.TargetDirectory, "上一版才有的文件.py");
            File.WriteAllText(staleFile, "旧的");

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, true);

            Assert.True(outcome.Succeeded, outcome.Message);
            Assert.False(File.Exists(staleFile));
            Assert.True(File.Exists(Path.Combine(outcome.TargetDirectory, "__init__.py")));
        }

        /// <summary>成功的回报里要有落点绝对路径与包自述里的生效提示——产物在仓库外，这是唯一可核对的痕迹。</summary>
        [Fact]
        public void SuccessReportsAbsolutePathAndActivationNote()
        {
            using var workspace = new Workspace();
            workspace.WriteDriver();
            workspace.WritePackage();
            workspace.WriteLocalSettings(workspace.CreateInstallRoot());

            var outcome = DriverScriptInstaller.Install(workspace.Root, "comfyui", "relay_image_node", false, false);

            Assert.True(outcome.Succeeded, outcome.Message);
            Assert.True(Path.IsPathRooted(outcome.TargetDirectory));
            Assert.Contains(outcome.Lines, line => line.Contains("重启", StringComparison.Ordinal));
            Assert.Contains(outcome.Lines, line => line.Contains("git diff 看不见", StringComparison.Ordinal));
        }

        /// <summary>一次性的临时仓库 + 临时宿主。</summary>
        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "脚本安装测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public void WriteDriver(bool withInstallRoot = true)
            {
                var directory = Path.Combine(Root, "Bridges", "comfyui");
                Directory.CreateDirectory(directory);

                var schema = withInstallRoot
                    ? """{ "地址": { "类型": "string", "默认": "" }, "安装目录": { "类型": "string", "默认": "" } }"""
                    : """{ "地址": { "类型": "string", "默认": "" } }""";

                File.WriteAllText(Path.Combine(directory, "driver.json"), $$"""
                    {
                      "名称": "comfyui",
                      "port": ["生图"],
                      "形态": "本地",
                      "契约版本": ">=1.0 <2.0",
                      "配置schema": {{schema}},
                      "密钥字段": [],
                      "试跑": "bridge.probe --Driver comfyui",
                      "能力探测": "bridge.probe --Driver comfyui",
                      "实现": "bridge-comfyui",
                      "字段类型映射": {},
                      "表单分组字段": ""
                    }
                    """);
            }

            public void WritePackage(string manifestJson = null)
            {
                var directory = Path.Combine(
                    DriverScriptPackage.ScriptsDirectory(Root, "comfyui"), "relay_image_node");
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, DriverScriptPackage.ManifestFileName),
                    manifestJson ?? """
                        {
                          "契约版本": "1.0.0",
                          "名称": "relay_image_node",
                          "宿主落点": "custom_nodes/relay_image_node",
                          "标志文件": "__init__.py",
                          "说明": "中转生图",
                          "生效提示": "装完要重启 ComfyUI 才会加载这个节点。"
                        }
                        """);
                File.WriteAllText(Path.Combine(directory, "__init__.py"), "# 测试用\n");
            }

            /// <summary>造一个临时的「宿主根目录」，返回它的路径。</summary>
            public string CreateInstallRoot()
            {
                var installRoot = Path.Combine(Root, "宿主");
                Directory.CreateDirectory(installRoot);
                return installRoot;
            }

            /// <summary>写一份本机配置。密钥这一项是为了验证它**不会**跟着装进宿主。</summary>
            public void WriteLocalSettings(string installRoot, string secret = "")
            {
                var directory = Path.Combine(Root, "Tools", "CreationPipeline", "Config");
                Directory.CreateDirectory(directory);

                var escaped = (installRoot ?? "").Replace("\\", "\\\\");
                File.WriteAllText(Path.Combine(directory, "local.json"), $$"""
                    {
                      "生图密钥": "{{secret}}",
                      "下游配置": {
                        "comfyui": { "地址": "http://127.0.0.1:8188", "安装目录": "{{escaped}}" }
                      }
                    }
                    """);
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
