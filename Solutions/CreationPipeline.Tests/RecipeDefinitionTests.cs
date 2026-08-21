using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>配方加载与配方门禁判定逻辑的测试：DiscoverNames、Load 与 RecipeInspector 逐类问题。</summary>
    public class RecipeDefinitionTests
    {
        private const string WorkflowJson = """
            {
              "1": { "类型": "CheckpointLoaderSimple", "参数": { "ckpt_name": "sd_xl_base_1.0.safetensors" } },
              "2": { "类型": "CLIPTextEncode", "参数": { "text": "" } },
              "3": { "类型": "EmptyLatentImage", "参数": { "width": 256, "height": 256, "batch_size": 1 } },
              "4": { "类型": "SaveImage", "参数": { "filename_prefix": "" } },
              "5": { "类型": "LoadImage", "参数": { "image": "" } }
            }
            """;

        private const string MappingJson = """
            {
              "契约版本": "1.0.0",
              "配方名": "图标@v5",
              "资产类型": "图标",
              "映射": [
                { "请求字段": "描述", "节点id": "2", "参数名": "text" },
                { "请求字段": "规格.宽", "节点id": "3", "参数名": "width" },
                { "请求字段": "变体数", "节点id": "3", "参数名": "batch_size" },
                { "请求字段": "命名", "节点id": "4", "参数名": "filename_prefix" }
              ],
              "锚点槽": [
                { "槽名": "参考图", "节点id": "5", "参数名": "image" }
              ],
              "依赖": ["ComfyUI-Impact-Pack", "sd_xl_base_1.0.safetensors"]
            }
            """;

        private const string DependencyManifestJson = """
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

        /// <summary>配方根目录不存在时 DiscoverNames 返回空列表、不抛异常。</summary>
        [Fact]
        public void DiscoverNamesWithMissingRootReturnsEmpty()
        {
            using var workspace = new Workspace();

            var recipeNames = RecipeDefinition.DiscoverNames(workspace.Root, "comfyui");

            Assert.Empty(recipeNames);
        }

        /// <summary>有配方时 DiscoverNames 返回序数序。</summary>
        [Fact]
        public void DiscoverNamesReturnsOrdinalOrder()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "AlphaRecipe", WorkflowJson, MappingJson);
            WriteRecipe(workspace.Root, "BetaRecipe", WorkflowJson, MappingJson);

            var recipeNames = RecipeDefinition.DiscoverNames(workspace.Root, "comfyui");

            Assert.Equal(new[] { "AlphaRecipe", "BetaRecipe" }, recipeNames);
        }

        /// <summary>Load 正常路径：配方名、资产类型与节点集合齐全。</summary>
        [Fact]
        public void LoadValidRecipeReturnsDefinition()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, MappingJson);

            var recipe = RecipeDefinition.Load(workspace.Root, "comfyui", "图标@v5");

            Assert.Equal("图标@v5", recipe.Name);
            Assert.Equal("图标", recipe.AssetType);
            Assert.Equal("1.0.0", recipe.ContractVersion);
            Assert.Equal(new[] { "1", "2", "3", "4", "5" }, recipe.WorkflowNodeIdentifiers);
            Assert.Equal(4, recipe.MappingEntries.Count);
            Assert.Single(recipe.AnchorSlots);
            Assert.Equal(2, recipe.DependencyNames.Count);
            Assert.Equal("描述", recipe.MappingEntries[0].RequestField);
            Assert.Equal("参考图", recipe.AnchorSlots[0].SlotName);
        }

        /// <summary>workflow.json 缺失时 Load 抛 InvalidOperationException，文案带绝对路径。</summary>
        [Fact]
        public void LoadMissingWorkflowThrows()
        {
            using var workspace = new Workspace();
            WriteFile(RecipePaths.MappingFile(workspace.Root, "comfyui", "图标@v5"), MappingJson);

            var exception = Assert.Throws<InvalidOperationException>(
                () => RecipeDefinition.Load(workspace.Root, "comfyui", "图标@v5"));

            Assert.Contains(RecipePaths.WorkflowFile(workspace.Root, "comfyui", "图标@v5"), exception.Message);
        }

        /// <summary>mapping.json 是坏 JSON 时 Load 抛 InvalidOperationException。</summary>
        [Fact]
        public void LoadBrokenMappingThrows()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, "not-json");

            Assert.Throws<InvalidOperationException>(
                () => RecipeDefinition.Load(workspace.Root, "comfyui", "图标@v5"));
        }

        /// <summary>全合法的配方：RecipeInspector 零 finding。</summary>
        [Fact]
        public void InspectorValidRecipeHasNoFindings()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, MappingJson);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), DependencyManifestJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            Assert.Empty(findings);
        }

        /// <summary>workflow.json 缺失时 RecipeInspector 报 1 条，不抛异常。</summary>
        [Fact]
        public void InspectorMissingFileIsReported()
        {
            using var workspace = new Workspace();
            WriteFile(RecipePaths.MappingFile(workspace.Root, "comfyui", "图标@v5"), MappingJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("缺文件", finding.Reason);
        }

        /// <summary>映射的节点id 不在 workflow 节点集合里时报 1 条。</summary>
        [Fact]
        public void InspectorMappingNodeNotInWorkflowIsReported()
        {
            using var workspace = new Workspace();
            var brokenMapping = MappingJson.Replace("\"节点id\": \"2\"", "\"节点id\": \"99\"");
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, brokenMapping);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), DependencyManifestJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("不在 workflow 的节点 id 里", finding.Reason);
        }

        /// <summary>锚点槽的节点id 不在 workflow 节点集合里时报 1 条。</summary>
        [Fact]
        public void InspectorAnchorSlotNodeNotInWorkflowIsReported()
        {
            using var workspace = new Workspace();
            var brokenMapping = MappingJson.Replace("\"节点id\": \"5\"", "\"节点id\": \"88\"");
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, brokenMapping);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), DependencyManifestJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("不在 workflow 的节点 id 里", finding.Reason);
        }

        /// <summary>请求字段不在白名单里时报 1 条，白名单外的字段一条不漏。</summary>
        [Fact]
        public void InspectorRequestFieldNotInWhitelistIsReported()
        {
            using var workspace = new Workspace();
            var brokenMapping = MappingJson.Replace("\"请求字段\": \"描述\"", "\"请求字段\": \"未知字段\"");
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, brokenMapping);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), DependencyManifestJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("不在白名单里", finding.Reason);
        }

        /// <summary>配方声明的依赖在依赖清单里查不到时报 1 条。</summary>
        [Fact]
        public void InspectorDependencyNotInManifestIsReported()
        {
            using var workspace = new Workspace();
            var brokenMapping = MappingJson.Replace("\"ComfyUI-Impact-Pack\"", "\"MissingPack\"");
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, brokenMapping);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), DependencyManifestJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("不在依赖清单里", finding.Reason);
        }

        /// <summary>配方声明了依赖但依赖清单文件不存在时报 1 条。</summary>
        [Fact]
        public void InspectorDependencyManifestMissingIsReported()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, MappingJson);

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("依赖清单文件不存在", finding.Reason);
        }

        /// <summary>依赖清单是坏 JSON 时转成 finding，不让异常穿出去。</summary>
        [Fact]
        public void InspectorBrokenManifestIsReported()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, MappingJson);
            WriteFile(RecipePaths.DependencyManifestFile(workspace.Root, "comfyui"), "not-json");

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("依赖清单文件", finding.Reason);
        }

        /// <summary>配方加载失败（mapping.json 坏）时转成 1 条 finding，不让异常穿出去。</summary>
        [Fact]
        public void InspectorLoadFailureIsReportedNotThrown()
        {
            using var workspace = new Workspace();
            WriteRecipe(workspace.Root, "图标@v5", WorkflowJson, "not-json");

            var findings = RecipeInspector.Inspect(workspace.Root, "comfyui");

            var finding = Assert.Single(findings);
            Assert.Contains("不是合法 JSON", finding.Reason);
        }

        private static void WriteRecipe(string root, string recipeName, string workflowJson, string mappingJson)
        {
            WriteFile(RecipePaths.WorkflowFile(root, "comfyui", recipeName), workflowJson);
            WriteFile(RecipePaths.MappingFile(root, "comfyui", recipeName), mappingJson);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "配方测试-" + Guid.NewGuid().ToString("N"));
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
