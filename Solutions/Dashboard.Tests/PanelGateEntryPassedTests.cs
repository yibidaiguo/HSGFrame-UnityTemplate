using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.Dashboard.Tests
{
    /// <summary>
    /// 门禁条目「通过」那一格的测试：页面只读这个布尔，所以它必须与
    /// <see cref="GateReportConclusion"/> 推总状态用的是同一套判定。
    /// 从前页面自己认「绿 / 通过」，而报告里写的是「成功」，
    /// 三十道全成功显示成 0 / 30——这几条就是拦那件事的。
    /// </summary>
    public class PanelGateEntryPassedTests
    {
        /// <summary>结果是「成功」的条目算过。</summary>
        [Fact]
        public void SucceededEntryIsPassed()
        {
            Assert.True(new PanelGateEntry("命名检查器", GateReportConclusion.SucceededResult, 0).Passed);
        }

        /// <summary>结果不是「成功」的一律算没过——包括从前页面认的那两个词。</summary>
        [Theory]
        [InlineData("失败")]
        [InlineData("绿")]
        [InlineData("通过")]
        [InlineData("")]
        public void AnythingElseIsNotPassed(string result)
        {
            Assert.False(new PanelGateEntry("命名检查器", result, 0).Passed);
        }

        /// <summary>逐道判定与总结论同源：全成功的报告里，每一道都得是「过」。</summary>
        [Fact]
        public void PassedMatchesConclusionVocabulary()
        {
            Assert.True(GateReportConclusion.IsPassed(GateReportConclusion.SucceededResult));
            Assert.False(GateReportConclusion.IsPassed(GateReportConclusion.Green));
        }
    }
}
