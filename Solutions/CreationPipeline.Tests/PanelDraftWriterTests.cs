using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>PanelDraftWriter 把表单字段组装成合规信封写盘的行为测试。</summary>
    public class PanelDraftWriterTests
    {
        /// <summary>一份带全部字段的表单参数写入后，信封能被 InboxEnvelope.TryRead 逐项读回。</summary>
        [Fact]
        public void WrittenEnvelopeReadsBackWithPanelChannelAndAllFields()
        {
            using var workspace = new PoolTestWorkspace();
            var moment = new DateTimeOffset(2026, 8, 21, 10, 30, 0, TimeSpan.FromHours(8));
            var criteria = new List<string> { "登录弹出签到界面", "连续七天有奖励" };

            var path = PanelDraftWriter.Write(
                workspace.Root,
                "面板人",
                "七日签到",
                "系统",
                "用户每天登录领奖励",
                criteria,
                "EP-0003",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["目标"] = "提升日活",
                    ["玩法"] = "每日登录"
                },
                moment);

            var ok = InboxEnvelope.TryRead(path, out var envelope, out var failureReason);

            Assert.True(ok);
            Assert.Equal("", failureReason);
            Assert.Equal("panel", envelope.Channel);
            Assert.Equal("panel-" + moment.ToString("yyyyMMddHHmmssfff"), envelope.RecordIdentifier);
            Assert.Equal(1, envelope.Revision);
            Assert.Equal("面板人", envelope.Submitter);
            Assert.Equal("七日签到", envelope.Fields["标题"].GetString());
            Assert.Equal("系统", envelope.Fields["类型"].GetString());
            Assert.Equal("用户每天登录领奖励", envelope.Fields["描述"].GetString());
            Assert.Equal("EP-0003", envelope.Fields["专项"].GetString());
            Assert.Equal("提升日活", envelope.Fields["目标"].GetString());
            Assert.Equal("每日登录", envelope.Fields["玩法"].GetString());

            var criteriaElement = envelope.Fields["验收标准"];
            Assert.Equal(JsonValueKind.Array, criteriaElement.ValueKind);
            Assert.Equal(2, criteriaElement.GetArrayLength());
            Assert.Equal("登录弹出签到界面", criteriaElement[0].GetString());
            Assert.Equal("连续七天有奖励", criteriaElement[1].GetString());
        }

        /// <summary>空描述、空专项、空附加字段值不出现在「字段」里；非空附加字段照写。</summary>
        [Fact]
        public void EmptyDescriptionEpicAndExtraFieldValuesAreOmitted()
        {
            using var workspace = new PoolTestWorkspace();
            var moment = new DateTimeOffset(2026, 8, 21, 10, 30, 0, TimeSpan.FromHours(8));

            var path = PanelDraftWriter.Write(
                workspace.Root,
                "",
                "标题",
                "系统",
                "",
                new List<string> { "验收一条" },
                "",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["目标"] = "",
                    ["玩法"] = "有值"
                },
                moment);

            InboxEnvelope.TryRead(path, out var envelope, out _);

            Assert.False(envelope.Fields.ContainsKey("描述"));
            Assert.False(envelope.Fields.ContainsKey("专项"));
            Assert.False(envelope.Fields.ContainsKey("目标"));
            Assert.True(envelope.Fields.ContainsKey("玩法"));
            Assert.Equal("有值", envelope.Fields["玩法"].GetString());
        }

        /// <summary>验收标准数组逐项原样进入「字段」，元素顺序不变。</summary>
        [Fact]
        public void AcceptanceCriteriaArrayIsPreserved()
        {
            using var workspace = new PoolTestWorkspace();
            var moment = new DateTimeOffset(2026, 8, 21, 10, 30, 0, TimeSpan.FromHours(8));
            var criteria = new List<string> { "标准一", "标准二", "标准三" };

            var path = PanelDraftWriter.Write(
                workspace.Root,
                "",
                "标题",
                "系统",
                "",
                criteria,
                "",
                new Dictionary<string, string>(StringComparer.Ordinal),
                moment);

            InboxEnvelope.TryRead(path, out var envelope, out _);

            var element = envelope.Fields["验收标准"];
            Assert.Equal(JsonValueKind.Array, element.ValueKind);
            Assert.Equal(3, element.GetArrayLength());
            for (var i = 0; i < criteria.Count; i++)
            {
                Assert.Equal(criteria[i], element[i].GetString());
            }
        }

        /// <summary>文件落在 InboxDirectory 下，文件名全 ASCII、以 -1.json 结尾。</summary>
        [Fact]
        public void WrittenFileLandsInInboxUnderAsciiDashOneJsonName()
        {
            using var workspace = new PoolTestWorkspace();
            var moment = new DateTimeOffset(2026, 8, 21, 10, 30, 0, TimeSpan.FromHours(8));

            var path = PanelDraftWriter.Write(
                workspace.Root,
                "",
                "标题",
                "系统",
                "",
                new List<string> { "验收一条" },
                "",
                new Dictionary<string, string>(StringComparer.Ordinal),
                moment);

            Assert.True(File.Exists(path));
            Assert.StartsWith(workspace.InboxDirectory + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
            var fileName = Path.GetFileName(path);
            Assert.True(IsAllAscii(fileName));
            Assert.EndsWith("-1.json", fileName);
        }

        private static bool IsAllAscii(string text)
        {
            foreach (var character in text)
            {
                if (character > 127)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
