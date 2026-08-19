using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>下游供给命令的参数。</summary>
    public sealed class BridgeProvisionArguments
    {
        /// <summary>要供给的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要供给的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写，列出将要生成的文件。</summary>
        [Summary("只算不写，列出将要生成的文件")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>下游供给命令：bridge.provision，一次产出建表描述、专项表、校验错误文案、助手配置包与指纹。</summary>
    public static class BridgeCommands
    {
        /// <summary>
        /// 跑一次下游供给：读 driver 自述与合并 schema，产出全部供给产物；干跑时只列不写。
        /// </summary>
        /// <param name="arguments">供给命令参数。</param>
        [EditorCommand("bridge.provision")]
        [Summary("产出下游供给的全部产物：建表描述、专项表、校验错误文案、助手配置包与指纹")]
        public static CommandResult Provision(BridgeProvisionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，例如 --driver feishu");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string poolRoot;
            try
            {
                poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.PoolRoot) ? "Pools" : arguments.PoolRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 PoolRoot 无法解析为绝对路径：{exception.Message}");
            }

            var isDryRun = arguments.DryRun;

            ProvisionOutcome outcome;
            try
            {
                outcome = BridgeProvisioner.Run(repositoryRoot, poolRoot, arguments.Driver, isDryRun);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var lines = new List<string>();
            var headLine = isDryRun ? "干跑完成" : "供给完成";
            lines.Add($"{headLine}：driver={outcome.DriverName} 干跑={(isDryRun ? "是" : "否")}");
            lines.Add($"schema 哈希={FirstTwelve(outcome.SchemaHash)}  设计池汇总哈希={FirstTwelve(outcome.DesignDigestHash)}");

            var filePrefix = isDryRun ? "将生成：" : "产物：";
            foreach (var file in outcome.ProducedFiles)
            {
                lines.Add($"{filePrefix}{RelativeTo(repositoryRoot, file)}");
            }

            lines.Add($"共 {outcome.ProducedFiles.Count} 个产物");
            return CommandResult.Success($"共 {outcome.ProducedFiles.Count} 个产物", lines);
        }

        /// <summary>取哈希的前 12 位；文本不足 12 位时原样返回。</summary>
        private static string FirstTwelve(string text)
        {
            return text.Length <= 12 ? text : text.Substring(0, 12);
        }

        /// <summary>把绝对路径转成相对仓库根的路径；无法相对化时原样返回。</summary>
        private static string RelativeTo(string basePath, string fullPath)
        {
            var relative = Path.GetRelativePath(basePath, fullPath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative;
        }
    }
}
