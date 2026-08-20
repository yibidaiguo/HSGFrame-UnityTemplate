using System;
using System.Collections.Generic;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>预审提示词组装测试：确定性（两次组装逐字符相同）、few-shot 按序数序取、空意见库可组装。</summary>
    public class PreReviewPromptTests
    {
        /// <summary>同一份输入两次组装 → 提示词逐字符相同（决策 58：不许随机，否则两次跑出不同提示词）。</summary>
        [Fact]
        public void SameInputTwiceProducesIdenticalPrompt()
        {
            using var workspace = new PoolTestWorkspace();
            var opinions = SampleOpinions();
            var specTexts = new[] { "规范一：不许用裸 null。", "规范二：热路径禁 GC 分配。" };

            var first = PreReviewPrompt.Build(workspace.Root, "diff 内容", specTexts, opinions, 10);
            var second = PreReviewPrompt.Build(workspace.Root, "diff 内容", specTexts, opinions, 10);

            Assert.Equal(first.PromptText, second.PromptText);
            Assert.Equal(first.PromptVersion, second.PromptVersion);
            Assert.False(string.IsNullOrWhiteSpace(first.PromptText));
        }

        /// <summary>few-shot 按意见 id 序数序取前 N 条：取 OP-0001/OP-0002，不取 OP-0003，顺序正确。</summary>
        [Fact]
        public void FewShotTakesFirstNByOrdinalIdentifierOrder()
        {
            using var workspace = new PoolTestWorkspace();
            var opinions = SampleOpinions();

            var result = PreReviewPrompt.Build(workspace.Root, "diff 内容", Array.Empty<string>(), opinions, 2);
            var text = result.PromptText;

            Assert.Contains("OP-0001", text);
            Assert.Contains("OP-0002", text);
            Assert.DoesNotContain("OP-0003", text);
            Assert.True(text.IndexOf("OP-0001", StringComparison.Ordinal) < text.IndexOf("OP-0002", StringComparison.Ordinal));
        }

        /// <summary>意见库为空 → 照样组装得出来，提示词含生效规范段与 diff 段。</summary>
        [Fact]
        public void EmptyOpinionBookStillBuildsPrompt()
        {
            using var workspace = new PoolTestWorkspace();
            var empty = new ReviewOpinionBook(Array.Empty<ReviewOpinion>(), "");

            var result = PreReviewPrompt.Build(workspace.Root, "diff 内容", new[] { "规范一。" }, empty, 10);

            Assert.False(string.IsNullOrWhiteSpace(result.PromptText));
            Assert.Contains("生效规范", result.PromptText);
            Assert.Contains("diff 内容", result.PromptText);
            Assert.Equal(PreReviewPrompt.PromptVersion, result.PromptVersion);
        }

        /// <summary>三条按 id 打乱的意见：组装顺序仍按 id 序数序（ReviewOpinionBook 保证排序，这里验证取的是前 N 条而非插入序）。</summary>
        [Fact]
        public void FewShotFollowsIdentifierOrderNotInsertionOrder()
        {
            using var workspace = new PoolTestWorkspace();
            // 故意按乱序插入；意见库视图应已按 id 排序，这里直接构造乱序列表验证 Build 不依赖传入顺序。
            var opinions = new ReviewOpinionBook(new[]
            {
                new ReviewOpinion("OP-0003", "类别三", "模块C", "不可规则化", "引用三", "2026-08-20T10:00:03+09:00"),
                new ReviewOpinion("OP-0001", "类别一", "模块A", "可代码化", "引用一", "2026-08-20T10:00:01+09:00"),
                new ReviewOpinion("OP-0002", "类别二", "模块B", "可提示词化", "引用二", "2026-08-20T10:00:02+09:00")
            }, "");

            var result = PreReviewPrompt.Build(workspace.Root, "diff 内容", Array.Empty<string>(), opinions, 2);
            var text = result.PromptText;

            // 取前 2 条（按 id 序数序 = OP-0001、OP-0002），OP-0003 不在。
            Assert.Contains("OP-0001", text);
            Assert.Contains("OP-0002", text);
            Assert.DoesNotContain("OP-0003", text);
        }

        /// <summary>构造一份带三条意见的意见库（id 序数序）。</summary>
        private static ReviewOpinionBook SampleOpinions()
        {
            return new ReviewOpinionBook(new[]
            {
                new ReviewOpinion("OP-0001", "空引用未防", "签到", "可代码化", "这里没判 null", "2026-08-20T10:00:01+09:00"),
                new ReviewOpinion("OP-0002", "命名歧义", "任务", "可提示词化", "这个名字有歧义", "2026-08-20T10:00:02+09:00"),
                new ReviewOpinion("OP-0003", "越权未查", "账户", "可代码化", "没查权限", "2026-08-20T10:00:03+09:00")
            }, "");
        }
    }
}
