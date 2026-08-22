using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>
    /// 面板上「模型」那一格的测试。这一族守的是页面与后端之间那份不成文的约定：
    /// 键名（模型格 / 自动说明 / 探测）、以及**清单空不等于没有可选**这句话的措辞。
    /// 键名改了而页面没改，那一格会静悄悄退化成一个普通输入框——编译与单测却全绿。
    /// </summary>
    public sealed class CreationPanelModelFieldTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根。</summary>
        public CreationPanelModelFieldTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板模型格测试-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>声明了「选项来源: 探测.模型」的那一格是模型格，并带着「自动会挑谁」那句话。</summary>
        [Fact]
        public void ModelFieldIsMarkedAndCarriesAutoNote()
        {
            WriteOnlineDriverWithModelField("faketalk");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));
            var modelField = row.Fields.Single(field => field.Name == "模型");
            var endpointField = row.Fields.Single(field => field.Name == "地址");

            Assert.True(modelField.IsModelField);
            Assert.False(endpointField.IsModelField);
            Assert.Contains("自动", modelField.AutoNote);
            Assert.Equal("", endpointField.AutoNote);
        }

        /// <summary>
        /// 还没探过时，那句话说的必须是「还没探过」，**不许**说成「没有可选的」。
        /// 前者的下一步是「去点一次重探」，后者是一句我们没资格下的结论。
        /// </summary>
        [Fact]
        public void UnprobedModelFieldSaysNotProbedYetInsteadOfNoOptions()
        {
            WriteOnlineDriverWithModelField("faketalk");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));
            var modelField = row.Fields.Single(field => field.Name == "模型");

            Assert.Empty(modelField.Options);
            Assert.Contains("还没探过", modelField.AutoNote);
            Assert.DoesNotContain("没有可选", modelField.AutoNote);
        }

        /// <summary>探过了：选项就是下游报的那几个，而「自动」会挑序数序第一项。</summary>
        [Fact]
        public void ProbedModelFieldListsWhatDownstreamReported()
        {
            WriteOnlineDriverWithModelField("faketalk");
            WriteProbeResult("faketalk", new[] { "丙-模型", "乙-模型" }, "https://探过的/v1");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));
            var modelField = row.Fields.Single(field => field.Name == "模型");

            Assert.Equal(new[] { "丙-模型", "乙-模型" }.OrderBy(name => name, StringComparer.Ordinal), modelField.Options);
            Assert.Contains("会挑", modelField.AutoNote);
        }

        /// <summary>宿主行带着自述里的「能力探测」命令：页面拿它做「重探」按钮与存完地址的自动重探。</summary>
        [Fact]
        public void HostRowCarriesProbeCommand()
        {
            WriteOnlineDriverWithModelField("faketalk");

            var row = Assert.Single(CreationPanelReader.ReadHostPackages(_repositoryRoot));

            Assert.Equal("bridge.probe --Driver faketalk", row.ProbeCommand);
        }

        /// <summary>序列化出来的键名就是 panel.js 读的那几个。</summary>
        [Fact]
        public void SerializedKeysMatchWhatThePageReads()
        {
            WriteOnlineDriverWithModelField("faketalk");

            var json = JsonSerializer.Serialize(
                CreationPanelReader.ReadHostPackages(_repositoryRoot),
                new JsonSerializerOptions(JsonSerializerOptions.Default)
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

            foreach (var key in new[] { "模型格", "自动说明", "探测" })
            {
                Assert.Contains("\"" + key + "\":", json, StringComparison.Ordinal);
            }
        }

        /// <summary>页面脚本真的读了这三个键，而不是后端白发一场。</summary>
        [Fact]
        public void PanelScriptReadsTheseKeys()
        {
            var script = File.ReadAllText(PanelScriptPath());

            foreach (var key in new[] { "模型格", "自动说明", "探测" })
            {
                Assert.Contains("'" + key + "'", script, StringComparison.Ordinal);
            }
        }

        /// <summary>页面脚本里的哨兵字面量必须与 C# 侧的 ModelSelection.AutoSentinel 一致。</summary>
        [Fact]
        public void PanelScriptSentinelMatchesBackend()
        {
            var script = File.ReadAllText(PanelScriptPath());

            Assert.Contains("var 自动值 = '" + Template.Toolkit.CreationPipeline.ModelSelection.AutoSentinel + "'", script, StringComparison.Ordinal);
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

        /// <summary>panel.js 在仓库里的路径：从测试程序集往上找到仓库根。</summary>
        private static string PanelScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tools", "Dashboard", "Web", "panel.js")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "Tools", "Dashboard", "Web", "panel.js");
        }

        private void WriteOnlineDriverWithModelField(string driverName)
        {
            var driverJson = """
                {
                  "名称": "%名%",
                  "port": ["执行后端"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "地址": { "类型": "string", "默认": "" },
                    "模型": { "类型": "string", "默认": "", "选项来源": "探测.模型" }
                  },
                  "密钥字段": [],
                  "试跑": "bridge.complete --Driver %名%",
                  "能力探测": "bridge.probe --Driver %名%",
                  "实现": "bridge-%名%",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """.Replace("%名%", driverName);
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", driverName, "driver.json"), driverJson);
        }

        private void WriteProbeResult(string driverName, string[] modelNames, string probedEndpoint)
        {
            var models = string.Join(",", modelNames.Select(name =>
                "{\"名\":" + JsonSerializer.Serialize(name) + ",\"版本\":\"\",\"hash\":\"\"}"));
            var json = "{\"节点\":[],\"模型\":[" + models + "],\"lora\":[],\"探于\":"
                + JsonSerializer.Serialize(probedEndpoint) + ",\"探测时间\":\"2026-08-20T01:02:03.0000000Z\"}";
            WriteFile(Path.Combine(_repositoryRoot, "_Generated", "Probes", driverName, "probe-result.json"), json);
        }

        private static void WriteFile(string filePath, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }
    }
}
