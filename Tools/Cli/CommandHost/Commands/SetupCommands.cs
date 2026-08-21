using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>安装命令的参数。</summary>
    public sealed class SetupArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>
    /// 新项目安装命令：setup.init 生成本机配置骨架（不含密钥键），setup.check 全面体检并报下一步。
    /// 目的：新项目「装到能用」的配置有一条统一的路，不靠人记文档，也不给密钥泄露留缝。
    /// </summary>
    public static class SetupCommands
    {
        /// <summary>
        /// 生成 local.json 骨架：从样例复制并剥掉全部密钥键；已存在时不动。
        /// </summary>
        /// <param name="arguments">安装参数。</param>
        [EditorCommand("setup.init")]
        [Summary("生成 local.json 骨架（剥掉密钥键，已存在不动），新项目配置的第一步")]
        public static CommandResult Initialize(SetupArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments, out var failure);
            if (failure != null)
            {
                return failure;
            }

            var message = SetupInspector.InitializeLocalSettings(repositoryRoot);
            return CommandResult.Success(message);
        }

        /// <summary>
        /// 全面体检：密钥文件保护、逐 driver 的密钥键 / 配置节 / 供给状态、Unity 编辑器可达性。
        /// 有红项按失败退出——红项没清完就不算「装好了」。
        /// </summary>
        /// <param name="arguments">安装参数。</param>
        [EditorCommand("setup.check")]
        [Summary("新项目安装体检：逐条报红/黄/绿与下一步，有红项按失败退出")]
        public static CommandResult Check(SetupArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments, out var failure);
            if (failure != null)
            {
                return failure;
            }

            var findings = SetupInspector.Inspect(repositoryRoot);
            var lines = new List<string>(findings.Select(finding => finding.Render()));
            var redCount = findings.Count(finding => finding.Severity == "红");
            var yellowCount = findings.Count(finding => finding.Severity == "黄");
            lines.Add($"—— 红 {redCount} / 黄 {yellowCount} / 绿 {findings.Count - redCount - yellowCount} ——");
            lines.Add("飞书平台侧的三件事（开权限、拉机器人进表、提权到可管理）看 Doc/creation-pipeline-user-setup.md 第三节。");

            return redCount > 0
                ? CommandResult.Failure($"安装体检未通过：{redCount} 个红项", lines)
                : CommandResult.Success("安装体检通过", lines);
        }

        private static string ResolveRoot(SetupArguments arguments, out CommandResult failure)
        {
            failure = null;
            try
            {
                return Path.GetFullPath(arguments?.RepositoryRoot ?? ".");
            }
            catch (Exception exception) when (exception is ArgumentException || exception is PathTooLongException)
            {
                failure = CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
                return null;
            }
        }
    }
}
