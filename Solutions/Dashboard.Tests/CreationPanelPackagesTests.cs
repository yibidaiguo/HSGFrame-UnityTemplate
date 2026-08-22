using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>
    /// 面板桥接包页的读取器与路由测试。判定本身在 HostPackageInventoryTests 里守，
    /// 这一族只守两件事：形状转换没丢字段，以及页面拿到的 JSON 键名就是页面读的那几个——
    /// 键名改了而页面没改，那一页会静悄悄地整列空白，编译与单测却全绿。
    /// </summary>
    public sealed class CreationPanelPackagesTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根。</summary>
        public CreationPanelPackagesTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板桥接包测试-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>UnityProject 与 Bridges 都不存在时返回空列表，不抛。</summary>
        [Fact]
        public void EmptyRepositoryReturnsEmptyWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadHostPackages(_repositoryRoot));
        }

        /// <summary>一个本地 driver：宿主行的种类、本体状态与驱动脚本条目都映射过来了。</summary>
        [Fact]
        public void LocalDriverRowCarriesKindStateAndScripts()
        {
            WriteDriver("blender");
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", "blender", "scripts", "probe.py"), "占位");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));

            Assert.Equal("blender", row.Name);
            Assert.Equal("本机服务", row.Kind);
            Assert.Equal("缺", row.HostState);
            Assert.Equal("bridge.probe --Driver blender", row.TrialCommand);
            var package = Assert.Single(row.Packages);
            Assert.Equal("probe.py", package.Name);
            Assert.Equal("无需安装", package.State);
        }

        /// <summary>
        /// 序列化出来的键名就是 panel.js 读的那几个。这条测试是页面与后端之间唯一的对账——
        /// 改了 JsonPropertyName 而没改页面，页面只会整列空白，不会报错。
        /// </summary>
        [Fact]
        public void SerializedKeysMatchWhatThePageReads()
        {
            WriteDriver("blender");
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", "blender", "scripts", "probe.py"), "占位");

            var json = JsonSerializer.Serialize(
                CreationPanelReader.ReadHostPackages(_repositoryRoot),
                new JsonSerializerOptions(JsonSerializerOptions.Default)
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

            foreach (var key in new[] { "宿主", "种类", "本体", "本体依据", "版本", "本体下一步", "包", "字段", "声明", "知会", "试跑", "读失败" })
            {
                Assert.Contains("\"" + key + "\":", json, StringComparison.Ordinal);
            }

            foreach (var key in new[] { "名", "类别", "状态", "依据", "来源", "安装命令", "下一步" })
            {
                Assert.Contains("\"" + key + "\":", json, StringComparison.Ordinal);
            }
        }

        /// <summary>没配仓库根时 /api/panel/packages 回 503 而不是空数组：没配置与真没有是两回事。</summary>
        [Fact]
        public async Task PackagesRouteReturnsServiceUnavailableWithoutRepositoryRoot()
        {
            using var server = new DashboardServer(new LogEventChannel(), 0);
            server.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/api/panel/packages");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        /// <summary>配了仓库根时 /api/panel/packages 回 200 且正文是一个数组。</summary>
        [Fact]
        public async Task PackagesRouteReturnsArrayWithRepositoryRoot()
        {
            WriteDriver("blender");
            using var server = new DashboardServer(
                new LogEventChannel(),
                0,
                _repositoryRoot,
                Path.Combine(_repositoryRoot, "Pools"),
                null);
            server.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/api/panel/packages");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Equal("blender", document.RootElement[0].GetProperty("宿主").GetString());
        }

        /// <summary>
        /// 密钥字段在接口里**永远没有值**：哪怕本机配置里真填了，「值」也是空串，只有「已配」为真。
        /// 写这一侧 2026-08-22 放开了（面板能存密钥），读这一侧一寸没让——
        /// 这条测试就是那一寸：值一旦漏进接口返回，它就会被预填进输入框、进截图、进聊天记录。
        /// </summary>
        [Fact]
        public void SecretFieldNeverCarriesItsValue()
        {
            WriteDriver("demo", """["演示密钥"]""");
            WriteFile(
                Path.Combine(_repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json"),
                """{ "演示密钥": "不许出现在接口返回里" }""");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));
            var secret = Assert.Single(row.Fields, field => field.IsSecret);

            Assert.True(secret.IsConfigured);
            Assert.Equal("", secret.Value);

            var json = JsonSerializer.Serialize(
                CreationPanelReader.ReadHostPackages(_repositoryRoot),
                new JsonSerializerOptions(JsonSerializerOptions.Default)
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            Assert.DoesNotContain("不许出现在接口返回里", json, StringComparison.Ordinal);
        }

        /// <summary>非密钥字段带着当前值出来——页面要预填进输入框，才谈得上「就地改」。</summary>
        [Fact]
        public void PlainFieldCarriesItsCurrentValue()
        {
            WriteDriver("blender");
            WriteFile(
                Path.Combine(_repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json"),
                """{ "下游配置": { "blender": { "可执行文件": "D:/Tools/Blender/blender.exe" } } }""");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));
            var field = Assert.Single(row.Fields, candidate => candidate.Name == "可执行文件");

            Assert.False(field.IsSecret);
            Assert.True(field.IsConfigured);
            Assert.Equal("D:/Tools/Blender/blender.exe", field.Value);
        }

        /// <summary>
        /// 身份接口报出自己挂在哪个仓库根上。探活的脚本靠它区分「这是我的面板」与
        /// 「这是隔壁项目的面板」——只探端口的话，8766 上跑着别的仓库时会被当成自己的，
        /// 人被送进另一个项目的数据里，而页面看着一切正常（真踩过）。
        /// </summary>
        [Fact]
        public async Task IdentityRouteReportsTheRepositoryItServes()
        {
            Directory.CreateDirectory(_repositoryRoot);
            using var server = new DashboardServer(
                new LogEventChannel(),
                0,
                _repositoryRoot,
                Path.Combine(_repositoryRoot, "Pools"),
                null);
            server.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/api/panel/identity");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(_repositoryRoot, document.RootElement.GetProperty("仓库根").GetString());
            Assert.Equal(Path.GetFileName(_repositoryRoot), document.RootElement.GetProperty("仓库名").GetString());
            Assert.Equal(server.Port, document.RootElement.GetProperty("端口").GetInt32());
        }

        /// <summary>
        /// 没配仓库根的面板，身份接口仍然回 200、仓库根给空串——**不能**跟着别的接口回 503。
        /// 探活的人要能区分「没配仓库根的面板」与「别的仓库的面板」：两者都不该被当成自己的，
        /// 但 503 会让调用方以为是接口坏了，进而走上「当成自己的」那条路。
        /// </summary>
        [Fact]
        public async Task IdentityRouteAnswersEvenWithoutARepositoryRoot()
        {
            using var server = new DashboardServer(new LogEventChannel(), 0);
            server.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/api/panel/identity");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            Assert.Equal("", document.RootElement.GetProperty("仓库根").GetString());
        }

        /// <summary>清理临时仓库根。</summary>
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
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void WriteDriver(string driverName)
        {
            WriteDriver(driverName, "[]");
        }

        private void WriteDriver(string driverName, string secretFieldsJson)
        {
            var driverJson = """
                {
                  "名称": "%名%",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "可执行文件": { "类型": "string", "默认": "" } },
                  "密钥字段": %密钥%,
                  "试跑": "bridge.probe --Driver %名%",
                  "能力探测": "bridge.probe --Driver %名%",
                  "实现": "bridge-%名%",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """.Replace("%名%", driverName).Replace("%密钥%", secretFieldsJson);
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", driverName, "driver.json"), driverJson);
        }

        private static void WriteFile(string filePath, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }
    }
}
