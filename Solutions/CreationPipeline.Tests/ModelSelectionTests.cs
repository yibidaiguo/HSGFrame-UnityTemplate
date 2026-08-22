using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 「模型」那一格的测试。这一族守的是三件事：
    /// **哨兵不许当模型名发出去**、**挑不出来时说的是「还没探过」而不是「没有」**、
    /// **挑了谁必须有账可查**。三件里塌任何一件，「自动」就从一个可解释的选择
    /// 变成一个凭空冒出模型名的黑箱。
    /// </summary>
    public class ModelSelectionTests
    {
        /// <summary>本次调用指定了模型：盖过本机配置，账里两个名字都要出现。</summary>
        [Fact]
        public void OverrideBeatsConfiguredValueAndSaysSo()
        {
            using var workspace = new Workspace();

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", "配着的模型", "这次要的模型", out var note);

            Assert.Equal("这次要的模型", chosen);
            Assert.Contains("这次要的模型", note);
            Assert.Contains("配着的模型", note);
        }

        /// <summary>配的是具体值、这次没指定：原样交出去，没什么可说的（账是空串）。</summary>
        [Fact]
        public void ConfiguredConcreteValuePassesThroughWithoutNote()
        {
            using var workspace = new Workspace();

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", "钉死的模型", "", out var note);

            Assert.Equal("钉死的模型", chosen);
            Assert.Equal("", note);
        }

        /// <summary>
        /// 配「自动」但还没探过：**一个 model 参数都不发**（返回空串），
        /// 而且那句话必须是「还没探过」而不是「没有可选的」——这两句差得远。
        /// </summary>
        [Fact]
        public void AutoWithoutProbeSendsNothingAndSaysNotProbedYet()
        {
            using var workspace = new Workspace();

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("", chosen);
            Assert.Contains("还没探过", note);
            Assert.Contains("bridge.probe --Driver testdriver", note);
            Assert.DoesNotContain("没有可选", note);
        }

        /// <summary>探过了但清单是空的：同样不发，但话不一样——这是「探回来就是空的」。</summary>
        [Fact]
        public void AutoWithEmptyCatalogSendsNothingAndSaysCatalogIsEmpty()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", Array.Empty<string>(), "https://例子/v1");

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("", chosen);
            Assert.Contains("清单是空的", note);
        }

        /// <summary>清单非空：挑序数序第一项，账里带上项数与探测来源。</summary>
        [Fact]
        public void AutoPicksFirstOrdinalNameAndReportsProvenance()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "z-模型", "a-模型", "m-模型" }, "https://例子/v1");

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("a-模型", chosen);
            Assert.Contains("3 项", note);
            Assert.Contains("https://例子/v1", note);
        }

        /// <summary>
        /// 记过一笔「这个模型真跑成功过」之后，「自动」优先挑它——**哪怕它不是序数序第一项**。
        /// 这条守的是真实踩到的坑：中转的 /models 里混着别的域的模型，
        /// 生图地址上序数序第一项可能是个代码审查模型，按顺序挑就挑出个用不了的值。
        /// </summary>
        [Fact]
        public void AutoPrefersLastGoodModelOverFirstOrdinal()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "a-审查模型", "z-出图模型" }, "https://例子/v1");
            ModelSelection.RecordSuccess(workspace.Root, "testdriver", "z-出图模型");

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("z-出图模型", chosen);
            Assert.Contains("真跑成功过", note);
        }

        /// <summary>记账里那个已经不在清单里（换了地址、下游下架了）：退回序数序第一项，并说清它过期了。</summary>
        [Fact]
        public void AutoFallsBackWhenLastGoodModelLeftTheCatalog()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "a-新模型", "b-新模型" }, "https://例子/v1");
            ModelSelection.RecordSuccess(workspace.Root, "testdriver", "已下架的模型");

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("a-新模型", chosen);
            Assert.Contains("已下架的模型", note);
            Assert.Contains("不在清单里", note);
        }

        /// <summary>哨兵与空串不许被记成「成功用过的模型」——记进去下次就会把哨兵当模型名挑出来。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("自动")]
        public void RecordSuccessIgnoresSentinelAndBlank(string value)
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "甲-模型" }, "");

            ModelSelection.RecordSuccess(workspace.Root, "testdriver", value);
            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("甲-模型", chosen);
            Assert.DoesNotContain("真跑成功过", note);
        }

        /// <summary>配「自动」时给了 --Model：覆盖优先，压根不去读探测产出。</summary>
        [Fact]
        public void OverrideWinsEvenWhenConfiguredAuto()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "a-模型" }, "");

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "指名这个", out var note);

            Assert.Equal("指名这个", chosen);
            Assert.Contains("指名这个", note);
        }

        /// <summary>哨兵判定不吃前后空白——手改 local.json 时很容易带一个空格进去。</summary>
        [Theory]
        [InlineData("自动", true)]
        [InlineData("  自动  ", true)]
        [InlineData("自动挡", false)]
        [InlineData("", false)]
        public void IsAutoIgnoresSurroundingWhitespace(string value, bool expected)
        {
            Assert.Equal(expected, ModelSelection.IsAuto(value));
        }

        /// <summary>PreviewAuto 就是「配着自动」那一路，面板与 bridge.catalog 拿它显示落点。</summary>
        [Fact]
        public void PreviewAutoMatchesResolveWithSentinel()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "b-模型", "a-模型" }, "");

            var preview = ModelSelection.PreviewAuto(workspace.Root, "testdriver", out var previewNote);
            var resolved = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var resolveNote);

            Assert.Equal(resolved, preview);
            Assert.Equal(resolveNote, previewNote);
            Assert.Equal("a-模型", preview);
        }

        /// <summary>探测产出没盖章（老产出）时照样读得动，只是账里不提地址。</summary>
        [Fact]
        public void UnstampedProbeResultStillUsableWithoutProvenance()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "testdriver", new[] { "只有一个" }, null);

            var chosen = ModelSelection.Resolve(workspace.Root, "testdriver", ModelSelection.AutoSentinel, "", out var note);

            Assert.Equal("只有一个", chosen);
            Assert.DoesNotContain("探于", note);
        }

        /// <summary>盖过章的产出：探于地址与探测时间都读得出来，缺键时是空串而不是抛。</summary>
        [Fact]
        public void ProbeResultReadsStampAndToleratesMissingKeys()
        {
            using var workspace = new Workspace();
            WriteProbeResult(workspace.Root, "stamped", new[] { "甲" }, "https://探过的地址/v1");
            WriteProbeResult(workspace.Root, "plain", new[] { "甲" }, null);

            var stamped = CapabilityProbeResult.LoadFromFile(ProvisionPaths.ProbeResultFile(workspace.Root, "stamped"));
            var plain = CapabilityProbeResult.LoadFromFile(ProvisionPaths.ProbeResultFile(workspace.Root, "plain"));

            Assert.Equal("https://探过的地址/v1", stamped.ProbedEndpoint);
            Assert.NotEqual("", stamped.ProbedAtText);
            Assert.Equal("", plain.ProbedEndpoint);
            Assert.Equal("", plain.ProbedAtText);
        }

        /// <summary>「哪个字段是模型字段」由自述的「选项来源」声明说了算，不按字段名猜。</summary>
        [Fact]
        public void DescriptorReadsModelFieldFromOptionSourceDeclaration()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "withmodel", """
                {
                  "名称": "withmodel",
                  "port": ["模型生成"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "地址": { "类型": "string", "默认": "" },
                    "模型版本": { "类型": "string", "默认": "", "选项来源": "探测.模型" }
                  },
                  "实现": "bridge-withmodel",
                  "字段类型映射": {}
                }
                """);

            var descriptor = BridgeDriverDescriptor.Load(workspace.Root, "withmodel");

            Assert.Equal("模型版本", descriptor.ModelFieldName);
        }

        /// <summary>没有哪个字段声明「探测.模型」时是空串：这个 driver 没有模型可挑，不是「叫模型的那一格」。</summary>
        [Fact]
        public void DescriptorWithoutDeclarationHasNoModelField()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "nomodel", """
                {
                  "名称": "nomodel",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "模型": { "类型": "string", "默认": "" } },
                  "实现": "bridge-nomodel",
                  "字段类型映射": {}
                }
                """);

            var descriptor = BridgeDriverDescriptor.Load(workspace.Root, "nomodel");

            Assert.Equal("", descriptor.ModelFieldName);
        }

        /// <summary>声明了两个模型字段：形状错，当场抛，不静默取第一个。</summary>
        [Fact]
        public void DescriptorWithTwoModelFieldsThrows()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "twomodels", """
                {
                  "名称": "twomodels",
                  "port": ["生图"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": {
                    "模型": { "类型": "string", "默认": "", "选项来源": "探测.模型" },
                    "模型版本": { "类型": "string", "默认": "", "选项来源": "探测.模型" }
                  },
                  "实现": "bridge-twomodels",
                  "字段类型映射": {}
                }
                """);

            var exception = Assert.Throws<InvalidOperationException>(() => BridgeDriverDescriptor.Load(workspace.Root, "twomodels"));

            Assert.Contains("只能有一个模型字段", exception.Message);
        }

        /// <summary>
        /// 调用一次下游时，「自动」的账要跟着结果一路带回调用方——
        /// 调用本身失败（这里子进程 stdout 不是协议 JSON）也照样带。
        /// </summary>
        [Fact]
        public void InvokeCarriesModelNoteBackEvenWhenCallFails()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "twomodel-free");
            WriteDriver(workspace.Root, "twomodel-free", """
                {
                  "名称": "twomodel-free",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "模型": { "类型": "string", "默认": "", "选项来源": "探测.模型" } },
                  "实现": "bridge-test",
                  "字段类型映射": {}
                }
                """);
            WriteLocalSettings(workspace.Root, "twomodel-free", ModelSelection.AutoSentinel);
            WriteProbeResult(workspace.Root, "twomodel-free", new[] { "乙模型", "甲模型" }, "https://探过的/v1");

            var result = BridgeInvoker.Invoke(workspace.Root, "twomodel-free", "caps", EmptyPayload(), timeoutSeconds: 60);

            Assert.False(result.Succeeded);

            // 序数序按码位排，不按拼音：「乙」(U+4E59) 排在「甲」(U+7532) 前面。
            // 这条断言顺带钉死了这一点——「第一项」得是可复算的，不能靠语感。
            Assert.Contains("乙模型", result.ModelNote);
        }

        /// <summary>driver 没有模型字段却给了 --Model：当场失败，不把它悄悄丢掉。</summary>
        [Fact]
        public void InvokeRejectsModelOverrideWhenDriverHasNoModelField()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "plaindriver");
            WriteDriver(workspace.Root, "plaindriver", """
                {
                  "名称": "plaindriver",
                  "port": ["模型加工"],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "配置schema": { "可执行文件": { "类型": "string", "默认": "" } },
                  "实现": "bridge-test",
                  "字段类型映射": {}
                }
                """);

            var result = BridgeInvoker.Invoke(workspace.Root, "plaindriver", "caps", EmptyPayload(), timeoutSeconds: 60, modelOverride: "硬塞一个");

            Assert.False(result.Succeeded);
            Assert.Equal("本机配置错误", result.ErrorCode);
            Assert.Contains("无处可放", result.HumanText);
        }

        private static JsonElement EmptyPayload()
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        /// <summary>写一份探测产出。<paramref name="probedEndpoint"/> 传 null 表示不盖章（老产出的长相）。</summary>
        private static void WriteProbeResult(string root, string driverName, string[] modelNames, string probedEndpoint)
        {
            var models = new StringBuilder();
            for (var index = 0; index < modelNames.Length; index++)
            {
                if (index > 0)
                {
                    models.Append(',');
                }

                models.Append("{\"名\":").Append(JsonSerializer.Serialize(modelNames[index])).Append(",\"版本\":\"\",\"hash\":\"\"}");
            }

            var stamp = probedEndpoint == null
                ? ""
                : ",\"探于\":" + JsonSerializer.Serialize(probedEndpoint) + ",\"探测时间\":\"2026-08-20T01:02:03.0000000Z\"";
            var json = "{\"节点\":[],\"模型\":[" + models + "],\"lora\":[]" + stamp + "}";

            var path = ProvisionPaths.ProbeResultFile(root, driverName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static void WriteDriver(string root, string driverName, string driverJson)
        {
            var path = BridgeDriverDescriptor.DriverFile(root, driverName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, driverJson, new UTF8Encoding(false));
        }

        private static void WriteRouteTable(string root, string driverName)
        {
            var json = "{\n"
                + "  \"契约版本\": \"1.0.0\",\n"
                + "  \"域路由\": { \"模型加工\": \"" + driverName + "\" },\n"
                + "  \"实现\": { \"bridge-test\": { \"可执行\": \"cmd\", \"参数\": [\"/c\", \"echo 这不是JSON\"] } }\n"
                + "}";
            var path = Path.Combine(root, "Tools", "CreationPipeline", "Config", "downstream.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static void WriteLocalSettings(string root, string driverName, string modelValue)
        {
            var json = "{\n"
                + "  \"下游配置\": { \"" + driverName + "\": { \"模型\": " + JsonSerializer.Serialize(modelValue) + " } }\n"
                + "}";
            var path = Path.Combine(root, "Tools", "CreationPipeline", "Config", "local.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "模型格测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, true);
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
