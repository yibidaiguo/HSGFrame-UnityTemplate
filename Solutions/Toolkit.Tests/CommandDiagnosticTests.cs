using Template.Toolkit.CommandFramework;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>四要素诊断记录类型的格式测试。</summary>
    public class CommandDiagnosticTests
    {
        [Fact]
        public void ToStringMatchesGateFindingFormat()
        {
            var diagnostic = new CommandDiagnostic(
                "RepositoryRoot",
                "参数 JSON 为空",
                "用 --arguments-file 指向一个 JSON 对象文件",
                "{\"RepositoryRoot\":\"<字符串>\"}");

            Assert.Equal(
                "位置：RepositoryRoot；原因：参数 JSON 为空；修复：用 --arguments-file 指向一个 JSON 对象文件；参考：{\"RepositoryRoot\":\"<字符串>\"}",
                diagnostic.ToString());
        }

        [Fact]
        public void ToStringKeepsAllFourSegmentsWhenReasonIsEmpty()
        {
            var diagnostic = new CommandDiagnostic("A", "", "C", "D");

            var text = diagnostic.ToString();

            Assert.Equal("位置：A；原因：；修复：C；参考：D", text);
            Assert.Contains("原因：", text);
            Assert.Contains("修复：", text);
            Assert.Contains("参考：", text);
        }
    }
}
