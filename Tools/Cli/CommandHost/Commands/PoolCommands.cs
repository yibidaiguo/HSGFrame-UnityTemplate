using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>池子整体校验命令的参数。</summary>
    public sealed class PoolValidateArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>单条需求文件校验命令的参数。</summary>
    public sealed class RequirementValidateArguments
    {
        /// <summary>要校验的需求文件路径。</summary>
        [Summary("要校验的需求文件路径")]
        public string FilePath { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>schema 扩展合法性检查命令的参数。</summary>
    public sealed class SchemaCheckArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>要检查的实体名。</summary>
        [Summary("要检查的实体名")]
        [DefaultValue("需求")]
        public string EntityName { get; set; }
    }

    /// <summary>池子校验命令：pool.validate / req.validate / schema.check 三条命令的 CLI 入口。</summary>
    public static class PoolCommands
    {
        /// <summary>
        /// 校验需求池里的全部需求文件。
        /// </summary>
        /// <param name="arguments">池子整体校验参数。</param>
        [EditorCommand("pool.validate")]
        [Summary("校验需求池里的全部需求文件")]
        public static CommandResult ValidatePool(PoolValidateArguments arguments)
        {
            return RunWithPoolRoot("池子校验", arguments?.PoolRoot, root =>
            {
                var schema = PoolSchemaLoader.Load(root, "需求");
                return RequirementValidator.CheckDirectory(PoolPaths.RequirementsDirectory(root), schema);
            });
        }

        /// <summary>
        /// 按合并后的 schema 校验单个需求文件。
        /// </summary>
        /// <param name="arguments">需求文件校验参数。</param>
        [EditorCommand("req.validate")]
        [Summary("按合并后的 schema 校验单个需求文件")]
        public static CommandResult ValidateRequirement(RequirementValidateArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.FilePath))
            {
                return CommandResult.Failure("参数 FilePath 为必填项");
            }

            var filePath = arguments.FilePath;
            if (!File.Exists(filePath))
            {
                return CommandResult.Failure($"需求文件不存在：{filePath}");
            }

            return RunWithPoolRoot("需求校验", arguments.PoolRoot, root =>
            {
                var schema = PoolSchemaLoader.Load(root, "需求");
                return RequirementValidator.CheckFile(filePath, schema);
            });
        }

        /// <summary>
        /// 检查项目层扩展 schema 是不是基线 schema 的合法扩展。
        /// </summary>
        /// <param name="arguments">schema 检查参数。</param>
        [EditorCommand("schema.check")]
        [Summary("检查项目层扩展 schema 的合法性")]
        public static CommandResult CheckSchema(SchemaCheckArguments arguments)
        {
            return RunWithPoolRoot("schema 扩展合法性", arguments?.PoolRoot, root =>
            {
                var entityName = string.IsNullOrWhiteSpace(arguments.EntityName) ? "需求" : arguments.EntityName;
                return SchemaExtensionValidator.Check(root, entityName);
            });
        }

        // 三条命令共用的处理：解析池子根目录（空白取默认值、相对路径按当前工作目录取绝对）、
        // 目录不存在即失败、把一组 PoolFinding 转成 CommandResult，并接住基线 schema 缺失
        // 抛出的 FileNotFoundException，不让异常穿出命令层。
        private static CommandResult RunWithPoolRoot(
            string checkName,
            string poolRoot,
            Func<string, IReadOnlyList<PoolFinding>> check)
        {
            var root = string.IsNullOrWhiteSpace(poolRoot) ? "Pools" : poolRoot;
            string absoluteRoot;
            try
            {
                absoluteRoot = Path.GetFullPath(root);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 PoolRoot 无法解析为绝对路径：{exception.Message}");
            }

            if (!Directory.Exists(absoluteRoot))
            {
                return CommandResult.Failure($"池子根目录不存在：{absoluteRoot}");
            }

            try
            {
                var findings = check(absoluteRoot);
                return ToFindingResult(checkName, findings);
            }
            catch (FileNotFoundException exception)
            {
                return CommandResult.Failure(exception.Message);
            }
        }

        // 一组发现转结果：零条成功，非零失败并逐行输出 ToDisplayText。
        private static CommandResult ToFindingResult(string checkName, IReadOnlyList<PoolFinding> findings)
        {
            if (findings.Count == 0)
            {
                return CommandResult.Success($"{checkName}通过，问题 0 条");
            }

            var lines = findings.Select(finding => finding.ToDisplayText()).ToList();
            return CommandResult.Failure($"{checkName}失败，问题 {findings.Count} 条", lines);
        }
    }
}
