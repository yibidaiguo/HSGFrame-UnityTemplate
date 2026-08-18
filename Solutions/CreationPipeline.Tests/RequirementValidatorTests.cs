using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>RequirementValidator 对需求 JSON 文件的校验行为测试。</summary>
    public class RequirementValidatorTests
    {
        /// <summary>返回一份完整合法、必填项齐全的「系统」类型需求 JSON，供各测试删字段或改值后使用。</summary>
        private static JsonObject ValidSystemRequirementJson()
        {
            return new JsonObject
            {
                ["id"] = "REQ-0042",
                ["类型"] = "系统",
                ["状态"] = "草稿",
                ["标题"] = "示例需求",
                ["验收标准"] = new JsonArray { "完成新手指引" },
                ["来源"] = new JsonObject { ["提出方"] = "策划" },
                ["关联设计记录"] = new JsonArray(),
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0",
                ["目标"] = "让新玩家完成第一局",
                ["玩法"] = "教学关卡逐步解锁"
            };
        }

        /// <summary>返回一份含全部必填字段与「系统」分类型必填的基线 schema，供校验测试加载。</summary>
        private static string FullRequirementSchema()
        {
            return """
            {
              "schema版本": "1.0.0",
              "实体": "需求",
              "id模式": "^REQ-\\d{4}$",
              "字段": [
                { "名称": "id", "类型": "string", "必填": true },
                { "名称": "类型", "类型": "enum", "枚举": ["系统", "修改", "缺陷"], "必填": true },
                { "名称": "状态", "类型": "enum", "枚举": ["草稿", "已确认", "进行中", "待验收", "已完成", "已作废"], "必填": true },
                { "名称": "标题", "类型": "string", "必填": true },
                { "名称": "验收标准", "类型": "数组", "元素类型": "string", "必填": true, "最少条数": 1 },
                { "名称": "来源", "类型": "对象", "必填": true },
                { "名称": "关联设计记录", "类型": "数组", "元素类型": "string", "必填": true },
                { "名称": "依赖", "类型": "数组", "元素类型": "string", "必填": true },
                { "名称": "锁定", "类型": "bool", "必填": true },
                { "名称": "schema版本", "类型": "string", "必填": true }
              ],
              "分类型必填": {
                "系统": ["目标", "玩法"],
                "修改": ["现状", "期望"],
                "缺陷": ["复现步骤", "期望", "实际"]
              }
            }
            """;
        }

        /// <summary>把工作区里完整需求 schema 写盘并加载出来。</summary>
        /// <param name="workspace">测试工作区。</param>
        private static PoolSchema LoadSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("需求", FullRequirementSchema());
            return PoolSchemaLoader.Load(workspace.Root, "需求");
        }

        /// <summary>拼出需求目录下某文件的完整路径。</summary>
        /// <param name="workspace">测试工作区。</param>
        /// <param name="fileName">需求文件名。</param>
        private static string RequirementPath(PoolTestWorkspace workspace, string fileName)
        {
            return Path.Combine(PoolPaths.RequirementsDirectory(workspace.Root), fileName);
        }

        /// <summary>完整合法的「系统」需求零违规。</summary>
        [Fact]
        public void ValidSystemRequirementHasNoFindings()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteRequirement("REQ-0042.json", ValidSystemRequirementJson().ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.Empty(findings);
        }

        /// <summary>缺必填「标题」时至少一条违规，原因里能看到「标题」。</summary>
        [Fact]
        public void MissingTitleReportsRequiredField()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json.Remove("标题");
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("标题"));
        }

        /// <summary>「类型」写成枚举外的值时至少一条违规，原因里能看到「类型」。</summary>
        [Fact]
        public void UnknownTypeValueReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json["类型"] = "美术";
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("类型"));
        }

        /// <summary>文件名叫 REQ-0042 而 id 写成 REQ-0043 时至少一条违规，原因里含「文件名」。</summary>
        [Fact]
        public void MismatchedIdAndFileNameReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json["id"] = "REQ-0043";
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("文件名"));
        }

        /// <summary>id 写成 REQ-42（位数不对）时至少一条违规，原因里能看到 id 模式。</summary>
        [Fact]
        public void WrongDigitCountIdReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json["id"] = "REQ-42";
            workspace.WriteRequirement("REQ-42.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-42.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("id 模式"));
        }

        /// <summary>「验收标准」是空数组时至少一条违规，原因里能看到「验收标准」。</summary>
        [Fact]
        public void EmptyAcceptanceCriteriaReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json["验收标准"] = new JsonArray();
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("验收标准"));
        }

        /// <summary>「系统」类型缺「玩法」时至少一条违规，原因里含「玩法」。</summary>
        [Fact]
        public void SystemTypeMissingGameplayReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json.Remove("玩法");
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("玩法"));
        }

        /// <summary>「系统」需求里多写缺陷类型的必填「复现步骤」时至少一条违规，原因里含「未在合并 schema 中声明」。</summary>
        [Fact]
        public void UndeclaredReproductionStepsReportsViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            var json = ValidSystemRequirementJson();
            json["复现步骤"] = "先做 A 再做 B";
            workspace.WriteRequirement("REQ-0042.json", json.ToJsonString());

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            Assert.NotEmpty(findings);
            Assert.Contains(findings, f => f.Reason.Contains("未在合并 schema 中声明"));
        }

        /// <summary>文件内容是坏掉的 JSON 时恰好一条违规（语法坏掉时不再往下查）。</summary>
        [Fact]
        public void BrokenJsonReportsSingleViolation()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteRequirement("REQ-0042.json", "{ 这不是 json");

            var findings = RequirementValidator.CheckFile(RequirementPath(workspace, "REQ-0042.json"), schema);

            var finding = Assert.Single(findings);
            Assert.Contains("JSON 语法错误", finding.Reason);
        }

        /// <summary>CheckDirectory 指向不存在的目录时零违规、不抛异常。</summary>
        [Fact]
        public void CheckDirectoryOnMissingDirectoryReturnsEmpty()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);

            var findings = RequirementValidator.CheckDirectory(Path.Combine(workspace.Root, "不存在的目录"), schema);

            Assert.Empty(findings);
        }

        /// <summary>目录里一个好文件一个缺「标题」的坏文件时，违规全部来自坏文件。</summary>
        [Fact]
        public void CheckDirectoryReportsOnlyBadFile()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadSchema(workspace);
            workspace.WriteRequirement("REQ-0042.json", ValidSystemRequirementJson().ToJsonString());
            var badJson = ValidSystemRequirementJson();
            badJson["id"] = "REQ-0043";
            badJson.Remove("标题");
            workspace.WriteRequirement("REQ-0043.json", badJson.ToJsonString());

            var findings = RequirementValidator.CheckDirectory(PoolPaths.RequirementsDirectory(workspace.Root), schema);

            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.Contains("REQ-0043", f.Location));
            Assert.DoesNotContain(findings, f => f.Location.Contains("REQ-0042"));
        }
    }
}
