using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.AgentRunner;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>执行端分派命令 agent.dispatch 的参数。</summary>
    public sealed class AgentDispatchArguments
    {
        /// <summary>角色名：implementer / verifier / operator / explore。</summary>
        [Summary("角色名：implementer / verifier / operator / explore")]
        public string Role { get; set; }

        /// <summary>任务书文件路径（绝对或相对当前工作目录）。</summary>
        [Summary("任务书文件路径（绝对或相对当前工作目录）")]
        public string TaskFile { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>轮数上限（一轮 = 一次 chat/completions 调用）。</summary>
        [Summary("轮数上限（一轮 = 一次 chat/completions 调用）")]
        [DefaultValue(40)]
        public int MaxRounds { get; set; }

        /// <summary>回报正文的字符上限，超出截头留尾；全文总在报告文件里。</summary>
        [Summary("回报正文的字符上限，超出截头留尾；全文总在报告文件里")]
        [DefaultValue(6000)]
        public int MaxReportChars { get; set; }

        /// <summary>模型名覆盖；空串用「执行后端」port 对应 driver 在 local.json 里配的「模型」。</summary>
        [Summary("模型名覆盖；空串用「执行后端」port 对应 driver 在 local.json 里配的「模型」")]
        [DefaultValue("")]
        public string Model { get; set; }

        /// <summary>只组装不发：报出系统提示与任务书的大小，方便核对，不花钱。</summary>
        [Summary("只组装不发：报出系统提示与任务书的大小，方便核对，不花钱")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// 执行端分派命令：角色档案 + 任务书 → OpenAI 兼容执行后端的工具循环 → 回报。
    /// dev-cycle 技能的实现/验证/杂活/定位四个角色都走这一条命令。
    /// </summary>
    public static class AgentCommands
    {
        /// <summary>
        /// 分派一次任务：按角色档案与任务书驱动执行后端，回报正文、轮数、token 与工作区指纹变化。
        /// </summary>
        /// <param name="arguments">分派参数。</param>
        [EditorCommand("agent.dispatch")]
        [Summary("把任务书分派给执行后端（角色档案 + OpenAI 兼容 API 工具循环），回报结果与工作区指纹")]
        public static CommandResult Dispatch(AgentDispatchArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Role))
            {
                return CommandResult.Failure("必须指定 Role（implementer / verifier / operator / explore）");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot ?? ".");
            }
            catch (Exception exception) when (exception is ArgumentException || exception is PathTooLongException)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var taskFilePath = string.IsNullOrWhiteSpace(arguments.TaskFile)
                ? ""
                : Path.GetFullPath(arguments.TaskFile);

            if (arguments.DryRun)
            {
                if (!AgentDispatch.TryAssemble(repositoryRoot, arguments.Role, taskFilePath, out var systemText, out var taskText, out var reason))
                {
                    return CommandResult.Failure(reason);
                }

                return CommandResult.Success("干跑完成，未调用执行后端", new List<string>
                {
                    $"角色={arguments.Role}",
                    $"系统提示 {systemText.Length} 字符（角色档案 + 工具协议）",
                    $"任务书 {taskText.Length} 字符（{taskFilePath}）"
                });
            }

            var result = AgentDispatch.Run(
                repositoryRoot,
                arguments.Role,
                taskFilePath,
                arguments.MaxRounds,
                arguments.MaxReportChars,
                arguments.Model);

            var lines = new List<string>
            {
                $"角色={arguments.Role}  轮数={result.Rounds}  token={result.TotalTokens}",
                $"工作区={(result.WorkspaceChanged ? "变了" : "没变")}"
                    + (arguments.Role is "verifier" or "explore" ? "（只读角色，变了则本轮作废）" : ""),
                $"转录：{result.TranscriptPath}",
                $"报告：{result.ReportPath}"
            };

            if (!result.Succeeded)
            {
                lines.Add("打断原因：" + result.FailureReason);
                if (result.ReportText.Length > 0)
                {
                    lines.Add("—— 收尾轮压出的进展 ——");
                    lines.Add(result.ReportText);
                }

                return CommandResult.Failure("分派没走完：" + result.FailureReason, lines);
            }

            lines.Add("—— 回报正文 ——");
            lines.Add(result.ReportText);
            return CommandResult.Success("分派完成", lines);
        }
    }
}
