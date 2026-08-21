using System;
using System.IO;
using Template.Toolkit.AgentRunner;
using Xunit;

namespace Template.Toolkit.AgentRunnerTests
{
    /// <summary>分派组装测试：角色/任务书/档案缺失的失败路径与正常组装，不碰网络。</summary>
    public class AgentDispatchTests : IDisposable
    {
        private readonly string _root;

        /// <summary>建一棵临时树当仓库根。</summary>
        public AgentDispatchTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentDispatchTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>清掉临时树。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>不认识的角色名直接拒，并列出全部合法角色。</summary>
        [Fact]
        public void UnknownRoleIsRejected()
        {
            var assembled = AgentDispatch.TryAssemble(_root, "hacker", "task.md", out _, out _, out var reason);

            Assert.False(assembled);
            Assert.Contains("implementer", reason);
        }

        /// <summary>角色档案缺失时失败并给出档案路径。</summary>
        [Fact]
        public void MissingRoleFileFails()
        {
            var assembled = AgentDispatch.TryAssemble(_root, "implementer", "task.md", out _, out _, out var reason);

            Assert.False(assembled);
            Assert.Contains("implementer.md", reason);
        }

        /// <summary>档案在、任务书不在：失败且原因点名任务书。</summary>
        [Fact]
        public void MissingTaskFileFails()
        {
            WriteRoleFile("implementer", "# 实现执行端\n");

            var missingTask = Path.Combine(_root, "no-such-task.md");
            var assembled = AgentDispatch.TryAssemble(_root, "implementer", missingTask, out _, out _, out var reason);

            Assert.False(assembled);
            Assert.Contains("no-such-task.md", reason);
        }

        /// <summary>正常组装：系统提示 = 角色档案 + 工具协议，任务书原文照进。</summary>
        [Fact]
        public void AssembleComposesRoleAndToolProtocol()
        {
            WriteRoleFile("verifier", "# 验证执行端\n只跑门禁。\n");
            var taskFile = Path.Combine(_root, "task.md");
            File.WriteAllText(taskFile, "# 验证任务\n跑一遍测试。\n");

            var assembled = AgentDispatch.TryAssemble(_root, "verifier", taskFile, out var systemText, out var taskText, out _);

            Assert.True(assembled);
            Assert.StartsWith("# 验证执行端", systemText);
            Assert.Contains("run_command", systemText);
            Assert.Contains("跑一遍测试", taskText);
        }

        private void WriteRoleFile(string roleName, string content)
        {
            var rolesDirectory = Path.Combine(_root, "Tools", "AgentRunner", "Roles");
            Directory.CreateDirectory(rolesDirectory);
            File.WriteAllText(Path.Combine(rolesDirectory, roleName + ".md"), content);
        }
    }
}
