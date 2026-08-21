using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipelineTests
{
    /// <summary>安装检查器测试：骨架生成剥密钥、缺配置的红项、driver 密钥键判定，全用合成目录。</summary>
    public class SetupInspectorTests : IDisposable
    {
        private readonly string _root;

        /// <summary>建一棵合成仓库树。</summary>
        public SetupInspectorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "SetupInspectorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>清掉合成树。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>骨架生成：从样例复制，剥掉 driver 自述声明的密钥键，非密钥内容原样保留。</summary>
        [Fact]
        public void InitializeStripsSecretKeysFromExample()
        {
            WriteDriver("sample", secretFieldNames: new[] { "样例密钥" });
            var configDirectory = Path.Combine(_root, "Tools", "CreationPipeline", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "local.example.json"), """
                {
                  "样例密钥": "placeholder",
                  "下游配置": { "sample": { "地址": "http://127.0.0.1:1234" } }
                }
                """);

            var message = SetupInspector.InitializeLocalSettings(_root);

            Assert.Contains("样例密钥", message);
            var written = JsonNode.Parse(File.ReadAllText(Path.Combine(configDirectory, "local.json"))) as JsonObject;
            Assert.False(written.ContainsKey("样例密钥"), "密钥键没被剥掉——带空值的密钥键就是假绿");
            Assert.NotNull(written["下游配置"]?["sample"]?["地址"]);
        }

        /// <summary>骨架生成：local.json 已存在时不动它。</summary>
        [Fact]
        public void InitializeDoesNotOverwriteExistingSettings()
        {
            var configDirectory = Path.Combine(_root, "Tools", "CreationPipeline", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "local.json"), "{ \"占位\": 1 }");
            File.WriteAllText(Path.Combine(configDirectory, "local.example.json"), "{}");

            var message = SetupInspector.InitializeLocalSettings(_root);

            Assert.Contains("没动", message);
            Assert.Contains("占位", File.ReadAllText(Path.Combine(configDirectory, "local.json")));
        }

        /// <summary>体检：local.json 缺失报红，且下一步点名 setup.init。</summary>
        [Fact]
        public void InspectReportsMissingLocalSettingsAsRed()
        {
            var findings = SetupInspector.Inspect(_root);

            Assert.Contains(findings, finding =>
                finding.Severity == "红" && finding.Item == "本机配置" && finding.NextStep.Contains("setup.init"));
        }

        /// <summary>体检：driver 声明的密钥键不在 local.json 顶层时报红并点名缺的键。</summary>
        [Fact]
        public void InspectReportsMissingDriverSecretsAsRed()
        {
            WriteDriver("sample", secretFieldNames: new[] { "样例密钥" });
            var configDirectory = Path.Combine(_root, "Tools", "CreationPipeline", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "local.json"), """
                { "下游配置": { "sample": { "地址": "http://127.0.0.1:1234" } } }
                """);

            var findings = SetupInspector.Inspect(_root);

            var secretFinding = findings.Single(finding => finding.Item == "下游 sample" && finding.Detail.Contains("密钥键缺"));
            Assert.Equal("红", secretFinding.Severity);
            Assert.Contains("样例密钥", secretFinding.Detail);
        }

        /// <summary>体检：密钥键在、配置节在、指纹在 → 该 driver 报绿。</summary>
        [Fact]
        public void InspectReportsReadyDriverAsGreen()
        {
            WriteDriver("sample", secretFieldNames: new[] { "样例密钥" });
            var configDirectory = Path.Combine(_root, "Tools", "CreationPipeline", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "local.json"), """
                { "样例密钥": "x", "下游配置": { "sample": { "地址": "http://127.0.0.1:1234" } } }
                """);
            var fingerprintDirectory = Path.Combine(_root, "_Generated", "Bridges", "sample");
            Directory.CreateDirectory(fingerprintDirectory);
            File.WriteAllText(Path.Combine(fingerprintDirectory, "fingerprint.json"), "{}");

            var findings = SetupInspector.Inspect(_root);

            Assert.Contains(findings, finding => finding.Item == "下游 sample" && finding.Severity == "绿");
        }

        private void WriteDriver(string name, string[] secretFieldNames)
        {
            var driverDirectory = Path.Combine(_root, "Bridges", name);
            Directory.CreateDirectory(driverDirectory);
            var secretList = string.Join(",", secretFieldNames.Select(field => "\"" + field + "\""));
            File.WriteAllText(Path.Combine(driverDirectory, "driver.json"), $$"""
                {
                  "名称": "{{name}}",
                  "port": ["样例端口"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "地址": { "类型": "string", "默认": "" } },
                  "密钥字段": [{{secretList}}],
                  "试跑": "",
                  "能力探测": "",
                  "实现": "bridge-{{name}}",
                  "字段类型映射": {},
                  "表单分组字段": ""
                }
                """);
        }
    }
}
