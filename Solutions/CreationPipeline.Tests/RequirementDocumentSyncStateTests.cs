using System;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 同步账的测试。守两件事：**哈希只算正文**（算上 frontmatter 的话一条需求会自己把自己推到天荒地老），
    /// 以及**回写只动同步块**（正文与别的 frontmatter 键一个字都不许变）。
    /// </summary>
    public class RequirementDocumentSyncStateTests
    {
        private const string DocumentWithoutSync = """
            ---
            需求id: REQ-0042
            标题: 七日签到
            ---

            # 七日签到

            ## 目标
            次留提升。
            """;

        private const string DocumentWithSync = """
            ---
            需求id: REQ-0042
            标题: 七日签到
            同步:
              节点token: wikcnAAAA
              链接: https://example.invalid/wiki/wikcnAAAA
              最后同步hash: sha256:0011223344556677
              最后同步时间: 2026-08-21T10:00:00Z
            媒体:
              - 路径: media/a.png
                说明: 一张图
            ---

            # 七日签到
            """;

        /// <summary>同步块读得出四项。</summary>
        [Fact]
        public void ReadsAllFourFields()
        {
            var state = RequirementDocumentSyncState.Read(Parse(DocumentWithSync));

            Assert.Equal("wikcnAAAA", state.NodeToken);
            Assert.Equal("https://example.invalid/wiki/wikcnAAAA", state.Link);
            Assert.Equal("sha256:0011223344556677", state.LastHash);
            Assert.Equal("2026-08-21T10:00:00Z", state.LastTime);
            Assert.True(state.HasBeenPushed);
        }

        /// <summary>没有同步块时四项全空，且算「没推过」。</summary>
        [Fact]
        public void MissingSyncSectionMeansNeverPushed()
        {
            var state = RequirementDocumentSyncState.Read(Parse(DocumentWithoutSync));

            Assert.Equal("", state.NodeToken);
            Assert.False(state.HasBeenPushed);
            Assert.True(state.NeedsPush("sha256:whatever"));
        }

        /// <summary>哈希对得上就不推，对不上才推。</summary>
        [Fact]
        public void NeedsPushComparesHashes()
        {
            var state = new RequirementDocumentSyncState("wikcnAAAA", "", "sha256:0011223344556677", "");

            Assert.False(state.NeedsPush("sha256:0011223344556677"));
            Assert.True(state.NeedsPush("sha256:ffffffffffffffff"));
        }

        /// <summary>
        /// 哈希只算正文：改 frontmatter 不改哈希。
        /// 这一条塌了的话，「推完写回同步块」本身就会把哈希改掉，下一次又判定要推。
        /// </summary>
        [Fact]
        public void HashIgnoresFrontMatter()
        {
            var before = RequirementDocumentSyncState.HashBody(DocumentWithoutSync);
            var after = RequirementDocumentSyncState.HashBody(
                RequirementDocumentSyncState.Write(
                    DocumentWithoutSync,
                    new RequirementDocumentSyncState("wikcnBBBB", "链接", "sha256:0011223344556677", "2026-08-23T00:00:00Z")));

            Assert.Equal(before, after);
        }

        /// <summary>正文变一个字，哈希就变。</summary>
        [Fact]
        public void HashFollowsBodyChanges()
        {
            Assert.NotEqual(
                RequirementDocumentSyncState.HashBody(DocumentWithoutSync),
                RequirementDocumentSyncState.HashBody(DocumentWithoutSync + "\n补一句。"));
        }

        /// <summary>本来没有同步块：补在 frontmatter 末尾，正文与其余键原样不动。</summary>
        [Fact]
        public void WriteAppendsSyncSectionWhenAbsent()
        {
            var updated = RequirementDocumentSyncState.Write(
                DocumentWithoutSync,
                new RequirementDocumentSyncState("wikcnBBBB", "https://example.invalid/x", "sha256:aabbccddeeff0011", "2026-08-23T00:00:00Z"));

            var state = RequirementDocumentSyncState.Read(Parse(updated));
            Assert.Equal("wikcnBBBB", state.NodeToken);
            Assert.Contains("需求id: REQ-0042", updated);
            Assert.Contains("次留提升。", updated);
        }

        /// <summary>本来就有同步块：整块换掉而不是叠一份，且它后面的 frontmatter 键还在。</summary>
        [Fact]
        public void WriteReplacesExistingSyncSectionInPlace()
        {
            var updated = RequirementDocumentSyncState.Write(
                DocumentWithSync,
                new RequirementDocumentSyncState("wikcnNEW", "https://example.invalid/new", "sha256:aabbccddeeff0011", "2026-08-23T00:00:00Z"));

            var state = RequirementDocumentSyncState.Read(Parse(updated));
            Assert.Equal("wikcnNEW", state.NodeToken);
            Assert.DoesNotContain("wikcnAAAA", updated);

            // 同步块后面那个「媒体」列表不许被吃掉——按缩进吃行的实现最容易在这里出错。
            var media = Assert.Single(Parse(updated).FrontMatter.List("媒体"));
            Assert.Equal("media/a.png", media["路径"]);
        }

        /// <summary>没有 frontmatter 的文档不敢写，报一句人看得懂的话。</summary>
        [Fact]
        public void WriteRefusesDocumentWithoutFrontMatter()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RequirementDocumentSyncState.Write("# 光杆文档", new RequirementDocumentSyncState("a", "b", "c", "d")));

            Assert.Contains("doc.render", exception.Message);
        }

        /// <summary>拿一份基线规范来解析：这一族测的是同步块，规范只是解析时的必需品。</summary>
        private static RequirementDocument Parse(string text)
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            var specification = RequirementDocumentSpec.Load(workspace.Root);

            Assert.True(RequirementDocument.TryParse(text, specification, out var parsed, out var reason), reason);
            return parsed;
        }
    }
}
