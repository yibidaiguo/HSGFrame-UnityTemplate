using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>InboxEnvelope 从 JSON 文件解析信封的行为测试。</summary>
    public class InboxEnvelopeTests
    {
        /// <summary>一份完整合法的信封 JSON，覆盖全部属性。</summary>
        private const string SampleEnvelopeJson = """
        {
          "渠道": "feishu",
          "记录id": "recABC123",
          "修订": 3,
          "提交人": "策划甲",
          "提交时间": "2026-08-18T10:00:00",
          "关联需求": null,
          "字段": { "类型": "系统", "标题": "七日签到", "验收标准": ["登录弹出签到界面"] }
        }
        """;

        /// <summary>把一段 JSON 写入收件箱并返回完整路径。</summary>
        /// <param name="workspace">测试工作区。</param>
        /// <param name="fileName">信封文件名。</param>
        /// <param name="json">信封 JSON 内容。</param>
        private static string WriteInboxEnvelope(PoolTestWorkspace workspace, string fileName, string json)
        {
            workspace.WriteInbox(fileName, json);
            return Path.Combine(workspace.InboxDirectory, fileName);
        }

        /// <summary>正常信封读得出全部属性，来源文件路径原样带回。</summary>
        [Fact]
        public void WellFormedEnvelopeReadsAllProperties()
        {
            using var workspace = new PoolTestWorkspace();
            var path = WriteInboxEnvelope(workspace, "feishu-recABC123-3.json", SampleEnvelopeJson);

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.True(ok);
            Assert.Equal("", failureReason);
            Assert.Equal("feishu", envelope.Channel);
            Assert.Equal("recABC123", envelope.RecordIdentifier);
            Assert.Equal(3, envelope.Revision);
            Assert.Equal("策划甲", envelope.Submitter);
            Assert.Equal("2026-08-18T10:00:00", envelope.SubmitTime);
            Assert.Null(envelope.LinkedRequirement);
            Assert.Equal(3, envelope.Fields.Count);
            Assert.Equal("系统", envelope.Fields["类型"].GetString());
            Assert.Equal("七日签到", envelope.Fields["标题"].GetString());
            Assert.Equal(path, envelope.SourceFilePath);
        }

        /// <summary>JSON 语法错误时返回 false，原因里能看到「JSON 语法错误」。</summary>
        [Fact]
        public void BrokenJsonReturnsFalse()
        {
            using var workspace = new PoolTestWorkspace();
            var path = WriteInboxEnvelope(workspace, "feishu-recABC123-3.json", "{ 这不是 json");

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.False(ok);
            Assert.Null(envelope);
            Assert.Contains("JSON 语法错误", failureReason);
        }

        /// <summary>缺「记录id」时返回 false，原因里能看到「记录id」。</summary>
        [Fact]
        public void MissingRecordIdentifierReturnsFalse()
        {
            using var workspace = new PoolTestWorkspace();
            var json = """
            { "渠道": "feishu", "修订": 3, "字段": { "标题": "七日签到" } }
            """;
            var path = WriteInboxEnvelope(workspace, "feishu-recABC123-3.json", json);

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.False(ok);
            Assert.Contains("记录id", failureReason);
        }

        /// <summary>「修订」是字符串而非整数时返回 false，原因里能看到「修订」。</summary>
        [Fact]
        public void NonIntegerRevisionReturnsFalse()
        {
            using var workspace = new PoolTestWorkspace();
            var json = """
            { "渠道": "feishu", "记录id": "recABC123", "修订": "3", "字段": { "标题": "七日签到" } }
            """;
            var path = WriteInboxEnvelope(workspace, "feishu-recABC123-3.json", json);

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.False(ok);
            Assert.Contains("修订", failureReason);
        }

        /// <summary>「字段」是数组而非对象时返回 false，原因里能看到「字段」。</summary>
        [Fact]
        public void NonObjectFieldsReturnsFalse()
        {
            using var workspace = new PoolTestWorkspace();
            var json = """
            { "渠道": "feishu", "记录id": "recABC123", "修订": 3, "字段": ["类型", "标题"] }
            """;
            var path = WriteInboxEnvelope(workspace, "feishu-recABC123-3.json", json);

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.False(ok);
            Assert.Contains("字段", failureReason);
        }
    }
}
