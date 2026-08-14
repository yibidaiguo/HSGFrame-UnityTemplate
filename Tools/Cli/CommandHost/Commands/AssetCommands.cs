using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>资产重命名命令的参数。</summary>
    public sealed class AssetRenameArguments
    {
        /// <summary>要整理的资产目录。</summary>
        [Summary("要整理的资产目录")]
        public string AssetDirectory { get; set; }

        /// <summary>为 true 时只列出计划而不落盘。</summary>
        [Summary("为 true 时只列出计划而不落盘")]
        [DefaultValue(false)]
        public bool PlanOnly { get; set; }
    }

    /// <summary>资产校验命令的参数。</summary>
    public sealed class AssetValidateArguments
    {
        /// <summary>要校验的资产目录。</summary>
        [Summary("要校验的资产目录")]
        public string AssetDirectory { get; set; }
    }

    /// <summary>资产重命名命令：把目录里的文件名整成规范名。</summary>
    public static class AssetRenameCommand
    {
        /// <summary>按目录的导入规则批量规范化文件名。</summary>
        /// <param name="arguments">重命名参数。</param>
        [EditorCommand("asset.rename")]
        [Summary("按导入规则把目录里的文件名整成规范名")]
        public static CommandResult Execute(AssetRenameArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetDirectory) || !Directory.Exists(arguments.AssetDirectory))
            {
                return CommandResult.Failure($"资产目录不存在：{arguments.AssetDirectory}");
            }

            var rule = AssetImportRuleSet.LoadForDirectory(arguments.AssetDirectory);
            if (rule == null)
            {
                return CommandResult.Failure($"目录及其上级都没有导入规则.json：{arguments.AssetDirectory}");
            }

            var plans = AssetNameNormalizer.PlanDirectory(arguments.AssetDirectory, rule);
            if (plans.Count == 0)
            {
                return CommandResult.Success("目录里的文件名已经全部规范，无需改动");
            }

            var lines = new List<string>();
            foreach (var plan in plans)
            {
                lines.Add($"{Path.GetFileName(plan.OriginalPath)} → {plan.NormalizedFileName}");
                if (arguments.PlanOnly)
                {
                    continue;
                }

                var targetPath = Path.Combine(Path.GetDirectoryName(plan.OriginalPath), plan.NormalizedFileName);
                File.Move(plan.OriginalPath, targetPath);

                // .meta 跟着资产一起改名，否则 Unity 下次打开会当成新资产重新分配 guid。
                var metaPath = plan.OriginalPath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Move(metaPath, targetPath + ".meta");
                }
            }

            var verb = arguments.PlanOnly ? "待重命名" : "已重命名";
            return CommandResult.Success($"{verb} {plans.Count} 个文件", lines);
        }
    }

    /// <summary>资产校验命令：跑四类校验。</summary>
    public static class AssetValidateCommand
    {
        /// <summary>对一个资产目录跑四类校验。</summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("asset.validate")]
        [Summary("对资产目录跑基础合规、引用完整性、冗余孤儿、依赖方向四类校验")]
        public static CommandResult Execute(AssetValidateArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetDirectory) || !Directory.Exists(arguments.AssetDirectory))
            {
                return CommandResult.Failure($"资产目录不存在：{arguments.AssetDirectory}");
            }

            var rule = AssetImportRuleSet.LoadForDirectory(arguments.AssetDirectory);
            if (rule == null)
            {
                return CommandResult.Failure($"目录及其上级都没有导入规则.json：{arguments.AssetDirectory}");
            }

            var findings = AssetValidator.Validate(arguments.AssetDirectory, rule, Array.Empty<string>());
            if (findings.Count == 0)
            {
                return CommandResult.Success("资产校验通过，问题 0 条");
            }

            return CommandResult.Failure(
                $"资产校验失败，问题 {findings.Count} 条",
                findings.Select(finding => finding.ToDisplayText()).ToList());
        }
    }
}
