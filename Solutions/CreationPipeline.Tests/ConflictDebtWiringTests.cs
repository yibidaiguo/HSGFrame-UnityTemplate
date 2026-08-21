using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 三个出口的接线测试：审查包第六节、助手包第七文件 + 设计池摘要标注 + 系统提示职责，
    /// 验的是「账真的铺到了出口上」，不是再测一遍 ConflictDebtView。
    /// </summary>
    public class ConflictDebtWiringTests
    {
        /// <summary>审查包：conflictDebt 为 null → 第六节含「未查」，绝不含「无未决冲突」。</summary>
        [Fact]
        public void ReviewSectionShowsNotCheckedWhenDebtNull()
        {
            var input = new ReviewPackageInput(
                "REQ-0001",
                new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                30,
                "无偏差",
                "预审通过",
                "验收通过",
                new List<string> { "feat: 改动 A" },
                null);
            var markdown = ReviewPackageBuilder.Build(input, LowRisk(), AutoDecision());

            Assert.Contains("## 六、未决冲突", markdown);
            Assert.Contains("（未查）", markdown);
            Assert.DoesNotContain("本需求无未决冲突", markdown);
        }

        /// <summary>审查包：有两条未决（其中一条强推）→ 两行都在、强推那行有 ⚠、合计行数字对得上。</summary>
        [Fact]
        public void ReviewSectionListsItemsAndMarksForcePush()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "REQ-0007",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "REQ-0009",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": {
                  "人": "张三",
                  "选择": "强制推送",
                  "时间": "2026-08-19T10:00:00+08:00"
                }
              }
            ]
            """);
            var debt = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0042");
            var markdown = ReviewPackageBuilder.Build(
                new ReviewPackageInput(
                    "REQ-0042",
                    new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                    30,
                    "无偏差",
                    "预审通过",
                    "验收通过",
                    new List<string> { "feat: 改动 A" },
                    debt),
                LowRisk(),
                AutoDecision());

            Assert.Contains("- CF-0001：REQ-0007 与 REQ-0042 冲突（入库阶段发现），尚未销账", markdown);
            Assert.Contains("- ⚠ CF-0002：REQ-0009 与 REQ-0042 冲突（入库阶段发现），张三强制推送挂账，尚未销账", markdown);
            Assert.Contains("合计：本需求 2 条未决，池子共 2 条", markdown);
        }

        /// <summary>审查包：Scanned=true 零条 → 含「本需求无未决冲突」，与「未查」是两个分支。</summary>
        [Fact]
        public void ReviewSectionShowsZeroPendingWhenScanned()
        {
            using var workspace = new PoolTestWorkspace();
            var debt = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0042");
            var markdown = ReviewPackageBuilder.Build(
                new ReviewPackageInput(
                    "REQ-0042",
                    new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                    30,
                    "无偏差",
                    "预审通过",
                    "验收通过",
                    new List<string> { "feat: 改动 A" },
                    debt),
                LowRisk(),
                AutoDecision());

            Assert.Contains("本需求无未决冲突（池子里共 0 条未决）", markdown);
            Assert.DoesNotContain("（未查）", markdown);
        }

        /// <summary>审查包：未决冲突不改放行结论——带/不带未决，「放行结论：」那一行逐字相同（决策 51）。</summary>
        [Fact]
        public void ReviewConclusionUnchangedByConflictDebt()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "REQ-0007",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);
            var debt = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0042");
            var risk = LowRisk();
            var decision = AutoDecision();

            var markdownWith = ReviewPackageBuilder.Build(
                new ReviewPackageInput(
                    "REQ-0042",
                    new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                    30,
                    "无偏差",
                    "预审通过",
                    "验收通过",
                    new List<string> { "feat: 改动 A" },
                    debt),
                risk,
                decision);
            var markdownWithout = ReviewPackageBuilder.Build(
                new ReviewPackageInput(
                    "REQ-0042",
                    new[] { "UnityProject/Assets/Game/Scripts/Modules/签到/A.cs" },
                    30,
                    "无偏差",
                    "预审通过",
                    "验收通过",
                    new List<string> { "feat: 改动 A" },
                    null),
                risk,
                decision);

            Assert.Equal(ExtractConclusion(markdownWithout), ExtractConclusion(markdownWith));
        }

        /// <summary>助手包：Build 之后 知识/conflicts.md 真的存在，且 ProspectiveFiles 的返回里含它，两边完全一致。</summary>
        [Fact]
        public void AssistantPackageWritesConflictListAndProspectiveMatches()
        {
            using var workspace = PrepareWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");
            var conflictList = ConflictList.Load(workspace.Root);

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", conflictList);
            var prospective = AssistantPackageBuilder.ProspectiveFiles(workspace.Root, "测试驱动");

            var conflictListFile = files.Single(file => Path.GetFileName(file) == "conflicts.md");
            Assert.True(File.Exists(conflictListFile));
            Assert.Contains(conflictListFile, prospective);
            Assert.Equal(prospective, files);
        }

        /// <summary>
        /// 助手包的第三份名单也要跟上：AssistantPackageInspector 查「产物齐全」用的是它自己那份清单，
        /// 与 Build/ProspectiveFiles 是两套。P6 批次三加第七个包文件时只同步了前两套，
        /// 结果 gate.provision 明明缺文件却是绿的——这条测试就是拦那种假绿的。
        /// </summary>
        [Fact]
        public void PackageInspectorKnowsEveryFileBuildWrites()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var written = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", null);
            var inspected = AssistantPackageInspector.Inspect(workspace.Root, "测试驱动");

            // 检查器的清单还含建表描述/专项表/校验错误文案/指纹这些非助手包产物，所以是「包含」不是「相等」。
            var inspectedNames = inspected.Artifacts.Select(artifact => Path.GetFileName(artifact.RelativePath)).ToList();
            foreach (var file in written)
            {
                Assert.Contains(Path.GetFileName(file), inspectedNames);
            }
        }

        /// <summary>助手包：冲突列表读不成 → conflicts.md 里含「没读成」，不含「暂无冲突」。</summary>
        [Fact]
        public void AssistantConflictListNotReadableShowsDeclaration()
        {
            using var workspace = PrepareWorkspace();
            var filePath = PoolPaths.ConflictListFile(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, "{ \"bad\": true }", new UTF8Encoding(false));
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", ConflictList.Load(workspace.Root));
            var conflictListMarkdown = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "conflicts.md"));

            Assert.Contains("没读成", conflictListMarkdown);
            Assert.DoesNotContain("暂无冲突", conflictListMarkdown);
        }

        /// <summary>助手包：有未决冲突 → 设计池摘要顶部含 ⚠ 冲突区域，且原有的各文件小节一节没少。</summary>
        [Fact]
        public void DesignSummaryGetsConflictMarkAndKeepsSections()
        {
            using var workspace = PrepareWorkspace();
            var summaryDirectory = PoolPaths.DesignSummaryDirectory(workspace.Root);
            Directory.CreateDirectory(summaryDirectory);
            File.WriteAllText(Path.Combine(summaryDirectory, "签到.md"), "签到系统当前设计。", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(summaryDirectory, "战斗.md"), "战斗系统当前设计。", new UTF8Encoding(false));
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", ConflictList.Load(workspace.Root));
            var designSummary = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "design-digest.md"));

            Assert.Contains("⚠ 冲突区域", designSummary);
            Assert.True(designSummary.IndexOf("⚠ 冲突区域", StringComparison.Ordinal) < designSummary.IndexOf("## 签到", StringComparison.Ordinal));
            Assert.Contains("## 签到", designSummary);
            Assert.Contains("## 战斗", designSummary);
        }

        /// <summary>助手包：零未决 → 设计池摘要不含 ⚠ 冲突区域。</summary>
        [Fact]
        public void DesignSummaryNoMarkWhenZeroPending()
        {
            using var workspace = PrepareWorkspace();
            var summaryDirectory = PoolPaths.DesignSummaryDirectory(workspace.Root);
            Directory.CreateDirectory(summaryDirectory);
            File.WriteAllText(Path.Combine(summaryDirectory, "签到.md"), "签到系统当前设计。", new UTF8Encoding(false));
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", ConflictList.Load(workspace.Root));
            var designSummary = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "design-digest.md"));

            Assert.DoesNotContain("⚠ 冲突区域", designSummary);
            Assert.Contains("## 签到", designSummary);
        }

        /// <summary>系统提示里含那句冲突提醒职责的原文（逐字）。</summary>
        [Fact]
        public void SystemPromptContainsConflictReminderDuty()
        {
            using var workspace = PrepareWorkspace();
            var schema = PoolSchemaLoader.Load(workspace.Root, "需求");

            var files = AssistantPackageBuilder.Build(workspace.Root, workspace.Root, schema, "测试驱动", ConflictList.Load(workspace.Root));
            var systemPrompt = File.ReadAllText(files.Single(file => Path.GetFileName(file) == "system-prompt.md"));

            Assert.Contains("4. 新需求碰到「conflicts.md」里列出的涉区 id 时，先提醒提出人那块还挂着未销账的冲突，再继续填写。", systemPrompt);
        }

        /// <summary>备一个池子：基线 schema 写进池根，知识目录与设计池汇总目录都不预建。</summary>
        private static PoolTestWorkspace PrepareWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            return workspace;
        }

        /// <summary>把冲突列表 JSON 写到池子的 Designs/conflicts.json。</summary>
        private static void WriteConflictList(string poolRoot, string json)
        {
            var filePath = PoolPaths.ConflictListFile(poolRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }

        /// <summary>低风险分级结果。</summary>
        private static RiskGradeResult LowRisk()
        {
            return new RiskGradeResult("低", new[] { "业务" }, "小改动且只涉业务或其它范围、零发现");
        }

        /// <summary>自动放行判定。</summary>
        private static ReleaseDecision AutoDecision()
        {
            return new ReleaseDecision(true, "低", new[] { "业务" }, Array.Empty<string>());
        }

        /// <summary>取「放行结论：」那一行原文。</summary>
        private static string ExtractConclusion(string markdown)
        {
            foreach (var line in markdown.Split('\n'))
            {
                if (line.StartsWith("放行结论：", StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return "";
        }
    }
}
