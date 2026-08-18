using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>出站意图规划（pool.push 的意图侧）的行为测试：事件映射、失败路径、中文不转义与专项认领收件人。</summary>
    public sealed class PoolPushPlannerTests
    {
        /// <summary>测试统一使用的固定时刻，禁止用 DateTimeOffset.Now。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>一份够用的需求文件 JSON：带标题、专项与来源.提交人。</summary>
        private const string RequirementJson = """
        {
          "id": "REQ-0042",
          "类型": "系统",
          "状态": "已确认",
          "标题": "七日签到",
          "验收标准": ["登录弹出签到界面"],
          "来源": { "渠道": "feishu", "记录id": "recXXX", "提交人": "策划甲", "提交时间": "2026-08-18T10:00:00" },
          "专项": "EP-0003"
        }
        """;

        /// <summary>覆盖策划与管理员两类职责的成员表 JSON。</summary>
        private const string MembersJson = """
        [
          { "open_id": "ou_A", "姓名": "策划甲", "默认职责": ["策划"], "确认人": true },
          { "open_id": "ou_Z", "姓名": "老板", "默认职责": ["管理员"], "确认人": true }
        ]
        """;

        /// <summary>事件「待验收」规划成功，出站目录出现文件且回写.状态为待验收。</summary>
        [Fact]
        public void AcceptanceEventPlansEnvelopeWithWriteBack()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0042.json", RequirementJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = PoolPushPlanner.Plan(workspace.RepositoryRoot, workspace.Root, "REQ-0042", "待验收", FixedMoment);

            Assert.True(result.IsPlanned);
            Assert.Equal("", result.FailureReason);
            var filePath = Assert.Single(workspace.ListOutboundFiles());
            Assert.EndsWith("20260818-100000-REQ-0042-待验收.json", filePath);

            var text = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(text);
            var status = document.RootElement.GetProperty("回写").GetProperty("状态").GetString();
            Assert.Equal("待验收", status);
        }

        /// <summary>需求文件不存在时不成案，理由含「需求文件不存在」，出站目录没有文件。</summary>
        [Fact]
        public void MissingRequirementIsNotPlanned()
        {
            using var workspace = new PoolTestWorkspace();

            var result = PoolPushPlanner.Plan(workspace.RepositoryRoot, workspace.Root, "REQ-9999", "待验收", FixedMoment);

            Assert.False(result.IsPlanned);
            Assert.Null(result.Envelope);
            Assert.Contains("需求文件不存在", result.FailureReason);
            Assert.Empty(workspace.ListOutboundFiles());
        }

        /// <summary>不认识的事件名不成案，理由里列出全部可用事件。</summary>
        [Fact]
        public void UnknownEventListsAvailableEvents()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0042.json", RequirementJson);

            var result = PoolPushPlanner.Plan(workspace.RepositoryRoot, workspace.Root, "REQ-0042", "神秘事件", FixedMoment);

            Assert.False(result.IsPlanned);
            Assert.Contains("不认识的出站事件", result.FailureReason);
            Assert.Contains("待验收", result.FailureReason);
            Assert.Contains("停等", result.FailureReason);
        }

        /// <summary>出站信封里的中文没被转义，文件文本里直接含「摘要」二字。</summary>
        [Fact]
        public void EnvelopeKeepsChineseUnescaped()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0042.json", RequirementJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = PoolPushPlanner.Plan(workspace.RepositoryRoot, workspace.Root, "REQ-0042", "待验收", FixedMoment);

            Assert.True(result.IsPlanned);
            var text = File.ReadAllText(Assert.Single(workspace.ListOutboundFiles()));
            Assert.Contains("摘要", text);
        }

        /// <summary>需求挂了专项且专项有认领人时，信封卡片.收件人就是认领人。</summary>
        [Fact]
        public void EpicClaimedRecipientLandsInEnvelope()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0042.json", RequirementJson);
            workspace.WriteMemberDirectory(MembersJson);
            workspace.WriteEpic("EP-0003.json", """
            { "id": "EP-0003", "认领": { "管理员": ["ou_Z"] } }
            """);

            var result = PoolPushPlanner.Plan(workspace.RepositoryRoot, workspace.Root, "REQ-0042", "停等", FixedMoment);

            Assert.True(result.IsPlanned);
            Assert.Equal(RoutingStep.ClaimedInEpic, result.Envelope.Routing.Step);
            Assert.Equal(new List<string> { "ou_Z" }, result.Envelope.Routing.Recipients);

            var text = File.ReadAllText(result.FilePath);
            using var document = JsonDocument.Parse(text);
            var recipients = document.RootElement.GetProperty("卡片").GetProperty("收件人");
            Assert.Equal("ou_Z", recipients[0].GetString());
        }
    }
}
