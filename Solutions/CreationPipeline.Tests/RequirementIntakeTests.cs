using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>RequirementIntake 入站主流程的行为测试：入池、幂等、拒收、锁定分流与取号。</summary>
    public class RequirementIntakeTests
    {
        /// <summary>测试统一使用的固定时刻，禁止用 DateTimeOffset.Now。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>覆盖候选需求全部键的完整 schema：工程字段带所有权标注，分类型必填三类。</summary>
        private const string SchemaJson = """
        {
          "schema版本": "1.2.0",
          "实体": "需求",
          "id模式": "^REQ-\\d{4}$",
          "字段": [
            { "名称": "id", "类型": "string", "必填": true },
            { "名称": "类型", "类型": "enum", "枚举": ["系统", "修改", "缺陷"], "必填": true, "所有权": "策划端" },
            { "名称": "状态", "类型": "enum", "枚举": ["草稿", "已确认", "进行中", "待验收", "已完成", "已作废"], "必填": true, "所有权": "工程" },
            { "名称": "标题", "类型": "string", "必填": true, "所有权": "策划端" },
            { "名称": "描述", "类型": "string", "必填": false, "所有权": "策划端" },
            { "名称": "验收标准", "类型": "数组", "元素类型": "string", "必填": true, "最少条数": 1, "所有权": "策划端" },
            { "名称": "专项", "类型": "string", "必填": false, "可空": true, "所有权": "策划端" },
            { "名称": "来源", "类型": "对象", "必填": true, "所有权": "工程" },
            { "名称": "关联设计记录", "类型": "数组", "元素类型": "string", "必填": true, "所有权": "工程" },
            { "名称": "父需求", "类型": "string", "必填": false, "可空": true, "所有权": "工程" },
            { "名称": "依赖", "类型": "数组", "元素类型": "string", "必填": true, "所有权": "工程" },
            { "名称": "锁定", "类型": "bool", "必填": true, "所有权": "工程" },
            { "名称": "schema版本", "类型": "string", "必填": true, "所有权": "工程" },
            { "名称": "同步", "类型": "对象", "必填": false, "所有权": "工程" },
            { "名称": "冲突", "类型": "数组", "元素类型": "string", "必填": false, "所有权": "工程" }
          ],
          "分类型必填": {
            "系统": ["目标", "玩法"],
            "修改": ["现状", "期望"],
            "缺陷": ["复现步骤", "期望", "实际"]
          },
          "状态机": {
            "初始状态": "草稿",
            "转换": []
          }
        }
        """;

        /// <summary>把基线需求 schema 写盘并加载出 PoolSchema。</summary>
        /// <param name="workspace">测试工作区。</param>
        private static PoolSchema LoadSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("需求", SchemaJson);
            return PoolSchemaLoader.Load(workspace.Root, "需求");
        }

        /// <summary>拼一份信封 JSON 文本。</summary>
        /// <param name="channel">渠道名。</param>
        /// <param name="recordId">记录 id。</param>
        /// <param name="revision">修订号。</param>
        /// <param name="fields">策划端字段对象。</param>
        private static string EnvelopeJson(string channel, string recordId, int revision, JsonObject fields)
        {
            return new JsonObject
            {
                ["渠道"] = channel,
                ["记录id"] = recordId,
                ["修订"] = revision,
                ["提交人"] = "策划甲",
                ["提交时间"] = "2026-08-18T10:00:00",
                ["字段"] = fields
            }.ToJsonString();
        }

        /// <summary>一份校验能通过的「系统」类型字段集合。</summary>
        /// <param name="title">标题值。</param>
        private static JsonObject ValidFields(string title = "七日签到")
        {
            return new JsonObject
            {
                ["类型"] = "系统",
                ["标题"] = title,
                ["验收标准"] = new JsonArray { "登录弹出签到界面" },
                ["目标"] = "提升留存",
                ["玩法"] = "每日签到领取奖励"
            };
        }

        /// <summary>合格新记录入池为 REQ-0001，工程字段按规则填默认值。</summary>
        [Fact]
        public void ValidNewRecordIsAccepted()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteInbox("feishu-recABC123-3.json", EnvelopeJson("feishu", "recABC123", 3, ValidFields()));

            var outcome = Assert.Single(RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment));

            Assert.Equal(IntakeDecision.Accepted, outcome.Decision);
            Assert.Equal("REQ-0001", outcome.RequirementIdentifier);
            Assert.True(workspace.RequirementExists("REQ-0001.json"));

            var json = JsonNode.Parse(workspace.ReadRequirement("REQ-0001.json")) as JsonObject;
            Assert.Equal("recABC123", json["来源"]["记录id"].GetValue<string>());
            Assert.Equal("草稿", json["状态"].GetValue<string>());
            Assert.False(json["锁定"].GetValue<bool>());
            Assert.Equal("1.2.0", json["schema版本"].GetValue<string>());
        }

        /// <summary>同一份 Inbox 连跑两次，第二次整条跳过，需求目录里仍只有一个文件。</summary>
        [Fact]
        public void SecondRunSkipsSameRecord()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteInbox("feishu-recABC123-3.json", EnvelopeJson("feishu", "recABC123", 3, ValidFields()));

            var firstOutcome = Assert.Single(RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment));
            var secondOutcome = Assert.Single(RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment));

            Assert.Equal(IntakeDecision.Accepted, firstOutcome.Decision);
            Assert.Equal(IntakeDecision.Skipped, secondOutcome.Decision);

            var requirementFiles = Directory.GetFiles(PoolPaths.RequirementsDirectory(workspace.Root), "*.json");
            Assert.Single(requirementFiles);
        }

        /// <summary>缺「验收标准」的记录拒收：拒收单落 _Generated/拒收，理由数组非空，需求目录没有新文件。</summary>
        [Fact]
        public void MissingAcceptanceCriteriaIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var fields = ValidFields();
            fields.Remove("验收标准");
            workspace.WriteInbox("feishu-recABC123-3.json", EnvelopeJson("feishu", "recABC123", 3, fields));

            var outcome = Assert.Single(RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment));

            Assert.Equal(IntakeDecision.Rejected, outcome.Decision);
            var noticePath = Path.Combine(workspace.RepositoryRoot, "_Generated", "拒收", "feishu-recABC123-3.json");
            Assert.True(File.Exists(noticePath));
            var notice = JsonNode.Parse(File.ReadAllText(noticePath)) as JsonObject;
            Assert.NotEmpty(notice["理由"] as JsonArray);
            Assert.False(workspace.RequirementExists("REQ-0001.json"));
        }

        /// <summary>拒收单文件文本里的中文没有被转义成 \uXXXX，能直接看到「原因」两个字。</summary>
        [Fact]
        public void RejectionNoticeKeepsChineseUnescaped()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var fields = ValidFields();
            fields.Remove("验收标准");
            workspace.WriteInbox("feishu-recABC123-3.json", EnvelopeJson("feishu", "recABC123", 3, fields));

            RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);

            var noticePath = Path.Combine(workspace.RepositoryRoot, "_Generated", "拒收", "feishu-recABC123-3.json");
            var text = File.ReadAllText(noticePath);
            Assert.Contains("原因", text);
            Assert.DoesNotContain("\\u", text);
        }

        /// <summary>信封字段里写工程字段「状态」→ 拒收，理由里含「归工程所有」。</summary>
        [Fact]
        public void EnvelopeWithEngineeringFieldIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var fields = ValidFields();
            fields["状态"] = "已确认";
            workspace.WriteInbox("feishu-recABC123-3.json", EnvelopeJson("feishu", "recABC123", 3, fields));

            var outcome = Assert.Single(RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment));

            Assert.Equal(IntakeDecision.Rejected, outcome.Decision);
            Assert.Contains(outcome.Findings, finding => finding.Reason.Contains("归工程所有"));
            Assert.False(workspace.RequirementExists("REQ-0001.json"));
        }

        /// <summary>未锁定需求的更高修订 → Updated：标题换成新值，来源.修订变成新修订。</summary>
        [Fact]
        public void HigherRevisionOnUnlockedRequirementUpdates()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteInbox("feishu-recABC123-1.json", EnvelopeJson("feishu", "recABC123", 1, ValidFields("七日签到")));
            RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);

            workspace.WriteInbox("feishu-recABC123-2.json", EnvelopeJson("feishu", "recABC123", 2, ValidFields("七日签到V2")));
            var outcomes = RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);
            var outcome = Assert.Single(outcomes, item => item.Decision == IntakeDecision.Updated);

            Assert.Equal("REQ-0001", outcome.RequirementIdentifier);
            var json = JsonNode.Parse(workspace.ReadRequirement("REQ-0001.json")) as JsonObject;
            Assert.Equal("七日签到V2", json["标题"].GetValue<string>());
            Assert.Equal(2, json["来源"]["修订"].GetValue<int>());
        }

        /// <summary>已锁定需求的更高修订 → Diverted：需求文件本身不变，变更目录有时间戳文件与累积.json。</summary>
        [Fact]
        public void HigherRevisionOnLockedRequirementDiverts()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteInbox("feishu-recABC123-1.json", EnvelopeJson("feishu", "recABC123", 1, ValidFields("七日签到")));
            RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);

            // 手工把已入池需求置为锁定，模拟需求进入锁定态。
            var requirementPath = Path.Combine(PoolPaths.RequirementsDirectory(workspace.Root), "REQ-0001.json");
            var requirement = JsonNode.Parse(File.ReadAllText(requirementPath)) as JsonObject;
            requirement["锁定"] = true;
            File.WriteAllText(requirementPath, requirement.ToJsonString());

            workspace.WriteInbox("feishu-recABC123-2.json", EnvelopeJson("feishu", "recABC123", 2, ValidFields("七日签到V2")));
            var outcomes = RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);
            var outcome = Assert.Single(outcomes, item => item.Decision == IntakeDecision.Diverted);

            Assert.Equal("REQ-0001", outcome.RequirementIdentifier);

            // 需求文件本身没变：标题仍是旧值，锁定保持 true。
            var after = JsonNode.Parse(workspace.ReadRequirement("REQ-0001.json")) as JsonObject;
            Assert.Equal("七日签到", after["标题"].GetValue<string>());
            Assert.True(after["锁定"].GetValue<bool>());

            // 变更目录下有固定时刻对应的时间戳文件与累积.json。
            var changeDirectory = Path.Combine(workspace.RepositoryRoot, "_Tasks", "REQ-0001", "变更");
            Assert.True(File.Exists(Path.Combine(changeDirectory, "20260818-100000.json")));
            Assert.True(File.Exists(Path.Combine(changeDirectory, "累积.json")));

            var accumulated = JsonNode.Parse(File.ReadAllText(Path.Combine(changeDirectory, "累积.json"))) as JsonObject;
            var fieldChanges = accumulated["字段改动"] as JsonObject;
            Assert.NotNull(fieldChanges["标题"]);
        }

        /// <summary>一轮内两条不同记录的新需求取到 REQ-0001 与 REQ-0002 两个不同号。</summary>
        [Fact]
        public void TwoDistinctRecordsGetDistinctIdentifiers()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteInbox("feishu-recABC123-1.json", EnvelopeJson("feishu", "recABC123", 1, ValidFields("签到A")));
            workspace.WriteInbox("feishu-recDEF456-1.json", EnvelopeJson("feishu", "recDEF456", 1, ValidFields("签到B")));

            var outcomes = RequirementIntake.Run(workspace.RepositoryRoot, workspace.Root, schema, FixedMoment);

            Assert.Equal(2, outcomes.Count);
            Assert.All(outcomes, item => Assert.Equal(IntakeDecision.Accepted, item.Decision));
            Assert.Contains(outcomes, item => item.RequirementIdentifier == "REQ-0001");
            Assert.Contains(outcomes, item => item.RequirementIdentifier == "REQ-0002");
            Assert.True(workspace.RequirementExists("REQ-0001.json"));
            Assert.True(workspace.RequirementExists("REQ-0002.json"));
        }
    }
}
