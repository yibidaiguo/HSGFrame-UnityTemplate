using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 模块策划案推知识库这条链的测试。
    ///
    /// 真调下游那一段没法在这儿验（决策 6：飞书 API 不可验），所以盯的是
    /// **调下游之前的那几道判据**——它们才是这条链上会出错又难查的部分：
    /// 该不该推、推什么、推到哪个父节点下。
    /// </summary>
    public class ModulePlanPushTests
    {
        private const string Module = "Inventory";

        private static PoolTestWorkspace NewWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();
            return workspace;
        }

        private static PlanningDocumentSpec Spec(PoolTestWorkspace workspace)
        {
            return PlanningDocumentSpec.Load(workspace.RepositoryRoot);
        }

        private static void Render(PoolTestWorkspace workspace)
        {
            PlanningDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), false);
        }

        /// <summary>还没 plan.render 过时跳过并说清楚，不去调下游。</summary>
        [Fact]
        public void SkipsWhenThereIsNoDocumentYet()
        {
            using var workspace = NewWorkspace();
            Directory.CreateDirectory(PoolPaths.ModulePlanDirectory(workspace.Root, Module));

            var outcome = ModulePlanPusher.PushOne(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), true, false, 60);

            Assert.False(outcome.Pushed);
            Assert.True(outcome.Skipped);
            Assert.Contains("先跑 plan.render", outcome.Note);
        }

        /// <summary>
        /// **正文没变就不推。** 这份文档每条需求验收都会重渲一遍，
        /// 而重渲九成时候是无变化的——不比对就等于每验收一条需求都往知识库写一次全量。
        /// </summary>
        [Fact]
        public void SkipsWhenTheBodyMatchesWhatWasPushedLastTime()
        {
            using var workspace = NewWorkspace();
            Render(workspace);

            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            var text = File.ReadAllText(path);
            var hash = RequirementDocumentSyncState.HashBody(text);
            File.WriteAllText(
                path,
                RequirementDocumentSyncState.Write(
                    text, new RequirementDocumentSyncState("wikcnX", "https://x", hash, "2026-08-24T00:00:00Z")));

            var outcome = ModulePlanPusher.PushOne(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), true, false, 60);

            Assert.True(outcome.Skipped);
            Assert.Contains("与上次推上去的一致", outcome.Note);
        }

        /// <summary>强推能越过那道比对：人明说要重推时不该被「没变」挡住。</summary>
        [Fact]
        public void ForcedPushIgnoresTheHashComparison()
        {
            using var workspace = NewWorkspace();
            Render(workspace);

            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            var text = File.ReadAllText(path);
            var hash = RequirementDocumentSyncState.HashBody(text);
            File.WriteAllText(
                path,
                RequirementDocumentSyncState.Write(
                    text, new RequirementDocumentSyncState("wikcnX", "https://x", hash, "2026-08-24T00:00:00Z")));

            var outcome = ModulePlanPusher.PushOne(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), true, true, 60);

            Assert.False(outcome.Skipped);
        }

        /// <summary>同步账写在 frontmatter 里，**不进正文哈希**——否则一推就变、变了又要推，推到天荒地老。</summary>
        [Fact]
        public void TheSyncBlockItselfDoesNotChangeTheBodyHash()
        {
            using var workspace = NewWorkspace();
            Render(workspace);

            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            var before = File.ReadAllText(path);
            var hashBefore = RequirementDocumentSyncState.HashBody(before);

            var after = RequirementDocumentSyncState.Write(
                before, new RequirementDocumentSyncState("wikcnX", "https://x", hashBefore, "2026-08-24T00:00:00Z"));

            Assert.Equal(hashBefore, RequirementDocumentSyncState.HashBody(after));
        }

        /// <summary>共用的解析器认得模块策划案的生成区标记，frontmatter 也读得出来。</summary>
        [Fact]
        public void TheSharedParserUnderstandsThePlanningDocument()
        {
            using var workspace = NewWorkspace();
            Render(workspace);
            var specification = Spec(workspace);

            var parsed = RequirementDocument.TryParse(
                File.ReadAllText(PoolPaths.ModulePlanDocument(workspace.Root, Module)),
                specification.GeneratedRegionBegin,
                specification.GeneratedRegionEnd,
                out var document,
                out var reason);

            Assert.True(parsed, reason);
            Assert.Equal(Module, document.FrontMatter.Scalar("模块"));
            Assert.True(document.HasGeneratedRegion);
        }

        /// <summary>挂的是模块策划案那个父节点，不是需求文档那个——两层混在一起，模块正本会被需求淹掉。</summary>
        [Fact]
        public void PushesUnderTheModulePlanParentNode()
        {
            Assert.Equal("模块策划案父节点", ModulePlanPusher.ParentKeyName);
            Assert.Equal("模块策划案端", ModulePlanPusher.DocumentPortName);
        }

        /// <summary>渲完顺手推那条路：没变化时**连推都不试**，省掉一次下游调用。</summary>
        [Fact]
        public void RefreshDoesNotEvenTryToPushWhenNothingChanged()
        {
            using var workspace = NewWorkspace();
            var notes = new List<string>();
            ModulePlanRefresher.Refresh(workspace.RepositoryRoot, workspace.Root, Module, notes);

            var second = new List<string>();
            ModulePlanRefresher.Refresh(
                workspace.RepositoryRoot, workspace.Root, Module, second, alsoPush: true, timeoutSeconds: 5);

            Assert.Contains(second, note => note.Contains("无变化"));
            Assert.DoesNotContain(second, note => note.Contains("推上去了") || note.Contains("失败"));
        }
    }
}
