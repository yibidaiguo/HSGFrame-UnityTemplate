using System;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>预审缓存测试：同键命中、换模型名不命中、换提示词版本不命中（决策 90：缓存键必须含模型名与提示词版本）。</summary>
    public class PreReviewCacheTests
    {
        private const string PromptText = "提示词全文";
        private const string ModelName = "deepseek-chat";
        private const string PromptVersion = "prereview-v1";

        /// <summary>同一键 Save 后 TryLoad → 命中，且报告标 来自缓存=true。</summary>
        [Fact]
        public void SameKeyHitsCache()
        {
            using var workspace = new PoolTestWorkspace();
            var key = PreReviewCache.ComputeKey(PromptText, ModelName, PromptVersion);
            var report = SampleReport();

            PreReviewCache.Save(workspace.Root, key, report);

            Assert.True(PreReviewCache.TryLoad(workspace.Root, key, out var loaded));
            Assert.True(loaded.FromCache);
            Assert.True(loaded.Parsed);
            Assert.Single(loaded.Findings);
            Assert.Equal("阻断级", loaded.Findings[0].Grade);
        }

        /// <summary>缓存键换模型名 → 键不同，不命中（换了模型还命中旧缓存，报告就在说谎）。</summary>
        [Fact]
        public void DifferentModelNameDoesNotHit()
        {
            using var workspace = new PoolTestWorkspace();
            var key = PreReviewCache.ComputeKey(PromptText, ModelName, PromptVersion);
            PreReviewCache.Save(workspace.Root, key, SampleReport());

            var otherKey = PreReviewCache.ComputeKey(PromptText, "another-model", PromptVersion);

            Assert.NotEqual(key, otherKey);
            Assert.False(PreReviewCache.TryLoad(workspace.Root, otherKey, out _));
        }

        /// <summary>缓存键换提示词版本 → 键不同，不命中。</summary>
        [Fact]
        public void DifferentPromptVersionDoesNotHit()
        {
            using var workspace = new PoolTestWorkspace();
            var key = PreReviewCache.ComputeKey(PromptText, ModelName, PromptVersion);
            PreReviewCache.Save(workspace.Root, key, SampleReport());

            var otherKey = PreReviewCache.ComputeKey(PromptText, ModelName, "prereview-v2");

            Assert.NotEqual(key, otherKey);
            Assert.False(PreReviewCache.TryLoad(workspace.Root, otherKey, out _));
        }

        /// <summary>同一键第二次 Save 覆盖：读回来是最新内容。</summary>
        [Fact]
        public void SaveOverwritesSameKey()
        {
            using var workspace = new PoolTestWorkspace();
            var key = PreReviewCache.ComputeKey(PromptText, ModelName, PromptVersion);
            PreReviewCache.Save(workspace.Root, key, SampleReport());

            var second = new PreReviewReport(
                parsed: true,
                model: ModelName,
                promptVersion: PromptVersion,
                decisionKey: key,
                findings: Array.Empty<PreReviewFinding>(),
                blockingCount: 0,
                suggestionCount: 0,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-21T00:00:00+09:00");
            PreReviewCache.Save(workspace.Root, key, second);

            Assert.True(PreReviewCache.TryLoad(workspace.Root, key, out var loaded));
            Assert.Empty(loaded.Findings);
        }

        /// <summary>构造一份可缓存的最小报告。</summary>
        private static PreReviewReport SampleReport()
        {
            return new PreReviewReport(
                parsed: true,
                model: ModelName,
                promptVersion: PromptVersion,
                decisionKey: PreReviewCache.ComputeKey(PromptText, ModelName, PromptVersion),
                findings: new[]
                {
                    new PreReviewFinding("阻断级", "A.cs", "L1", "空引用", "没判 null")
                },
                blockingCount: 1,
                suggestionCount: 0,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-21T00:00:00+09:00");
        }
    }
}
