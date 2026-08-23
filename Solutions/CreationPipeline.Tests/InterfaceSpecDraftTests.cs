using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 「从需求产界面规格草案」这一步：提示词组得对不对、模型的回答解不解得回来。
    ///
    /// 这一层刻意跟「调后端」分开，就是为了能在**不花一分钱**的情况下钉住它——
    /// 提示词里少写一句、模型多包一层代码块，都该在这里测出来，
    /// 而不是花钱跑一趟才发现。
    /// </summary>
    public sealed class InterfaceSpecDraftTests
    {
        /// <summary>
        /// 可用的元素类型要摆进提示词。
        /// 不摆的话模型会自造类型名，而那些类型没有模板，校验时全部判红——
        /// 白花一次调用的钱。
        /// </summary>
        [Fact]
        public void PromptListsAvailableElementTypes()
        {
            var prompt = InterfaceSpecDraftPrompt.Build("要个背包", "Inventory", 1920, 1080, Catalog(), null);

            Assert.Contains("Button", prompt);
            Assert.Contains("Label", prompt);
            Assert.Contains("不出图", prompt);
        }

        /// <summary>那几条硬规矩要写进提示词——它们正是校验层会拦的东西。</summary>
        [Theory]
        [InlineData("不许缩写")]
        [InlineData("失败」是数组")]
        [InlineData("必须能测")]
        [InlineData("重复")]
        public void PromptCarriesTheHardRules(string fragment)
        {
            var prompt = InterfaceSpecDraftPrompt.Build("要个背包", "Inventory", 1920, 1080, Catalog(), null);

            Assert.Contains(fragment, prompt);
        }

        /// <summary>有总设计层与负面清单时带进去；**色板不带**——这一步定功能契约，不谈配色。</summary>
        [Fact]
        public void PromptCarriesDirectionAndNegativeListButNotPalette()
        {
            var anchor = new StyleAnchor(
                "低饱和冷色系",
                new[] { "#2b3a4a" },
                new[] { "不要赛博朋克霓虹" },
                Array.Empty<string>(),
                "项目风格@v1",
                false,
                Array.Empty<string>());

            var prompt = InterfaceSpecDraftPrompt.Build("要个背包", "Inventory", 1920, 1080, Catalog(), anchor);

            Assert.Contains("低饱和冷色系", prompt);
            Assert.Contains("不要赛博朋克霓虹", prompt);
            Assert.DoesNotContain("#2b3a4a", prompt);
        }

        /// <summary>模型的回答包在代码块里、前后有闲话，照样抠得出那份 JSON。</summary>
        [Fact]
        public void ReplyWrappedInCodeFenceIsParsed()
        {
            var text = "好的，这是规格：\n```json\n" + MinimalReply + "\n```\n就这样。";

            Assert.True(InterfaceSpecDraftPrompt.TryParse(text, "UI-0003", "REQ-0042", out var spec, out var reason), reason);
            Assert.Equal("Inventory", spec["面板"].GetValue<string>());
        }

        /// <summary>
        /// 机器该填的四样以机器为准。**模型不许自己发 id**——
        /// 让它编的话，重跑两次就会撞号或者跳号。
        /// </summary>
        [Fact]
        public void MachineOwnedFieldsOverrideWhateverTheModelWrote()
        {
            var text = MinimalReply.Replace("\"面板\"", "\"id\": \"UI-9999\", \"状态\": \"已定稿\", \"面板\"");

            Assert.True(InterfaceSpecDraftPrompt.TryParse(text, "UI-0003", "REQ-0042", out var spec, out _));

            Assert.Equal("UI-0003", spec["id"].GetValue<string>());
            Assert.Equal("草稿", spec["状态"].GetValue<string>());
            Assert.Equal("REQ-0042", (spec["来源需求"] as JsonArray)[0].GetValue<string>());
        }

        /// <summary>回了空文本、没有 JSON、元素是空数组——三种都要判失败并说清原因。</summary>
        [Theory]
        [InlineData("", "空文本")]
        [InlineData("我觉得这个界面应该有个按钮", "JSON")]
        [InlineData("{\"面板\":\"Inventory\",\"元素\":[]}", "元素")]
        public void UnusableReplyIsRejectedWithAReadableReason(string text, string expectedFragment)
        {
            Assert.False(InterfaceSpecDraftPrompt.TryParse(text, "UI-0003", "REQ-0042", out var spec, out var reason));
            Assert.Null(spec);
            Assert.Contains(expectedFragment, reason);
        }

        /// <summary>嵌套对象里的花括号不许把抠 JSON 那一步弄断。</summary>
        [Fact]
        public void NestedBracesDoNotTruncateTheJson()
        {
            Assert.True(InterfaceSpecDraftPrompt.TryParse(MinimalReply, "UI-0003", "REQ-0042", out var spec, out _));

            var elements = spec["元素"] as JsonArray;
            Assert.Single(elements);
            Assert.Equal("ButtonSort", elements[0]["id"].GetValue<string>());
        }

        /// <summary>字符串里的花括号也不许（模型很爱在文案里写「{数量}」这种占位符）。</summary>
        [Fact]
        public void BracesInsideStringsDoNotConfuseTheExtractor()
        {
            var text = MinimalReply.Replace("\"点了会重排\"", "\"提示写作 {已用}/{总数}\"");

            Assert.True(InterfaceSpecDraftPrompt.TryParse(text, "UI-0003", "REQ-0042", out var spec, out var reason), reason);
            Assert.Single(spec["元素"] as JsonArray);
        }

        /// <summary>发号按现存最大号 + 1，与 REQ-/DR-/ASSET- 同一套规矩。</summary>
        [Fact]
        public void IdentifierIsAllocatedFromTheHighestExisting()
        {
            using var workspace = new Workspace();
            var directory = InterfaceSpec.Directory(workspace.Root);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "UI-0001.json"), "{}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "UI-0007.json"), "{}", new UTF8Encoding(false));

            Assert.Equal("UI-0008", InterfaceSpecDraftPrompt.AllocateIdentifier(workspace.Root));
        }

        /// <summary>一份都没有时从 UI-0001 起。</summary>
        [Fact]
        public void FirstIdentifierIsOne()
        {
            using var workspace = new Workspace();

            Assert.Equal("UI-0001", InterfaceSpecDraftPrompt.AllocateIdentifier(workspace.Root));
        }

        /// <summary>造一份够用的模板目录：只要类型名与必填，不碰磁盘。</summary>
        private static UiElementTemplateCatalog Catalog()
        {
            var templates = new Dictionary<string, UiElementTemplate>(StringComparer.Ordinal)
            {
                ["Button"] = new UiElementTemplate(
                    "Button", new[] { "交互", "成功", "失败", "状态" }, Array.Empty<string>(), true),
                ["Label"] = new UiElementTemplate(
                    "Label", new[] { "数据" }, Array.Empty<string>(), false)
            };

            return new UiElementTemplateCatalog(
                new[] { "id", "名称", "类型", "布局", "复用", "验收" }, templates, Array.Empty<PoolFinding>());
        }

        private const string MinimalReply = @"{
  ""面板"": ""Inventory"", ""标题"": ""背包"",
  ""画布"": { ""宽"": 1920, ""高"": 1080 },
  ""元素"": [
    { ""id"": ""ButtonSort"", ""名称"": ""排序"", ""类型"": ""Button"",
      ""布局"": { ""位置"": [10, 10], ""尺寸"": [96, 96] },
      ""复用"": ""本界面专有"", ""验收"": ""点了会重排"" }
  ]
}";

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "界面草案测试-" + Guid.NewGuid().ToString("N"));
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
