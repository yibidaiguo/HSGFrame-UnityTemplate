using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 配置写入的测试：本机配置字段、密钥键、插件声明三条写路。
    ///
    /// 这一族守三件事，每一件都是「写坏了要人命」的那种：
    /// 1. **不覆盖人写的东西**——JSON 坏掉时拒绝写，文件一个字节都不许变；顶层其余键（尤其是密钥）原样留着。
    /// 2. **不写不该写的**——凭空造的字段名、没声明过的密钥键，一律拒绝。
    /// 3. **密钥值不外流**——写密钥的返回文案里只有键名，值一个字符都不许出现（决策 5、78 的读侧）。
    /// </summary>
    public class ConfigWriterTests
    {
        private const string DriverJson = """
            {
              "名称": "demo",
              "port": ["模型生成"],
              "形态": "线上",
              "契约版本": ">=1.0 <2.0",
              "配置schema": {
                "地址": { "类型": "string", "默认": "" },
                "超时秒": { "类型": "number", "默认": 60 }
              },
              "密钥字段": ["演示密钥"],
              "试跑": "",
              "能力探测": "",
              "实现": "bridge-demo",
              "字段类型映射": {},
              "表单分组字段": ""
            }
            """;

        /// <summary>本机配置文件还不存在时，写一个字段会把文件建出来，值落在 下游配置.&lt;driver&gt; 下。</summary>
        [Fact]
        public void SetDriverFieldCreatesTheFileWhenItIsMissing()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            var outcome = LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "地址", "http://127.0.0.1:9000");

            Assert.True(outcome.Succeeded, outcome.Message);
            Assert.Equal("http://127.0.0.1:9000", ReadJsonString(workspace.Root, "下游配置", "demo", "地址"));
        }

        /// <summary>写一个字段不动文件里的别的东西：别的 driver 的配置与顶层密钥原样留着。</summary>
        [Fact]
        public void SetDriverFieldKeepsEverythingElseIncludingSecrets()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            WriteLocalSettings(workspace.Root, """
                {
                  "演示密钥": "原来的密钥值",
                  "下游配置": {
                    "别的driver": { "地址": "别动我" },
                    "demo": { "地址": "http://old" }
                  }
                }
                """);

            LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "地址", "http://new");

            Assert.Equal("原来的密钥值", ReadJsonString(workspace.Root, "演示密钥"));
            Assert.Equal("别动我", ReadJsonString(workspace.Root, "下游配置", "别的driver", "地址"));
            Assert.Equal("http://new", ReadJsonString(workspace.Root, "下游配置", "demo", "地址"));
        }

        /// <summary>自述里是 number 的字段写成 JSON 数字，不是带引号的字符串。</summary>
        [Fact]
        public void NumberFieldIsWrittenAsANumber()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "超时秒", "180");

            using var document = JsonDocument.Parse(File.ReadAllText(LocalSettingsWriter.LocalSettingsFile(workspace.Root)));
            var value = document.RootElement.GetProperty("下游配置").GetProperty("demo").GetProperty("超时秒");
            Assert.Equal(JsonValueKind.Number, value.ValueKind);
            Assert.Equal(180, value.GetDouble());
        }

        /// <summary>number 字段填了个不是数字的东西：拒绝，并把两样都说清楚。</summary>
        [Fact]
        public void NumberFieldRejectsNonNumericValue()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            var outcome = LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "超时秒", "很久");

            Assert.False(outcome.Succeeded);
            Assert.Contains("number", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>值传空串是「删掉这个键」，不是「写一个空串」——空串会被判成「已配」，那是假绿。</summary>
        [Fact]
        public void EmptyValueRemovesTheKeyInsteadOfWritingBlank()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "地址", "http://x");

            LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "地址", "");

            using var document = JsonDocument.Parse(File.ReadAllText(LocalSettingsWriter.LocalSettingsFile(workspace.Root)));
            var driverSection = document.RootElement.GetProperty("下游配置").GetProperty("demo");
            Assert.False(driverSection.TryGetProperty("地址", out _));
        }

        /// <summary>密钥字段不走「下游配置」这条路：拒绝，并指到写密钥的那条命令上。</summary>
        [Fact]
        public void SecretFieldIsNotWrittenThroughTheDriverSection()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            var outcome = LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "演示密钥", "值");

            Assert.False(outcome.Succeeded);
            Assert.Contains("bridge.secret.set", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>自述里没声明过的字段名一律拒绝：写进去没人读，页面还会把它显示成「已配」。</summary>
        [Fact]
        public void UndeclaredFieldIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            var outcome = LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "我瞎编的字段", "值");

            Assert.False(outcome.Succeeded);
            Assert.Contains("地址", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// 本机配置是坏 JSON：拒绝写，而且**文件一个字节都不许变**。
        /// 拿一份干净骨架盖掉人填了一半的文件，等于把密钥连同别的配置一起抹了。
        /// </summary>
        [Fact]
        public void BrokenLocalSettingsAreNeverOverwritten()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            var broken = "{ \"演示密钥\": \"值\", ";
            WriteLocalSettings(workspace.Root, broken);

            var outcome = LocalSettingsWriter.SetDriverField(workspace.Root, "demo", "地址", "http://x");

            Assert.False(outcome.Succeeded);
            Assert.Equal(broken, File.ReadAllText(LocalSettingsWriter.LocalSettingsFile(workspace.Root)));
        }

        /// <summary>
        /// 写密钥：值落进文件顶层，而返回文案里**只有键名**——
        /// 文案会被面板显示、会进命令输出区，把值拼进去它就跟着截图和日志到处跑。
        /// </summary>
        [Fact]
        public void SecretIsWrittenToTheTopLevelAndNeverEchoed()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            const string secretValue = "绝不该出现在文案里的值";

            var outcome = LocalSettingsWriter.SetSecret(workspace.Root, "演示密钥", secretValue);

            Assert.True(outcome.Succeeded, outcome.Message);
            Assert.Equal(secretValue, ReadJsonString(workspace.Root, "演示密钥"));
            Assert.DoesNotContain(secretValue, outcome.Message, StringComparison.Ordinal);
            Assert.Contains("演示密钥", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>写密钥不动文件里的别的东西。</summary>
        [Fact]
        public void SecretWriteKeepsTheRestOfTheFile()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            WriteLocalSettings(workspace.Root, """{ "下游配置": { "demo": { "地址": "http://old" } } }""");

            LocalSettingsWriter.SetSecret(workspace.Root, "演示密钥", "值");

            Assert.Equal("http://old", ReadJsonString(workspace.Root, "下游配置", "demo", "地址"));
        }

        /// <summary>没有任何 driver 声明过的密钥键名一律拒绝：写错一个字母，密钥会躺在文件里永远没人读。</summary>
        [Fact]
        public void UndeclaredSecretKeyIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);

            var outcome = LocalSettingsWriter.SetSecret(workspace.Root, "我瞎编的密钥", "值");

            Assert.False(outcome.Succeeded);
            Assert.Contains("演示密钥", outcome.Message, StringComparison.Ordinal);
        }

        /// <summary>密钥传空串是删掉这个键。</summary>
        [Fact]
        public void EmptySecretValueRemovesTheKey()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root);
            LocalSettingsWriter.SetSecret(workspace.Root, "演示密钥", "值");

            LocalSettingsWriter.SetSecret(workspace.Root, "演示密钥", "");

            using var document = JsonDocument.Parse(File.ReadAllText(LocalSettingsWriter.LocalSettingsFile(workspace.Root)));
            Assert.False(document.RootElement.TryGetProperty("演示密钥", out _));
        }

        /// <summary>插件声明清单还不存在时，加一条会把文件连同契约版本一起建出来。</summary>
        [Fact]
        public void PluginUpsertCreatesTheManifest()
        {
            using var workspace = new Workspace();

            var outcome = EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", "路径甲"));

            Assert.True(outcome.Succeeded, outcome.Message);
            var manifest = EditorPluginManifest.Load(workspace.Root);
            Assert.True(manifest.Loaded);
            var entry = Assert.Single(manifest.Entries);
            Assert.Equal("插件甲", entry.Name);
            Assert.Equal("路径甲", entry.MarkerPath);
        }

        /// <summary>同宿主同名再存一次是「改」：条目数不变，内容换成新的。</summary>
        [Fact]
        public void PluginUpsertReplacesTheSameHostAndName()
        {
            using var workspace = new Workspace();
            EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", "旧路径"));

            var outcome = EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", "新路径"));

            Assert.True(outcome.Succeeded, outcome.Message);
            Assert.Contains("已改", outcome.Message, StringComparison.Ordinal);
            var entry = Assert.Single(EditorPluginManifest.Load(workspace.Root).Entries);
            Assert.Equal("新路径", entry.MarkerPath);
        }

        /// <summary>同名但宿主不同是两条，不是一条——同一个插件装进两个宿主是两件事。</summary>
        [Fact]
        public void SameNameOnAnotherHostIsASeparateEntry()
        {
            using var workspace = new Workspace();
            EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", "路径甲"));

            EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "blender", "路径乙"));

            Assert.Equal(2, EditorPluginManifest.Load(workspace.Root).Entries.Count);
        }

        /// <summary>写回时顶层的说明字段原样保留，条目按 (宿主, 名称) 排序——免得每改一条 git diff 整篇翻个个儿。</summary>
        [Fact]
        public void PluginWriteKeepsTopLevelKeysAndSortsEntries()
        {
            using var workspace = new Workspace();
            WriteManifest(workspace.Root, """{ "_说明": "别删我", "契约版本": "1.0.0", "插件": [] }""");

            EditorPluginWriter.Upsert(workspace.Root, Entry("乙插件", "unity", ""));
            EditorPluginWriter.Upsert(workspace.Root, Entry("甲插件", "blender", ""));

            using var document = JsonDocument.Parse(File.ReadAllText(EditorPluginManifest.ManifestFile(workspace.Root)));
            Assert.Equal("别删我", document.RootElement.GetProperty("_说明").GetString());
            var names = EditorPluginManifest.Load(workspace.Root).Entries.Select(entry => entry.HostName + "/" + entry.Name);
            Assert.Equal(new[] { "blender/甲插件", "unity/乙插件" }, names);
        }

        /// <summary>删一条：删掉了就不在清单里；删不存在的那条是失败，不是静默成功。</summary>
        [Fact]
        public void PluginRemoveDropsTheEntryAndReportsMissingOnes()
        {
            using var workspace = new Workspace();
            EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", ""));

            Assert.True(EditorPluginWriter.Remove(workspace.Root, "unity", "插件甲").Succeeded);
            Assert.Empty(EditorPluginManifest.Load(workspace.Root).Entries);

            var second = EditorPluginWriter.Remove(workspace.Root, "unity", "插件甲");
            Assert.False(second.Succeeded);
        }

        /// <summary>插件声明清单是坏 JSON：拒绝写，文件一个字节都不许变。</summary>
        [Fact]
        public void BrokenPluginManifestIsNeverOverwritten()
        {
            using var workspace = new Workspace();
            var broken = "{ \"插件\": [ ";
            WriteManifest(workspace.Root, broken);

            var outcome = EditorPluginWriter.Upsert(workspace.Root, Entry("插件甲", "unity", ""));

            Assert.False(outcome.Succeeded);
            Assert.Equal(broken, File.ReadAllText(EditorPluginManifest.ManifestFile(workspace.Root)));
        }

        private static EditorPluginEntry Entry(string name, string hostName, string markerPath)
        {
            return new EditorPluginEntry(name, hostName, markerPath, "1.0", "https://example.invalid", "装它", "测试用");
        }

        private static void WriteDriver(string repositoryRoot)
        {
            WriteFile(Path.Combine(repositoryRoot, "Bridges", "demo", "driver.json"), DriverJson);
        }

        private static void WriteLocalSettings(string repositoryRoot, string json)
        {
            WriteFile(LocalSettingsWriter.LocalSettingsFile(repositoryRoot), json);
        }

        private static void WriteManifest(string repositoryRoot, string json)
        {
            WriteFile(EditorPluginManifest.ManifestFile(repositoryRoot), json);
        }

        /// <summary>按一串键名往下读一个字符串值；路上任何一段缺失都直接失败断言。</summary>
        private static string ReadJsonString(string repositoryRoot, params string[] path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(LocalSettingsWriter.LocalSettingsFile(repositoryRoot)));
            var element = document.RootElement;
            foreach (var key in path)
            {
                Assert.True(element.TryGetProperty(key, out element), "读不到键：" + key);
            }

            return element.GetString();
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
                Root = Path.Combine(Path.GetTempPath(), "配置写入测试-" + Guid.NewGuid().ToString("N"));
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
