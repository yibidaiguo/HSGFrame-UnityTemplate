using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>模板单向同步命令的参数。</summary>
    public sealed class TemplateSyncArguments
    {
        /// <summary>同步的来源，也就是本轮改动落地的那棵树。</summary>
        [Summary("同步的来源，也就是本轮改动落地的那棵树")]
        public string SourceRoot { get; set; }

        /// <summary>同步的去向，独立的模板仓库根目录。</summary>
        [Summary("同步的去向，独立的模板仓库根目录")]
        public string TargetRoot { get; set; }

        /// <summary>为 true 时只列出差异而不落盘。</summary>
        [Summary("为 true 时只列出差异而不落盘")]
        [DefaultValue(true)]
        public bool PlanOnly { get; set; }
    }

    /// <summary>模板单向同步命令：把来源树的改动同步到模板仓库。方向只有一个，反向同步会让两棵树分叉。</summary>
    public static class TemplateSyncCommand
    {
        // 这些目录是各自仓库的私事或可重建产物，同步它们只会互相打架。
        private static readonly string[] SkippedSegments =
        {
            ".git", "bin", "obj", "Logs", "Build", "Bundles", "Temp",
            "Library", "HybridCLRData", "UserSettings", "_sample_gen", "_tmp.PlaceholderCheck",
        };

        /// <summary>把来源树单向同步到模板仓库，默认只列计划。</summary>
        /// <param name="arguments">同步参数。</param>
        [EditorCommand("template.sync")]
        [Summary("把改动单向同步到模板仓库，默认只列计划不落盘")]
        public static CommandResult Execute(TemplateSyncArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.SourceRoot) || !Directory.Exists(arguments.SourceRoot))
            {
                return CommandResult.Failure(ComposeError(
                    arguments.SourceRoot, "同步来源目录不存在", "把 SourceRoot 指向本轮改动落地的那棵树", "RebuiltRPG"));
            }

            if (string.IsNullOrWhiteSpace(arguments.TargetRoot) || !Directory.Exists(arguments.TargetRoot))
            {
                return CommandResult.Failure(ComposeError(
                    arguments.TargetRoot, "同步去向目录不存在", "把 TargetRoot 指向独立的模板仓库根目录", "GameTemplate"));
            }

            var sourceFiles = EnumerateRelativePaths(arguments.SourceRoot);
            var targetFiles = EnumerateRelativePaths(arguments.TargetRoot);

            var added = new List<string>();
            var updated = new List<string>();

            foreach (var relativePath in sourceFiles)
            {
                var sourcePath = Path.Combine(arguments.SourceRoot, relativePath);
                var targetPath = Path.Combine(arguments.TargetRoot, relativePath);

                if (!File.Exists(targetPath))
                {
                    added.Add(relativePath);
                    continue;
                }

                if (!HasSameContent(sourcePath, targetPath))
                {
                    updated.Add(relativePath);
                }
            }

            // 只在去向侧存在的文件单独报出来，交给人判断：它可能是模板独有的东西，
            // 也可能是来源侧删掉了却忘了同步。同步命令自己不删，删除是不可逆动作。
            var targetOnly = targetFiles.Except(sourceFiles, StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            var lines = new List<string>();
            lines.AddRange(added.Select(path => $"新增：{path}"));
            lines.AddRange(updated.Select(path => $"更新：{path}"));
            lines.AddRange(targetOnly.Select(path => $"仅去向侧有（本命令不动它）：{path}"));

            if (arguments.PlanOnly)
            {
                return CommandResult.Success(
                    $"同步计划：新增 {added.Count} 个，更新 {updated.Count} 个，仅去向侧有 {targetOnly.Count} 个", lines);
            }

            foreach (var relativePath in added.Concat(updated))
            {
                var sourcePath = Path.Combine(arguments.SourceRoot, relativePath);
                var targetPath = Path.Combine(arguments.TargetRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.Copy(sourcePath, targetPath, overwrite: true);
            }

            return CommandResult.Success(
                $"同步完成：新增 {added.Count} 个，更新 {updated.Count} 个，仅去向侧有 {targetOnly.Count} 个（未动）", lines);
        }

        private static IReadOnlyList<string> EnumerateRelativePaths(string root)
        {
            var fullRoot = Path.GetFullPath(root);
            return Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
                .Where(path => !ContainsSkippedSegment(path, fullRoot))
                .Select(path => path.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private static bool ContainsSkippedSegment(string path, string root)
        {
            var relative = path.Substring(root.Length).Replace('\\', '/');
            return relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => SkippedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        // 按内容哈希比而不是按时间戳：复制过一次之后时间戳必然不同，用时间戳会让每次同步都是全量。
        private static bool HasSameContent(string leftPath, string rightPath)
        {
            using var sha256 = SHA256.Create();
            using var leftStream = File.OpenRead(leftPath);
            using var rightStream = File.OpenRead(rightPath);
            return sha256.ComputeHash(leftStream).SequenceEqual(sha256.ComputeHash(rightStream));
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}
