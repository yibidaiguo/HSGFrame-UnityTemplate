using System;
using System.ComponentModel;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>配置表桥接命令的参数。</summary>
    public sealed class ConfigArguments
    {
        /// <summary>要处理的表名。</summary>
        [Summary("要处理的表名")]
        public string TableName { get; set; }

        /// <summary>配置根目录。</summary>
        [Summary("配置根目录")]
        [DefaultValue("Template/Config")]
        public string ConfigRoot { get; set; }
    }

    /// <summary>配置表桥接命令：sync / apply / validate 三个流程的 CLI 入口。</summary>
    public static class ConfigCommands
    {
        /// <summary>
        /// 把 Excel 配置表同步到镜像 JSON（Excel 为准）。
        /// </summary>
        /// <param name="arguments">同步参数。</param>
        [EditorCommand("config.sync")]
        [Summary("把 Excel 配置表同步到镜像 JSON（Excel 为准）")]
        public static CommandResult Sync(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Sync(arguments.TableName));
        }

        /// <summary>
        /// 把镜像 JSON 回写到 Excel（先校验基线哈希，不一致拒绝）。
        /// </summary>
        /// <param name="arguments">应用参数。</param>
        [EditorCommand("config.apply")]
        [Summary("把镜像 JSON 回写到 Excel（先校验基线哈希）")]
        public static CommandResult Apply(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Apply(arguments.TableName));
        }

        /// <summary>
        /// 按 schema 逐字段校验镜像 JSON。
        /// </summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("config.validate")]
        [Summary("按 schema 逐字段校验镜像 JSON")]
        public static CommandResult Validate(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Validate(arguments.TableName));
        }

        private static CommandResult Run(ConfigArguments arguments, Func<ConfigSyncService, ConfigOperationResult> operation)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.TableName))
            {
                return CommandResult.Failure("参数 TableName 为必填项");
            }

            var configRoot = string.IsNullOrWhiteSpace(arguments.ConfigRoot)
                ? "Template/Config"
                : arguments.ConfigRoot;
            var service = new ConfigSyncService(configRoot);
            var result = operation(service);

            return result.IsSuccess
                ? CommandResult.Success(result.Message, result.Details)
                : CommandResult.Failure(result.Message, result.Details);
        }
    }
}
