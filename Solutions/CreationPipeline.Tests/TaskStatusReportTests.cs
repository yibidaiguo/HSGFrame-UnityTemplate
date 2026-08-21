using System;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>TaskStatusReport 文本树渲染的行为测试。</summary>
    public class TaskStatusReportTests
    {
        /// <summary>一份带标题与状态的需求 JSON。</summary>
        private const string RequirementJson = """
        {
          "id": "REQ-0001",
          "标题": "七日签到",
          "状态": "进行中"
        }
        """;

        /// <summary>一份含当前工作项与两份产物的任务状态 JSON。</summary>
        private const string TaskStateJson = """
        {
          "阶段": "执行",
          "子状态": "运行中",
          "当前工作项": "WI-0001-03",
          "关卡待审": null,
          "预算": { "llm上限": 500000, "llm已用": 132000, "生图上限": 60, "生图已用": 18 },
          "产物哈希": { "10-方案.md": "abc", "30-outputs/金币袋.png": "def" }
        }
        """;

        /// <summary>需求与任务状态都在 → 输出含 REQ-0001、标题、阶段：、预算：，且行数是 6。</summary>
        [Fact]
        public void FullReportRendersSixLines()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", RequirementJson);
            workspace.WriteTaskState("REQ-0001", TaskStateJson);

            var output = TaskStatusReport.RenderOne(workspace.RepositoryRoot, workspace.Root, "REQ-0001");

            Assert.Contains("REQ-0001", output);
            Assert.Contains("七日签到", output);
            Assert.Contains("阶段：", output);
            Assert.Contains("预算：", output);
            Assert.Equal(6, output.Split(Environment.NewLine, StringSplitOptions.None).Length);
        }

        /// <summary>只有需求、没有任务状态 → 输出含「尚未开跑」。</summary>
        [Fact]
        public void MissingTaskStateShowsNotStarted()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", RequirementJson);

            var output = TaskStatusReport.RenderOne(workspace.RepositoryRoot, workspace.Root, "REQ-0001");

            Assert.Contains("尚未开跑", output);
        }

        /// <summary>需求文件不存在 → 输出含「需求文件不存在」。</summary>
        [Fact]
        public void MissingRequirementShowsMissingLine()
        {
            using var workspace = new PoolTestWorkspace();

            var output = TaskStatusReport.RenderOne(workspace.RepositoryRoot, workspace.Root, "REQ-0001");

            Assert.Contains("需求文件不存在", output);
        }

        /// <summary>当前工作项为 null → 那一行写「无」。</summary>
        [Fact]
        public void NullWorkItemRendersAsNone()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", RequirementJson);
            workspace.WriteTaskState("REQ-0001", """
            {
              "阶段": "执行",
              "子状态": "运行中",
              "当前工作项": null,
              "关卡待审": null,
              "预算": { "llm上限": 500000, "llm已用": 0, "生图上限": 60, "生图已用": 0 },
              "产物哈希": {}
            }
            """);

            var output = TaskStatusReport.RenderOne(workspace.RepositoryRoot, workspace.Root, "REQ-0001");

            Assert.Contains("当前工作项：无", output);
        }

        /// <summary>RenderAll 在 _Tasks/ 有两个目录时，两个需求 id 都出现在输出里。</summary>
        [Fact]
        public void RenderAllCoversEveryTaskDirectory()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", RequirementJson);
            workspace.WriteTaskState("REQ-0001", TaskStateJson);
            workspace.WriteRequirement("REQ-0002.json", """
            { "id": "REQ-0002", "标题": "水下遗迹", "状态": "已确认" }
            """);
            workspace.WriteTaskState("REQ-0002", TaskStateJson);

            var output = TaskStatusReport.RenderAll(workspace.RepositoryRoot, workspace.Root);

            Assert.Contains("REQ-0001", output);
            Assert.Contains("REQ-0002", output);
        }
    }
}
