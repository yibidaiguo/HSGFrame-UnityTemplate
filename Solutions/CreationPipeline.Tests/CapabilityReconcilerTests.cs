using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>依赖清单加载、能力探测输出加载与能力对账逻辑的测试。</summary>
    public class CapabilityReconcilerTests
    {
        private const string ManifestJson = """
            {
              "契约版本": "1.0.0",
              "依赖": [
                {
                  "名称": "ComfyUI-Impact-Pack",
                  "类别": "节点",
                  "版本": "8.0.0",
                  "来源": "https://github.com/ltdrdata/ComfyUI-Impact-Pack",
                  "安装命令": "git clone https://github.com/ltdrdata/ComfyUI-Impact-Pack custom_nodes/ComfyUI-Impact-Pack",
                  "说明": "透明底裁切"
                },
                {
                  "名称": "sd_xl_base_1.0.safetensors",
                  "类别": "模型",
                  "版本": "1.0",
                  "来源": "https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0",
                  "安装命令": "",
                  "说明": "底模"
                }
              ]
            }
            """;

        private const string ProbeJson = """
            {
              "节点": [{ "名": "ComfyUI-Impact-Pack", "版本": "8.0.0" }],
              "模型": [{ "名": "sd_xl_base_1.0.safetensors", "hash": "abc123" }],
              "lora": []
            }
            """;

        /// <summary>依赖清单正常加载：条目按名称序数序排序。</summary>
        [Fact]
        public void ManifestLoadValidSortsByName()
        {
            using var workspace = new Workspace();
            WriteManifest(workspace.Root, ManifestJson);

            var manifest = DependencyManifest.Load(workspace.Root, "comfyui");

            Assert.Equal("1.0.0", manifest.ContractVersion);
            Assert.Equal(2, manifest.Entries.Count);
            Assert.Equal(new[] { "ComfyUI-Impact-Pack", "sd_xl_base_1.0.safetensors" }, manifest.Entries.Select(entry => entry.Name));
            Assert.True(manifest.TryFind("sd_xl_base_1.0.safetensors", out var found));
            Assert.Equal("模型", found.Category);
        }

        /// <summary>依赖清单文件不存在时 Load 抛 InvalidOperationException。</summary>
        [Fact]
        public void ManifestLoadMissingThrows()
        {
            using var workspace = new Workspace();

            Assert.Throws<InvalidOperationException>(() => DependencyManifest.Load(workspace.Root, "comfyui"));
        }

        /// <summary>类别不是 节点 / 模型 / lora 时 Load 抛，文案说清三个合法值。</summary>
        [Fact]
        public void ManifestLoadInvalidCategoryThrows()
        {
            using var workspace = new Workspace();
            var brokenManifest = ManifestJson.Replace("\"类别\": \"节点\"", "\"类别\": \"插件\"");
            WriteManifest(workspace.Root, brokenManifest);

            var exception = Assert.Throws<InvalidOperationException>(() => DependencyManifest.Load(workspace.Root, "comfyui"));

            Assert.Contains("ComfyUI-Impact-Pack", exception.Message);
            Assert.Contains("插件", exception.Message);
            Assert.Contains("节点", exception.Message);
            Assert.Contains("模型", exception.Message);
            Assert.Contains("lora", exception.Message);
        }

        /// <summary>探测输出正常加载：三类能力齐全。</summary>
        [Fact]
        public void ProbeLoadValidReadsThreeCategories()
        {
            var probePath = Path.Combine(Path.GetTempPath(), "probe-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(probePath, ProbeJson, new UTF8Encoding(false));
            try
            {
                var result = CapabilityProbeResult.LoadFromFile(probePath);

                Assert.Single(result.Nodes);
                Assert.Single(result.Models);
                Assert.Empty(result.Loras);
                Assert.Equal("abc123", result.Models[0].Hash);
            }
            finally
            {
                File.Delete(probePath);
            }
        }

        /// <summary>缺 lora 顶层键时当空列表处理，不抛。</summary>
        [Fact]
        public void ProbeLoadMissingLoraKeyIsEmpty()
        {
            var probePath = Path.Combine(Path.GetTempPath(), "probe-" + Guid.NewGuid().ToString("N") + ".json");
            var noLoraProbe = ProbeJson.Replace(",\n              \"lora\": []", "");
            File.WriteAllText(probePath, noLoraProbe, new UTF8Encoding(false));
            try
            {
                var result = CapabilityProbeResult.LoadFromFile(probePath);

                Assert.Single(result.Nodes);
                Assert.Empty(result.Loras);
            }
            finally
            {
                File.Delete(probePath);
            }
        }

        /// <summary>探测输出文件不存在时 LoadFromFile 抛 InvalidOperationException。</summary>
        [Fact]
        public void ProbeLoadMissingFileThrows()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), "probe-" + Guid.NewGuid().ToString("N") + ".json");

            Assert.Throws<InvalidOperationException>(() => CapabilityProbeResult.LoadFromFile(missingPath));
        }

        /// <summary>Contains 对未知类别返回 false。</summary>
        [Fact]
        public void ProbeContainsUnknownCategoryReturnsFalse()
        {
            var result = new CapabilityProbeResult(
                new[] { new CapabilityItem("ComfyUI-Impact-Pack", "8.0.0", "") },
                Array.Empty<CapabilityItem>(),
                Array.Empty<CapabilityItem>());

            Assert.False(result.Contains("未知类别", "ComfyUI-Impact-Pack"));
        }

        /// <summary>全满足时对账零 finding，满足数等于依赖数。</summary>
        [Fact]
        public void ReconcileAllSatisfiedHasNoFindings()
        {
            var manifest = LoadManifest(ManifestJson);
            var probe = LoadProbe(ProbeJson);

            var report = CapabilityReconciler.Reconcile("comfyui", manifest, probe);

            Assert.Empty(report.Findings);
            Assert.Equal(2, report.DependencyCount);
            Assert.Equal(2, report.SatisfiedCount);
        }

        /// <summary>缺一项时 finding 文案同时出现来源 URL 与安装命令。</summary>
        [Fact]
        public void ReconcileMissingDependencyReportsSourceAndInstallCommand()
        {
            var manifest = LoadManifest(ManifestJson);
            var probe = LoadProbe(
                """
                { "节点": [], "模型": [], "lora": [] }
                """);

            var report = CapabilityReconciler.Reconcile("comfyui", manifest, probe);

            Assert.Equal(2, report.Findings.Count);
            Assert.Equal(0, report.SatisfiedCount);
            var firstFinding = report.Findings[0];
            Assert.Contains("缺依赖「ComfyUI-Impact-Pack」（类别：节点）", firstFinding.Reason);
            Assert.Contains("https://github.com/ltdrdata/ComfyUI-Impact-Pack", firstFinding.ToDisplayText());
            Assert.Contains("git clone https://github.com/ltdrdata/ComfyUI-Impact-Pack", firstFinding.ToDisplayText());
        }

        /// <summary>安装命令为空时 finding 文案给出兜底句。</summary>
        [Fact]
        public void ReconcileMissingDependencyWithoutInstallCommandGivesHint()
        {
            var manifest = LoadManifest(ManifestJson);
            var probe = LoadProbe(
                """
                { "节点": [{ "名": "ComfyUI-Impact-Pack", "版本": "8.0.0" }], "模型": [], "lora": [] }
                """);

            var report = CapabilityReconciler.Reconcile("comfyui", manifest, probe);

            var finding = Assert.Single(report.Findings);
            Assert.Contains("sd_xl_base_1.0.safetensors", finding.Reason);
            Assert.Contains("清单没给安装命令，照来源页面自行安装", finding.ToDisplayText());
        }

        private static DependencyManifest LoadManifest(string json)
        {
            using var workspace = new Workspace();
            WriteManifest(workspace.Root, json);
            return DependencyManifest.Load(workspace.Root, "comfyui");
        }

        private static CapabilityProbeResult LoadProbe(string json)
        {
            var probePath = Path.Combine(Path.GetTempPath(), "probe-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(probePath, json, new UTF8Encoding(false));
            try
            {
                return CapabilityProbeResult.LoadFromFile(probePath);
            }
            finally
            {
                File.Delete(probePath);
            }
        }

        private static void WriteManifest(string root, string json)
        {
            var path = RecipePaths.DependencyManifestFile(root, "comfyui");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "对账测试-" + Guid.NewGuid().ToString("N"));
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
