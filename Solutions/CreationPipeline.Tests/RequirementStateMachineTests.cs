using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>RequirementStateMachine 按 schema 转换表判断状态转换合法性的测试。</summary>
    public class RequirementStateMachineTests
    {
        /// <summary>把工作区里「需求」基线 schema 写盘并加载出来供状态机使用。</summary>
        /// <param name="workspace">测试工作区。</param>
        private static PoolSchema LoadRequirementSchema(PoolTestWorkspace workspace)
        {
            workspace.WriteBaselineSchema("需求", PoolTestWorkspace.MinimalRequirementSchema());
            return PoolSchemaLoader.Load(workspace.Root, "需求");
        }

        /// <summary>返回一份没有「状态机」节的基线 schema，模拟工作项这类无状态机实体。</summary>
        private static string NoStateMachineSchema()
        {
            return """
            {
              "schema版本": "1.0.0",
              "实体": "工作项",
              "id模式": "^WI-\\d{4}$",
              "字段": [
                { "名称": "id", "类型": "string", "必填": true },
                { "名称": "标题", "类型": "string", "必填": true }
              ]
            }
            """;
        }

        /// <summary>草稿 → 已确认，由确认人发起，允许。</summary>
        [Fact]
        public void DraftToConfirmedByConfirmerIsAllowed()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "草稿", "已确认", "确认人");

            Assert.True(result.IsAllowed);
        }

        /// <summary>草稿 → 已确认，由引擎发起，拒绝，原因里能看到「确认人」。</summary>
        [Fact]
        public void DraftToConfirmedByEngineIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "草稿", "已确认", "引擎");

            Assert.False(result.IsAllowed);
            Assert.Contains("确认人", result.Reason);
        }

        /// <summary>已确认 → 进行中，由引擎发起，允许。</summary>
        [Fact]
        public void ConfirmedToInProgressByEngineIsAllowed()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "已确认", "进行中", "引擎");

            Assert.True(result.IsAllowed);
        }

        /// <summary>进行中 → 已完成（转换表里没有这一对），拒绝，原因里含「不存在」。</summary>
        [Fact]
        public void InProgressToCompletedIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "进行中", "已完成", "引擎");

            Assert.False(result.IsAllowed);
            Assert.Contains("不存在", result.Reason);
        }

        /// <summary>进行中 → 已作废，由确认人发起，走「*」通配转换，允许。</summary>
        [Fact]
        public void InProgressToObsoleteByConfirmerIsAllowed()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "进行中", "已作废", "确认人");

            Assert.True(result.IsAllowed);
        }

        /// <summary>草稿 → 草稿，状态未发生变化，拒绝，原因里含「未发生变化」。</summary>
        [Fact]
        public void SameStateTransferIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            var schema = LoadRequirementSchema(workspace);

            var result = RequirementStateMachine.CanTransfer(schema, "草稿", "草稿", "确认人");

            Assert.False(result.IsAllowed);
            Assert.Contains("未发生变化", result.Reason);
        }

        /// <summary>schema 没有「状态机」节（工作项那种实体）时，任何转换都被拒绝，原因里含「没有定义状态机」。</summary>
        [Fact]
        public void SchemaWithoutStateMachineIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteBaselineSchema("工作项", NoStateMachineSchema());
            var schema = PoolSchemaLoader.Load(workspace.Root, "工作项");

            var result = RequirementStateMachine.CanTransfer(schema, "草稿", "已确认", "引擎");

            Assert.False(result.IsAllowed);
            Assert.Contains("没有定义状态机", result.Reason);
        }
    }
}
