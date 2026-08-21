using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CreationPipeline;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板下游页读取器测试：全部用系统临时目录建仓库根，跑完自删。</summary>
    public sealed class CreationPanelBridgeTests : IDisposable
    {
        private readonly string _repositoryRoot;

        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelBridgeTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板下游读取器测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>Bridges/ 不存在时返回空列表，不抛。</summary>
        [Fact]
        public void MissingBridgesDirectoryReturnsEmptyWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));
        }

        /// <summary>两个 driver → 两行，按名称序数序。</summary>
        [Fact]
        public void TwoDriversProduceRowsSortedByName()
        {
            WriteDriver("bbb", LocalDriverJson("bbb"));
            WriteDriver("aaa", LocalDriverJson("aaa"));

            var rows = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);

            Assert.Equal(2, rows.Count);
            Assert.Equal("aaa", rows[0].DriverName);
            Assert.Equal("bbb", rows[1].DriverName);
        }

        /// <summary>driver.json 是坏 JSON → 该行仍在，LoadFailureReason 非空（决策 43）。</summary>
        [Fact]
        public void BrokenDriverJsonStillProducesRowWithLoadFailureReason()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteDriver("broken", """
                {
                  "名称": "broken",
                  "port": [
                """);

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            Assert.Equal("broken", row.DriverName);
            Assert.False(string.IsNullOrEmpty(row.LoadFailureReason));
        }

        /// <summary>配置 schema 里的字段类型、必填、enum 选项映射对得上；类型是 secret 的字段 IsSecret 为 true。</summary>
        [Fact]
        public void SchemaFieldsMapTypeRequiredAndEnumOptions()
        {
            WriteDriver("comfyui", RichDriverJson());

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            var addressField = Field(row, "地址");
            Assert.Equal("string", addressField.FieldType);
            Assert.False(addressField.IsRequired);

            var retryField = Field(row, "重试");
            Assert.Equal("number", retryField.FieldType);
            Assert.True(retryField.IsRequired);

            var enabledField = Field(row, "启用");
            Assert.Equal("bool", enabledField.FieldType);

            var modeField = Field(row, "模式");
            Assert.Equal("enum", modeField.FieldType);
            Assert.Equal(new[] { "快速", "精细" }, modeField.Options);

            var tokenField = Field(row, "令牌");
            Assert.Equal("secret", tokenField.FieldType);
            Assert.True(tokenField.IsSecret);
        }

        /// <summary>密钥字段点名的字段 IsSecret 为 true；local.json 不存在 → 未配，有那个键 → 已配；不在 schema 的密钥字段也产行。</summary>
        [Fact]
        public void SecretStateComesFromLocalConfigKeyPresence()
        {
            WriteDriver("comfyui", RichDriverJson());

            // local.json 不存在 → 全部密钥字段未配（正常，不是错误）。
            var rows = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);
            Assert.Equal("未配", Field(rows[0], "令牌").SecretState);

            // 「密钥字段」数组点名、但不在配置 schema 里的字段也产行，类型就是 secret。
            var extraSecretField = Field(rows[0], "额外密钥");
            Assert.True(extraSecretField.IsSecret);
            Assert.Equal("secret", extraSecretField.FieldType);
            Assert.Equal("未配", extraSecretField.SecretState);

            // local.json 里有那个键 → 已配；没有的键保持未配。
            WriteLocalConfig("""
                {
                  "令牌": "已配置的值"
                }
                """);
            var rowsAfter = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);
            Assert.Equal("已配", Field(rowsAfter[0], "令牌").SecretState);
            Assert.Equal("未配", Field(rowsAfter[0], "额外密钥").SecretState);
        }

        /// <summary>
        /// 密钥值不外泄（本批最硬的红线，决策 5）：local.json 里放 SUPERSECRETVALUE，
        /// 把整个 ReadBridges 的返回序列化成 JSON 字符串，断言这个字符串里不含密钥值——
        /// 密钥只许判键在不在，值一次都不许落进任何返回、日志或文案。
        /// </summary>
        [Fact]
        public void SecretValueNeverLeaksIntoSerializedOutput()
        {
            WriteDriver("comfyui", RichDriverJson());
            WriteLocalConfig("""
                {
                  "令牌": "SUPERSECRETVALUE",
                  "额外密钥": "OTHERSUPERSECRETVALUE"
                }
                """);

            var rows = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);
            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            Assert.DoesNotContain("SUPERSECRETVALUE", json);
            Assert.DoesNotContain("OTHERSUPERSECRETVALUE", json);
        }

        /// <summary>线上 driver 不做本地能力对账：CapabilityMeasured 为 false，原因写明（决策 42）。</summary>
        [Fact]
        public void OnlineDriverHasNoCapabilityReconcile()
        {
            WriteDriver("feishu", OnlineDriverJson());

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            Assert.Equal("线上", row.Shape);
            Assert.False(row.CapabilityMeasured);
            Assert.Equal(-1, row.DependencyCount);
            Assert.Equal(-1, row.SatisfiedCount);
            Assert.Contains(row.CapabilityNotes, note => note.Contains("线上 driver", StringComparison.Ordinal));
        }

        /// <summary>本地 driver 缺依赖清单 → 对账没跑成，两个计数都是 -1 而不是 0（0 会被读成「零个依赖全满足」）。</summary>
        [Fact]
        public void LocalDriverWithoutManifestIsNotMeasuredWithMinusOneCounts()
        {
            WriteDriver("comfyui", LocalDriverJson("comfyui"));
            // 刻意不写 Bridges/comfyui/dependencies.json。

            var row = Assert.Single(CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot));

            Assert.False(row.CapabilityMeasured);
            Assert.Equal(-1, row.DependencyCount);
            Assert.Equal(-1, row.SatisfiedCount);
            Assert.NotEmpty(row.CapabilityNotes);
        }

        /// <summary>IsProvisioned 复用供给指纹判据：_Generated/Bridges/&lt;名&gt;/ 下有指纹文件即为 true。</summary>
        [Fact]
        public void ProvisionedFlagReflectsFingerprintFileExistence()
        {
            WriteDriver("withfp", LocalDriverJson("withfp"));
            WriteDriver("withoutfp", LocalDriverJson("withoutfp"));
            WriteFile(ProvisionPaths.FingerprintFile(_repositoryRoot, "withfp"), "{}");

            var rows = CreationPanelReader.ReadBridges(_repositoryRoot, _poolRoot);

            Assert.True(rows[0].IsProvisioned);
            Assert.False(rows[1].IsProvisioned);
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
            WriteFile(Path.Combine(_repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json"), json);
        }

        private static string LocalDriverJson(string driverName)
        {
            return $$"""
                {
                  "名称": "{{driverName}}",
                  "port": ["生图"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {},
                  "密钥字段": [],
                  "试跑": "art.caps --driver {{driverName}}",
                  "能力探测": "{{driverName}}-caps",
                  "实现": "bridge-{{driverName}}",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """;
        }

        private static string OnlineDriverJson()
        {
            return """
                {
                  "名称": "feishu",
                  "port": ["需求编辑端"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {},
                  "密钥字段": [],
                  "试跑": "",
                  "能力探测": "",
                  "实现": "bridge-feishu",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """;
        }

        private static string RichDriverJson()
        {
            return """
                {
                  "名称": "comfyui",
                  "port": ["生图"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "地址": { "类型": "string" },
                    "重试": { "类型": "number", "必填": true },
                    "启用": { "类型": "bool" },
                    "模式": { "类型": "enum", "选项": ["快速", "精细"] },
                    "令牌": { "类型": "secret" }
                  },
                  "密钥字段": ["令牌", "额外密钥"],
                  "试跑": "art.caps --driver comfyui",
                  "能力探测": "comfyui-caps",
                  "实现": "bridge-comfyui",
                  "字段类型映射": {},
                  "表单分组字段": ""
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
