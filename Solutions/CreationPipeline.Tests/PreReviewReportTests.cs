using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>预审报告解析测试：容忍 ```json 代码块、坏回复绝不判成零发现、分级计数正确。</summary>
    public class PreReviewReportTests
    {
        /// <summary>模型回复裹在 ```json 代码块里 → 照常解析出发现。</summary>
        [Fact]
        public void FencedJsonCodeBlockParses()
        {
            var modelText = "```json\n" +
                "{\"发现\":[{\"分级\":\"阻断级\",\"文件\":\"Tools/A.cs\",\"位置\":\"L42\",\"问题\":\"空引用\",\"依据\":\"没判 null\"}]}\n" +
                "```";

            Assert.True(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);
            var finding = Assert.Single(report.Findings);
            Assert.Equal("阻断级", finding.Grade);
            Assert.Equal("Tools/A.cs", finding.File);
            Assert.Equal("L42", finding.Location);
            Assert.Equal("空引用", finding.Issue);
            Assert.Equal("没判 null", finding.Basis);
        }

        /// <summary>坏回复（不是 JSON）→ 判成了=false、零发现、原因写清——绝不许当成「没问题」。</summary>
        [Fact]
        public void GarbageReplyIsNotParsedAndHasNoFindings()
        {
            var modelText = "我看看这段代码……感觉还行，没什么大问题。";

            Assert.False(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Findings);
            Assert.Equal(0, report.BlockingCount);
            Assert.Equal(0, report.SuggestionCount);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>JSON 合法但缺「发现」数组 → 判成了=false，不给零发现。</summary>
        [Fact]
        public void JsonWithoutFindingsArrayIsNotParsed()
        {
            var modelText = "{\"结论\":\"没问题\"}";

            Assert.False(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Findings);
            Assert.Contains("发现", reason);
        }

        /// <summary>发现条目的分级值非法 → 整单解析失败，不把非法分级当发现。</summary>
        [Fact]
        public void IllegalGradeValueIsNotParsed()
        {
            var modelText = "{\"发现\":[{\"分级\":\"严重级\",\"文件\":\"A.cs\",\"位置\":\"L1\",\"问题\":\"问题\",\"依据\":\"依据\"}]}";

            Assert.False(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Findings);
            Assert.Contains("分级", reason);
        }

        /// <summary>分级计数正确：2 条阻断级 + 1 条建议级。</summary>
        [Fact]
        public void GradeCountsAreCorrect()
        {
            var modelText = "{\"发现\":[" +
                "{\"分级\":\"阻断级\",\"文件\":\"A.cs\",\"位置\":\"L1\",\"问题\":\"问题一\",\"依据\":\"依据一\"}," +
                "{\"分级\":\"建议级\",\"文件\":\"B.cs\",\"位置\":\"L2\",\"问题\":\"问题二\",\"依据\":\"依据二\"}," +
                "{\"分级\":\"阻断级\",\"文件\":\"C.cs\",\"位置\":\"L3\",\"问题\":\"问题三\",\"依据\":\"依据三\"}]}";

            Assert.True(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);
            Assert.Equal(3, report.Findings.Count);
            Assert.Equal(2, report.BlockingCount);
            Assert.Equal(1, report.SuggestionCount);
        }

        /// <summary>发现条目允许缺 文件/位置/依据（宽松），但分级与问题必须有。</summary>
        [Fact]
        public void MissingOptionalFieldsStillParse()
        {
            var modelText = "{\"发现\":[{\"分级\":\"建议级\",\"问题\":\"建议加注释\"}]}";

            Assert.True(PreReviewReport.TryParse(modelText, out var report, out var reason));
            Assert.True(report.Parsed);
            var finding = Assert.Single(report.Findings);
            Assert.Equal("建议级", finding.Grade);
            Assert.Equal("", finding.File);
            Assert.Equal("", finding.Location);
            Assert.Equal("建议加注释", finding.Issue);
            Assert.Equal("", finding.Basis);
        }

        /// <summary>空回复 → 判成了=false 且零发现。</summary>
        [Fact]
        public void EmptyReplyIsNotParsed()
        {
            Assert.False(PreReviewReport.TryParse("", out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Findings);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }
}
