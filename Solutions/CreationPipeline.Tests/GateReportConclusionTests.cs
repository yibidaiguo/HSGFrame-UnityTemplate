using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 门禁结论推法的测试。
    /// 这几条断言存在的理由是一次真事故：进度页照着一个**不存在的「结论」键**读，
    /// 于是它永远说「未跑」，而总览页同时说「绿」——同一份报告，面板上两个答案。
    /// 所以这里盯的不是「绿红算得对不对」，而是**报告里根本没有结论字段、绿红是推出来的**这件事。
    /// </summary>
    public class GateReportConclusionTests
    {
        /// <summary>报告文件不存在 → 未跑。</summary>
        [Fact]
        public void MissingReportIsNotRun()
        {
            using var workspace = new PoolTestWorkspace();
            Assert.Equal(GateReportConclusion.NotRun, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>每一道都成功 → 绿。</summary>
        [Fact]
        public void AllSucceededIsGreen()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, """
            {"时间":"2026-08-24T00:00:00+09:00","条目":[
              {"名称":"秒级门禁","结果":"成功","问题数":0},
              {"名称":"十秒级门禁","结果":"成功","问题数":0}]}
            """);

            Assert.Equal(GateReportConclusion.Green, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>有一道不成功 → 红。</summary>
        [Fact]
        public void AnyFailureIsRed()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, """
            {"时间":"2026-08-24T00:00:00+09:00","条目":[
              {"名称":"秒级门禁","结果":"成功","问题数":0},
              {"名称":"命名门禁","结果":"失败","问题数":3}]}
            """);

            Assert.Equal(GateReportConclusion.Red, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>
        /// 报告里**没有「条目」数组** → 未跑。
        /// 这一条正是那次事故的形状：拿一个不存在的键去读，读不到时必须落到「未跑」，
        /// 而不是落到「绿」——把读不出来说成通过，是所有假绿里最贵的一种。
        /// </summary>
        [Fact]
        public void ReportWithoutEntriesIsNotRun()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, """{"时间":"2026-08-24T00:00:00+09:00"}""");

            Assert.Equal(GateReportConclusion.NotRun, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>坏 JSON → 未跑，不抛异常（这份结论进面板，面板不能因为一份坏报告整页打不开）。</summary>
        [Fact]
        public void BrokenJsonIsNotRun()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, "{ 这不是 JSON");

            Assert.Equal(GateReportConclusion.NotRun, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>空条目数组 → 绿：跑过了，零道不过。与「没跑过」是两回事。</summary>
        [Fact]
        public void EmptyEntriesIsGreen()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, """{"时间":"2026-08-24T00:00:00+09:00","条目":[]}""");

            Assert.Equal(GateReportConclusion.Green, GateReportConclusion.Read(workspace.RepositoryRoot));
        }

        /// <summary>进度快照的「门禁」那一格取的就是这一份结论——两处不许各推一遍。</summary>
        [Fact]
        public void ProgressSnapshotUsesTheSameConclusion()
        {
            using var workspace = new PoolTestWorkspace();
            WriteReport(workspace.RepositoryRoot, """
            {"时间":"2026-08-24T00:00:00+09:00","条目":[{"名称":"秒级门禁","结果":"成功","问题数":0}]}
            """);

            var snapshot = ProgressSnapshot.CollectFromRepository(workspace.RepositoryRoot, workspace.Root);

            Assert.Equal(GateReportConclusion.Green, snapshot.Global["门禁"]);
        }

        /// <summary>把报告写到 _Generated/gate-report.json。</summary>
        private static void WriteReport(string repositoryRoot, string json)
        {
            var filePath = GateReportConclusion.ReportFile(repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }
    }
}
