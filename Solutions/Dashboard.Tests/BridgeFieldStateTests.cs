using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CreationPipeline;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>
    /// 下游页字段「配没配」状态测试（P8-8）：
    /// 非密钥字段读本机配置的「下游配置.&lt;driver&gt;.&lt;字段名&gt;」，判「键在不在且非空串」；
    /// 密钥字段只判键在不在、值一次都不取（决策 5、78）；
    /// 「本机.json 不存在」与「文件有但这项没填」是两支，必须分开报（决策 42、77）。
    /// 全部用系统临时目录建仓库根，跑完自删。
    /// </summary>
    public sealed class BridgeFieldStateTests : IDisposable
    {
        private readonly string _repositoryRoot;

        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public BridgeFieldStateTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板字段状态测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>非密钥字段：本机配置「下游配置.&lt;driver&gt;.&lt;字段名&gt;」有值 → 已配（字符串与数字都算）。</summary>
        [Fact]
        public void NonSecretFieldWithValueIsConfigured()
        {
            WriteDriver("blender", BlenderDriverJson());
            WriteLocalConfig("""
                {
                  "下游配置": {
                    "blender": {
                      "可执行文件": "D:/Tools/Blender/blender.exe",
                      "超时秒": 900
                    }
                  }
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            Assert.Equal("已配", Field(row, "可执行文件").State);
            Assert.Equal("已配", Field(row, "超时秒").State);
        }

        /// <summary>非密钥字段：值是空串 → 未配；键缺失 → 未配。</summary>
        [Fact]
        public void NonSecretFieldEmptyOrMissingIsNotConfigured()
        {
            WriteDriver("blender", BlenderDriverJson());
            WriteLocalConfig("""
                {
                  "下游配置": {
                    "blender": {
                      "可执行文件": ""
                    }
                  }
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            // 空串 → 未配。
            Assert.Equal("未配", Field(row, "可执行文件").State);
            // 键缺失 → 未配。
            Assert.Equal("未配", Field(row, "超时秒").State);
        }

        /// <summary>本机.json 不存在 → 密钥与非密钥字段全部未配，且行上有「本机配置文件不存在」的说明（决策 42、77，本批最重要的一条）。</summary>
        [Fact]
        public void MissingLocalConfigMarksAllFieldsUnconfiguredWithNote()
        {
            WriteDriver("feishu", FeishuDriverJson());
            // 刻意不写 Config/创作管线/本机.json。

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            // 非密钥字段与密钥字段全部未配。
            Assert.Equal("未配", Field(row, "应用标识").State);
            Assert.Equal("未配", Field(row, "多维表格标识").State);
            Assert.Equal("未配", Field(row, "超时秒").State);
            Assert.Equal("未配", Field(row, "飞书应用密钥").State);
            // 「文件不存在」单独说，不合并成「没填」。
            Assert.Contains("本机配置文件不存在", row.LocalConfigNote, StringComparison.Ordinal);
        }

        /// <summary>密钥字段的值（可识别的假值）不出现在任何返回字段里；密钥字段只判键在不在（决策 5、78）。</summary>
        [Fact]
        public void SecretValueNeverAppearsInAnyReturnedField()
        {
            WriteDriver("feishu", FeishuDriverJson());
            WriteLocalConfig("""
                {
                  "下游配置": {
                    "feishu": {
                      "应用标识": "app-id-value",
                      "多维表格标识": "table-id-value",
                      "超时秒": 60
                    }
                  },
                  "飞书应用密钥": "SUPERSECRETFEISHUKEY"
                }
                """);

            var rows = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);
            var row = Assert.Single(rows);

            // 密钥字段只判键在不在：顶层有那个键 → 已配。
            Assert.Equal("已配", Field(row, "飞书应用密钥").State);

            // 把整个返回序列化成 JSON，断言密钥假值不在其中。
            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            Assert.DoesNotContain("SUPERSECRETFEISHUKEY", json);
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
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
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        private static PanelBridgeFieldRow Field(PanelBridgeRow row, string name)
        {
            return Assert.Single(row.Fields, field => string.Equals(field.Name, name, StringComparison.Ordinal));
        }

        private void WriteDriver(string driverName, string json)
        {
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", driverName, "driver.json"), json);
        }

        private void WriteLocalConfig(string json)
        {
            WriteFile(Path.Combine(_repositoryRoot, "Config", "创作管线", "本机.json"), json);
        }

        private static string BlenderDriverJson()
        {
            return """
                {
                  "名称": "blender",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "可执行文件": { "类型": "string", "默认": "" },
                    "超时秒": { "类型": "number", "默认": 900 }
                  },
                  "密钥字段": [],
                  "试跑": "art.caps --driver blender",
                  "能力探测": "bridge.probe --Driver blender",
                  "实现": "bridge-blender",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """;
        }

        private static string FeishuDriverJson()
        {
            return """
                {
                  "名称": "feishu",
                  "port": ["需求编辑端", "消息卡片", "助手"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "应用标识": { "类型": "string", "默认": "" },
                    "多维表格标识": { "类型": "string", "默认": "" },
                    "超时秒": { "类型": "number", "默认": 60 }
                  },
                  "密钥字段": ["飞书应用密钥"],
                  "试跑": "bridge.provision --driver feishu --dry-run",
                  "能力探测": "",
                  "实现": "bridge-feishu",
                  "字段类型映射": {},
                  "表单分组字段": "类型"
                }
                """;
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        }
    }
}
