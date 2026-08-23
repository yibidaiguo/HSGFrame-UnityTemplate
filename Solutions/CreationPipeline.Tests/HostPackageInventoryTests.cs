using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 装机清单的测试。这一族守的是「没有的不说成有」：
    /// Unity 的 PackageCache 不存在时外部包必须是「未验」而不是「缺」，
    /// 能力探测输出不存在时依赖必须是「未验」而不是「缺」——
    /// 把「没查过」渲染成「没有」，人会去白装一遍已经装好的东西（决策 42）。
    /// </summary>
    public class HostPackageInventoryTests
    {
        /// <summary>本地形态 driver 的自述。driver.json 的「名称」必须与目录名一致，Load 会当场校验。</summary>
        private static string LocalDriverJson(string driverName)
        {
            return """
                {
                  "名称": "%名%",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "可执行文件": { "类型": "string", "默认": "" } },
                  "密钥字段": [],
                  "试跑": "bridge.probe --Driver %名%",
                  "能力探测": "bridge.probe --Driver %名%",
                  "实现": "bridge-%名%",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """.Replace("%名%", driverName);
        }

        private const string OnlineDriverJson = """
            {
              "名称": "tripo",
              "port": ["模型生成"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": { "地址": { "类型": "string", "默认": "" } },
              "密钥字段": ["模型生成密钥"],
              "试跑": "bridge.model --Driver tripo",
              "能力探测": "",
              "实现": "bridge-tripo",
              "字段类型映射": {},
              "表单分组字段": ""
            }
            """;

        /// <summary>带「安装目录」这一格的本地 driver 自述：这一格是「允不允许装脚本包」的判据。</summary>
        private const string InstallableDriverJson = """
            {
              "名称": "comfyui",
              "port": ["生图"],
              "形态": "本地",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {
                "地址": { "类型": "string", "默认": "" },
                "安装目录": { "类型": "string", "默认": "" }
              },
              "密钥字段": [],
              "试跑": "bridge.probe --Driver comfyui",
              "能力探测": "bridge.probe --Driver comfyui",
              "实现": "bridge-comfyui",
              "字段类型映射": {},
              "表单分组字段": ""
            }
            """;

        private const string DependencyJson = """
            {
              "契约版本": "1.0.0",
              "依赖": [
                {
                  "名称": "Impact-Pack",
                  "类别": "节点",
                  "版本": "8.0.0",
                  "来源": "https://github.com/ltdrdata/ComfyUI-Impact-Pack",
                  "安装命令": "git clone https://github.com/ltdrdata/ComfyUI-Impact-Pack custom_nodes/Impact-Pack",
                  "说明": "透明底裁切"
                }
              ]
            }
            """;

        /// <summary>UnityProject 与 Bridges 都不存在时返回空列表，不抛。</summary>
        [Fact]
        public void EmptyRepositoryProducesNoRows()
        {
            using var workspace = new Workspace();

            Assert.Empty(HostPackageInventory.Build(workspace.Root));
        }

        /// <summary>Unity 行在最前，driver 行按名称序数序跟在后面。</summary>
        [Fact]
        public void UnityRowComesFirstThenDriversInOrdinalOrder()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """{ "dependencies": { "com.unity.ugui": "2.0.0" } }""");
            WriteDriver(workspace.Root, "zzz", LocalDriverJson("zzz"));
            WriteDriver(workspace.Root, "aaa", LocalDriverJson("aaa"));

            var names = HostPackageInventory.Build(workspace.Root).Select(row => row.Name).ToArray();

            Assert.Equal(new[] { "unity", "aaa", "zzz" }, names);
        }

        /// <summary>file: 形态的本地包：目录里有 package.json 才算已装，没有就是缺。</summary>
        [Fact]
        public void LocalUnityPackageNeedsItsPackageFile()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """
                {
                  "dependencies": {
                    "com.hsgframe.audio": "file:../../Packages/com.hsgframe.audio",
                    "com.hsgframe.save": "file:../../Packages/com.hsgframe.save"
                  }
                }
                """);
            WriteFile(Path.Combine(workspace.Root, "Packages", "com.hsgframe.audio", "package.json"), """{ "name": "com.hsgframe.audio" }""");

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            Assert.Equal(HostPackageInventory.StateInstalled, Package(unity, "com.hsgframe.audio").State);
            Assert.Equal(HostPackageInventory.StateMissing, Package(unity, "com.hsgframe.save").State);
        }

        /// <summary>
        /// PackageCache 整个不存在（这台机器没用 Unity 打开过工程）时，外部包是「未验」而不是「缺」，
        /// 并且知会里明说这件事——否则人会以为包真的没装。
        /// </summary>
        [Fact]
        public void ExternalUnityPackageIsUnverifiedWhenPackageCacheIsAbsent()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """
                { "dependencies": { "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git#main" } }
                """);

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            var package = Package(unity, "com.coplaydev.unity-mcp");
            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Equal("main", package.VersionRequirement);
            Assert.Contains(unity.Notes, note => note.Contains("PackageCache", StringComparison.Ordinal));
        }

        /// <summary>PackageCache 里有 &lt;包名&gt;@&lt;哈希&gt; 目录就是已装；只有别的包时是缺。</summary>
        [Fact]
        public void ExternalUnityPackageReadsPackageCacheDirectories()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """
                {
                  "dependencies": {
                    "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git#main",
                    "com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git#3.0.5"
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspace.Root, "UnityProject", "Library", "PackageCache", "com.coplaydev.unity-mcp@8bd91ce7bd3a"));

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            Assert.Equal(HostPackageInventory.StateInstalled, Package(unity, "com.coplaydev.unity-mcp").State);
            Assert.Equal(HostPackageInventory.StateMissing, Package(unity, "com.tuyoogame.yooasset").State);
        }

        /// <summary>com.unity.* 官方包不进表，但要有一条知会说明它们被排除了。</summary>
        [Fact]
        public void OfficialUnityPackagesAreCountedInNotesNotListed()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """
                {
                  "dependencies": {
                    "com.unity.ugui": "2.0.0",
                    "com.unity.mathematics": "1.3.3",
                    "com.hsgframe.save": "file:../../Packages/com.hsgframe.save"
                  }
                }
                """);

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            Assert.DoesNotContain(unity.Packages, package => package.Name.StartsWith("com.unity.", StringComparison.Ordinal));
            Assert.Contains(unity.Notes, note => note.Contains("2 个 com.unity.*", StringComparison.Ordinal));
        }

        /// <summary>manifest.json 不存在时这一行仍然产出，读失败写清原因（决策 43）。</summary>
        [Fact]
        public void MissingManifestStillProducesUnityRowWithReason()
        {
            using var workspace = new Workspace();
            WriteFile(Path.Combine(workspace.Root, "UnityProject", "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.3.11f1");

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            Assert.NotEqual("", unity.LoadFailureReason);
            Assert.Empty(unity.Packages);
        }

        /// <summary>ProjectVersion.txt 读不到版本时本体是「未验」，不是「缺」——判不了不等于没装。</summary>
        [Fact]
        public void UnknownEditorVersionMakesHostUnverified()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "", """{ "dependencies": {} }""");

            var unity = HostPackageInventory.Build(workspace.Root).Single(row => row.Name == "unity");

            Assert.Equal(HostPackageInventory.StateUnverified, unity.HostState);
        }

        /// <summary>本地形态 driver：可执行文件没填是缺，填了但文件不在也是缺，文件在才是已装。</summary>
        [Fact]
        public void LocalDriverHostStateFollowsExecutableFile()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "blender", LocalDriverJson("blender"));

            Assert.Equal(HostPackageInventory.StateMissing, Host(workspace.Root, "blender").HostState);

            var executablePath = Path.Combine(workspace.Root, "blender.exe");
            WriteLocalSettings(workspace.Root, """{ "下游配置": { "blender": { "可执行文件": "不存在的路径.exe" } } }""");
            Assert.Equal(HostPackageInventory.StateMissing, Host(workspace.Root, "blender").HostState);

            WriteFile(executablePath, "占位");
            WriteLocalSettings(workspace.Root, "{ \"下游配置\": { \"blender\": { \"可执行文件\": " + Quote(executablePath) + " } } }");
            Assert.Equal(HostPackageInventory.StateInstalled, Host(workspace.Root, "blender").HostState);
        }

        /// <summary>
        /// 有「地址」的本地 driver：没试跑过是「未验」，对着这个地址试跑通了才是「已装」。
        /// 这一条守的是卡片会不会更新——试跑跑通、依赖都染绿了，本体那一格还挂着「点试跑一次」，
        /// 人看到的就是一张永远不动的卡。
        /// </summary>
        [Fact]
        public void AddressDriverHostStateFollowsLastTrial()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteLocalSettings(workspace.Root, """{ "下游配置": { "comfyui": { "地址": "http://127.0.0.1:8188" } } }""");

            Assert.Equal(HostPackageInventory.StateUnverified, Host(workspace.Root, "comfyui").HostState);

            WriteFile(ProvisionPaths.ProbeResultFile(workspace.Root, "comfyui"), """
                {
                  "节点": [], "模型": [], "lora": [],
                  "探于": "http://127.0.0.1:8188",
                  "探测时间": "2026-08-22T12:00:00.0000000Z"
                }
                """);

            var host = Host(workspace.Root, "comfyui");

            Assert.Equal(HostPackageInventory.StateInstalled, host.HostState);
            Assert.Contains("上次试跑连上了这个地址", host.HostDetail, StringComparison.Ordinal);
            Assert.Equal("", host.HostNextStep);
        }

        /// <summary>
        /// 探测产出是跟着地址走的：地址改过之后那份产出是上一个地址的战果，本体必须记回「未验」，
        /// 并且把两个地址都点名——不点名的话，人从卡上看不出「这绿是旧的」。
        /// </summary>
        [Fact]
        public void AddressDriverGoesBackToUnverifiedWhenAddressChanged()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteLocalSettings(workspace.Root, """{ "下游配置": { "comfyui": { "地址": "http://127.0.0.1:9000" } } }""");
            WriteFile(ProvisionPaths.ProbeResultFile(workspace.Root, "comfyui"), """
                { "节点": [], "模型": [], "lora": [], "探于": "http://127.0.0.1:8188" }
                """);

            var host = Host(workspace.Root, "comfyui");

            Assert.Equal(HostPackageInventory.StateUnverified, host.HostState);
            Assert.Contains("http://127.0.0.1:8188", host.HostDetail, StringComparison.Ordinal);
            Assert.Contains("http://127.0.0.1:9000", host.HostDetail, StringComparison.Ordinal);
        }

        /// <summary>
        /// 没盖章的老产出证明不了它试的是哪个地址：那是「未验」，不能拿来染绿。
        /// </summary>
        [Fact]
        public void UnstampedProbeResultDoesNotProveTheAddress()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteLocalSettings(workspace.Root, """{ "下游配置": { "comfyui": { "地址": "http://127.0.0.1:8188" } } }""");
            WriteFile(ProvisionPaths.ProbeResultFile(workspace.Root, "comfyui"), """
                { "节点": [], "模型": [], "lora": [] }
                """);

            var host = Host(workspace.Root, "comfyui");

            Assert.Equal(HostPackageInventory.StateUnverified, host.HostState);
            Assert.Contains("没盖地址章", host.HostDetail, StringComparison.Ordinal);
        }

        /// <summary>依赖清单在、能力探测输出不在：每条依赖都是「未验」，下一步指向探测命令。</summary>
        [Fact]
        public void DependenciesAreUnverifiedBeforeAnyProbe()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", LocalDriverJson("comfyui"));
            WriteFile(Path.Combine(workspace.Root, "Bridges", "comfyui", "dependencies.json"), DependencyJson);

            var package = Package(Host(workspace.Root, "comfyui"), "Impact-Pack");

            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Contains("bridge.probe", package.NextStep, StringComparison.Ordinal);
            Assert.Contains("custom_nodes", package.InstallCommand, StringComparison.Ordinal);
        }

        /// <summary>探测输出里有它就是已装；探测跑过但没探到才是缺。</summary>
        [Fact]
        public void DependencyStateFollowsProbeResult()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", LocalDriverJson("comfyui"));
            WriteFile(Path.Combine(workspace.Root, "Bridges", "comfyui", "dependencies.json"), DependencyJson);

            WriteFile(ProvisionPaths.ProbeResultFile(workspace.Root, "comfyui"), """
                { "节点": [{ "名": "Impact-Pack", "版本": "8.0.0" }], "模型": [], "lora": [] }
                """);
            Assert.Equal(HostPackageInventory.StateInstalled, Package(Host(workspace.Root, "comfyui"), "Impact-Pack").State);

            WriteFile(ProvisionPaths.ProbeResultFile(workspace.Root, "comfyui"), """
                { "节点": [], "模型": [], "lora": [] }
                """);
            Assert.Equal(HostPackageInventory.StateMissing, Package(Host(workspace.Root, "comfyui"), "Impact-Pack").State);
        }

        /// <summary>scripts/ 下的驱动脚本随仓库走，状态是「无需安装」。</summary>
        [Fact]
        public void DriverScriptsAreListedAsNotNeedingInstall()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "blender", LocalDriverJson("blender"));
            WriteFile(Path.Combine(workspace.Root, "Bridges", "blender", "scripts", "probe.py"), "占位");

            var package = Package(Host(workspace.Root, "blender"), "probe.py");

            Assert.Equal(HostPackageInventory.StateNotNeeded, package.State);
            Assert.Equal("驱动脚本", package.Category);
        }

        /// <summary>
        /// 目录型脚本包（scripts/ 下带 plugin.json 的目录）是**要装进宿主**的那一支：
        /// 没配安装目录时状态必须是「未验」而不是「缺」——判据都还没凑齐，
        /// 此时说「缺」会让人去白装一遍可能早就装好的东西（决策 42 的同一条道理）。
        /// </summary>
        [Fact]
        public void ScriptPackageWithoutInstallRootIsUnverifiedNotMissing()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteScriptPackage(workspace.Root, "relay_image_node");

            var package = Package(Host(workspace.Root, "comfyui"), "relay_image_node");

            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Equal("驱动脚本", package.Category);
            Assert.Contains("安装目录", package.Evidence, StringComparison.Ordinal);
        }

        /// <summary>driver 自述里没有「安装目录」这一格时同样是「未验」，并指路去 driver.json 加。</summary>
        [Fact]
        public void ScriptPackageOnDriverWithoutInstallFieldIsUnverified()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", LocalDriverJson("comfyui"));
            WriteScriptPackage(workspace.Root, "relay_image_node");

            var package = Package(Host(workspace.Root, "comfyui"), "relay_image_node");

            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Contains("driver.json", package.Evidence, StringComparison.Ordinal);
        }

        /// <summary>坏包（plugin.json 缺或坏）也要列出来并说清坏在哪，不许从清单上消失。</summary>
        [Fact]
        public void BrokenScriptPackageIsListedAsUnverified()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteFile(
                Path.Combine(workspace.Root, "Bridges", "comfyui", "scripts", "坏包", "plugin.json"),
                "{ 这不是 JSON");

            var package = Package(Host(workspace.Root, "comfyui"), "坏包");

            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Contains("不是合法 JSON", package.Evidence, StringComparison.Ordinal);
        }

        /// <summary>配了安装目录、标志文件不在 → 「缺」，并给出能直接点的安装命令。</summary>
        [Fact]
        public void ScriptPackageMissingFromHostIsMissingWithInstallCommand()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteScriptPackage(workspace.Root, "relay_image_node");

            var installRoot = Path.Combine(workspace.Root, "宿主");
            Directory.CreateDirectory(installRoot);
            WriteLocalSettings(workspace.Root,
                $$"""{ "下游配置": { "comfyui": { "安装目录": {{Quote(installRoot)}} } } }""");

            var package = Package(Host(workspace.Root, "comfyui"), "relay_image_node");

            Assert.Equal(HostPackageInventory.StateMissing, package.State);
            Assert.Equal(
                "bridge.script.install --Driver comfyui --Name relay_image_node",
                package.InstallCommand);
        }

        /// <summary>配了安装目录、标志文件真在 → 「已装」，依据里带上那个绝对路径。</summary>
        [Fact]
        public void ScriptPackagePresentInHostIsInstalled()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "comfyui", InstallableDriverJson);
            WriteScriptPackage(workspace.Root, "relay_image_node");

            var installRoot = Path.Combine(workspace.Root, "宿主");
            var marker = Path.Combine(installRoot, "custom_nodes", "relay_image_node", "__init__.py");
            WriteFile(marker, "# 装好了");
            WriteLocalSettings(workspace.Root,
                $$"""{ "下游配置": { "comfyui": { "安装目录": {{Quote(installRoot)}} } } }""");

            var package = Package(Host(workspace.Root, "comfyui"), "relay_image_node");

            Assert.Equal(HostPackageInventory.StateInstalled, package.State);
            Assert.Contains(marker, package.Evidence, StringComparison.Ordinal);
            Assert.Equal("", package.NextStep);
        }

        /// <summary>
        /// 线上形态 driver：本体「无需安装」，没有本机桥接包；
        /// 密钥只报键名与「在不在」，说明文本里绝不出现值（决策 5、78）。
        /// </summary>
        [Fact]
        public void OnlineDriverNeedsNoLocalInstallAndReportsSecretKeysOnly()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "tripo", OnlineDriverJson);

            var missingRow = Host(workspace.Root, "tripo");
            Assert.Equal(HostPackageInventory.StateNotNeeded, missingRow.HostState);
            Assert.Empty(missingRow.Packages);
            Assert.Contains(missingRow.Notes, note => note.Contains("模型生成密钥", StringComparison.Ordinal));

            WriteLocalSettings(workspace.Root, """{ "模型生成密钥": "秘密值不许出现在任何文案里" }""");
            var configuredRow = Host(workspace.Root, "tripo");
            Assert.Contains(configuredRow.Notes, note => note.Contains("密钥键齐了", StringComparison.Ordinal));
            Assert.DoesNotContain(configuredRow.Notes, note => note.Contains("秘密值", StringComparison.Ordinal));
        }

        /// <summary>driver.json 坏掉时这一行仍然产出，读失败非空（决策 43：烂在库里的必须让人看见）。</summary>
        [Fact]
        public void BrokenDriverJsonStillProducesRowWithReason()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "broken", "{ \"名称\": \"broken\", ");

            var row = Host(workspace.Root, "broken");

            Assert.NotEqual("", row.LoadFailureReason);
            Assert.Empty(row.Packages);
        }

        /// <summary>插件声明清单文件不存在是正常状态：Loaded 为真、条目为空、没有原因。</summary>
        [Fact]
        public void MissingPluginManifestIsNormalNotBroken()
        {
            using var workspace = new Workspace();

            var manifest = EditorPluginManifest.Load(workspace.Root);

            Assert.True(manifest.Loaded);
            Assert.Empty(manifest.Entries);
            Assert.Equal("", manifest.LoadFailureReason);
        }

        /// <summary>
        /// 声明了插件但「标志路径」还没填：状态是「未验」，下一步就是去把落点填上。
        /// 这一支是给「刚声明、还没装」用的，把它记成「缺」等于替人断言那个插件不在。
        /// </summary>
        [Fact]
        public void DeclaredPluginWithoutMarkerPathIsUnverified()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """{ "dependencies": {} }""");
            WritePluginManifest(workspace.Root, PluginJson("unity", ""));

            var package = Package(Host(workspace.Root, "unity"), "厂商插件");

            Assert.Equal("编辑器插件", package.Category);
            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
            Assert.Contains("标志路径", package.NextStep, StringComparison.Ordinal);
        }

        /// <summary>标志路径指到的目录在 → 已装；不在 → 缺，且下一步就是声明里写的安装步骤。</summary>
        [Fact]
        public void DeclaredPluginStateFollowsMarkerPath()
        {
            using var workspace = new Workspace();
            WriteUnityProject(workspace.Root, "6000.3.11f1", """{ "dependencies": {} }""");
            WritePluginManifest(workspace.Root, PluginJson("unity", "UnityProject/Assets/Plugins/厂商插件"));

            var missing = Package(Host(workspace.Root, "unity"), "厂商插件");
            Assert.Equal(HostPackageInventory.StateMissing, missing.State);
            Assert.Equal("导入那个 unitypackage", missing.NextStep);

            Directory.CreateDirectory(Path.Combine(workspace.Root, "UnityProject", "Assets", "Plugins", "厂商插件"));
            Assert.Equal(HostPackageInventory.StateInstalled, Package(Host(workspace.Root, "unity"), "厂商插件").State);
        }

        /// <summary>插件不只能挂 Unity：宿主写成某个 driver 名时，它出现在那个 driver 行上。</summary>
        [Fact]
        public void DeclaredPluginCanTargetADriverHost()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "blender", LocalDriverJson("blender"));
            WritePluginManifest(workspace.Root, PluginJson("blender", ""));

            var package = Package(Host(workspace.Root, "blender"), "厂商插件");

            Assert.Equal("编辑器插件", package.Category);
            Assert.Equal(HostPackageInventory.StateUnverified, package.State);
        }

        /// <summary>
        /// 声明的「宿主」在这个仓库里找不到时，末尾多出一行把它挂住——
        /// 不挂住的话这条声明谁都不管，而人以为声明了就管上了（决策 43）。
        /// </summary>
        [Fact]
        public void PluginWithUnknownHostGetsItsOwnRow()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "blender", LocalDriverJson("blender"));
            WritePluginManifest(workspace.Root, PluginJson("不存在的宿主", ""));

            var rows = HostPackageInventory.Build(workspace.Root);

            var last = rows[rows.Count - 1];
            Assert.Equal("插件声明", last.Name);
            Assert.Equal("声明", last.Kind);
            Assert.Equal("厂商插件", Assert.Single(last.Packages).Name);
        }

        /// <summary>插件声明清单是坏 JSON：末尾一行写清原因，其余宿主行照常产出。</summary>
        [Fact]
        public void BrokenPluginManifestIsReportedInItsOwnRow()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "blender", LocalDriverJson("blender"));
            WritePluginManifest(workspace.Root, "{ \"插件\": [ ");

            var rows = HostPackageInventory.Build(workspace.Root);

            Assert.Equal("blender", rows[0].Name);
            var last = rows[rows.Count - 1];
            Assert.Equal("插件声明", last.Name);
            Assert.NotEqual("", last.LoadFailureReason);
        }

        /// <summary>
        /// 自述里给字段写了「说明」，那句话就是面板上这一格的提示——
        /// 一串 token 长得都一样，认错了就把东西写进别人的地盘，所以说明得跟着字段走。
        /// </summary>
        [Fact]
        public void FieldHintComesFromSchemaNote()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "feishu", AnnotatedDriverJson);

            var field = Field(Host(workspace.Root, "feishu"), "知识空间标识");

            Assert.Equal("策划文档落脚的知识库空间：space_id，一串纯数字", field.Hint);
        }

        /// <summary>自述没写「说明」的通用格退回内置那句；两种格子在同一份自述里各走各的。</summary>
        [Fact]
        public void FieldWithoutSchemaNoteFallsBackToBuiltInHint()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "feishu", AnnotatedDriverJson);

            var host = Host(workspace.Root, "feishu");

            Assert.Equal("一次调用等多久算超时", Field(host, "超时秒").Hint);
            Assert.Equal("卡片发给谁：open_id", Field(host, "测试收件人").Hint);
        }

        /// <summary>「说明」这一句不影响别的：字段照样可填、类型照样按自述读。</summary>
        [Fact]
        public void SchemaNoteDoesNotDisturbTypeOrValue()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "feishu", AnnotatedDriverJson);
            WriteLocalSettings(workspace.Root, """
                { "下游配置": { "feishu": { "知识空间标识": "7676450654847503634", "超时秒": 60 } } }
                """);

            var host = Host(workspace.Root, "feishu");

            var space = Field(host, "知识空间标识");
            Assert.Equal("string", space.FieldType);
            Assert.Equal("7676450654847503634", space.Value);
            Assert.True(space.IsConfigured);
            Assert.Equal("number", Field(host, "超时秒").FieldType);
            Assert.False(Field(host, "测试收件人").IsConfigured);
        }

        /// <summary>带「说明」的线上 driver 自述：一格写了说明、两格没写（一格有内置兜底，一格没有）。</summary>
        private const string AnnotatedDriverJson = """
            {
              "名称": "feishu",
              "port": ["需求编辑端"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {
                "知识空间标识": {
                  "类型": "string",
                  "默认": "",
                  "说明": "策划文档落脚的知识库空间：space_id，一串纯数字"
                },
                "测试收件人": { "类型": "string", "默认": "", "说明": "卡片发给谁：open_id" },
                "超时秒": { "类型": "number", "默认": 60 }
              },
              "密钥字段": [],
              "试跑": "bridge.provision --Driver feishu --DryRun true",
              "能力探测": "",
              "实现": "bridge-feishu",
              "字段类型映射": {},
              "表单分组字段": ""
            }
            """;

        /// <summary>一条插件声明的 JSON：宿主与标志路径按参数填，其余固定。</summary>
        private static string PluginJson(string hostName, string markerPath)
        {
            return """
                {
                  "契约版本": "1.0.0",
                  "插件": [
                    {
                      "名称": "厂商插件",
                      "宿主": "%宿主%",
                      "标志路径": "%标志%",
                      "版本": "latest",
                      "来源": "https://example.invalid/plugin",
                      "安装步骤": "导入那个 unitypackage",
                      "说明": "测试用"
                    }
                  ]
                }
                """.Replace("%宿主%", hostName).Replace("%标志%", markerPath);
        }

        private static void WritePluginManifest(string repositoryRoot, string json)
        {
            WriteFile(EditorPluginManifest.ManifestFile(repositoryRoot), json);
        }

        private static HostInventoryRow Host(string repositoryRoot, string name)
        {
            return HostPackageInventory.Build(repositoryRoot).Single(row => row.Name == name);
        }

        private static HostPackageEntry Package(HostInventoryRow row, string name)
        {
            return row.Packages.Single(package => package.Name == name);
        }

        private static HostConfigFieldEntry Field(HostInventoryRow row, string name)
        {
            return row.EditableFields.Single(field => field.Name == name);
        }

        /// <summary>把一段路径包成 JSON 字符串字面量：反斜杠要转义，否则 Windows 路径直接把 JSON 写坏。</summary>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\") + "\"";
        }

        private static void WriteUnityProject(string repositoryRoot, string editorVersion, string manifestJson)
        {
            var versionText = editorVersion.Length == 0 ? "m_EditorVersionWithRevision: 空" : "m_EditorVersion: " + editorVersion;
            WriteFile(Path.Combine(repositoryRoot, "UnityProject", "ProjectSettings", "ProjectVersion.txt"), versionText);
            WriteFile(Path.Combine(repositoryRoot, "UnityProject", "Packages", "manifest.json"), manifestJson);
        }

        private static void WriteDriver(string repositoryRoot, string driverName, string driverJson)
        {
            WriteFile(Path.Combine(repositoryRoot, "Bridges", driverName, "driver.json"), driverJson);
        }

        /// <summary>造一个目录型脚本包：一份写对的 plugin.json 加一个标志文件。</summary>
        private static void WriteScriptPackage(string repositoryRoot, string packageName)
        {
            var directory = Path.Combine(repositoryRoot, "Bridges", "comfyui", "scripts", packageName);
            WriteFile(Path.Combine(directory, "plugin.json"), $$"""
                {
                  "契约版本": "1.0.0",
                  "名称": "{{packageName}}",
                  "宿主落点": "custom_nodes/{{packageName}}",
                  "标志文件": "__init__.py",
                  "说明": "测试用",
                  "生效提示": "装完要重启 ComfyUI。"
                }
                """);
            WriteFile(Path.Combine(directory, "__init__.py"), "# 测试用");
        }

        private static void WriteLocalSettings(string repositoryRoot, string json)
        {
            WriteFile(Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json"), json);
        }

        private static void WriteFile(string filePath, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "装机清单测试-" + Guid.NewGuid().ToString("N"));
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
