using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>需求文档渲染命令的参数。</summary>
    public sealed class RequirementDocRenderArguments
    {
        /// <summary>要渲染的需求 id，如「REQ-0042」；留空表示池子里全部需求。</summary>
        [Summary("要渲染的需求 id，如 REQ-0042；留空表示池子里全部需求")]
        public string RequirementIdentifier { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>干跑：算出全文但不写盘。</summary>
        [Summary("干跑：算出全文但不写盘")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>需求文档门禁命令的参数。</summary>
    public sealed class RequirementDocGateArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>需求文档命令：doc.render 生成/刷新文档，gate.reqdoc 按规范查六条。</summary>
    public static class RequirementDocCommands
    {
        /// <summary>
        /// 按需求骨架生成或刷新 index.md：补工程负责的 frontmatter 键、补缺掉的必填小节、重生成生成区。
        /// </summary>
        /// <param name="arguments">渲染命令参数。</param>
        [EditorCommand("doc.render")]
        [Summary("按需求骨架生成或刷新需求文档 index.md")]
        public static CommandResult Render(RequirementDocRenderArguments arguments)
        {
            if (!TryResolveRoots(arguments?.RepositoryRoot, arguments?.PoolRoot, out var repositoryRoot, out var poolRoot, out var failure))
            {
                return failure;
            }

            RequirementDocumentSpec specification;
            try
            {
                specification = RequirementDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var identifiers = ResolveIdentifiers(poolRoot, arguments?.RequirementIdentifier);
            if (identifiers.Count == 0)
            {
                return CommandResult.Success("没有需要渲染的需求");
            }

            var isDryRun = arguments != null && arguments.DryRun;
            var lines = new List<string>();
            var changedCount = 0;

            foreach (var identifier in identifiers)
            {
                RequirementDocumentRenderOutcome outcome;
                try
                {
                    outcome = RequirementDocumentRenderer.Render(repositoryRoot, poolRoot, identifier, specification, isDryRun);
                }
                catch (InvalidOperationException exception)
                {
                    return CommandResult.Failure($"{identifier}：{exception.Message}", lines);
                }

                if (outcome.IsChanged)
                {
                    changedCount++;
                }

                var action = outcome.IsCreated ? "新建" : (outcome.IsChanged ? "刷新" : "无变化");
                var addedText = outcome.AddedSections.Count == 0
                    ? ""
                    : "，补小节：" + string.Join("、", outcome.AddedSections);
                lines.Add($"{identifier}　{action}{addedText}　{RelativeTo(repositoryRoot, outcome.DocumentPath)}");
            }

            var head = isDryRun ? "干跑完成" : "渲染完成";
            return CommandResult.Success($"{head}：共 {identifiers.Count} 条需求，有变化 {changedCount} 条", lines);
        }

        /// <summary>
        /// 按需求文档规范查全部 index.md：frontmatter、id、小节顺序、验收标准、媒体、生成区。
        /// </summary>
        /// <param name="arguments">门禁命令参数。</param>
        [EditorCommand("gate.reqdoc")]
        [Summary("需求文档门禁：按基线规范查 index.md 的六条")]
        public static CommandResult Check(RequirementDocGateArguments arguments)
        {
            if (!TryResolveRoots(arguments?.RepositoryRoot, arguments?.PoolRoot, out var repositoryRoot, out var poolRoot, out var failure))
            {
                return failure;
            }

            RequirementDocumentSpec specification;
            try
            {
                specification = RequirementDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = RequirementDocumentChecker.CheckAll(poolRoot, specification);
            if (findings.Count == 0)
            {
                return CommandResult.Success("需求文档门禁通过，问题 0 条");
            }

            return CommandResult.Failure(
                $"需求文档门禁失败，问题 {findings.Count} 条",
                findings.Select(finding => finding.ToDisplayText()).ToList());
        }

        private static bool TryResolveRoots(
            string repositoryRootArgument,
            string poolRootArgument,
            out string repositoryRoot,
            out string poolRoot,
            out CommandResult failure)
        {
            repositoryRoot = "";
            poolRoot = "";
            failure = null;

            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(repositoryRootArgument) ? "." : repositoryRootArgument);
                poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(poolRootArgument) ? "Pools" : poolRootArgument);
            }
            catch (Exception exception)
            {
                failure = CommandResult.Failure($"根目录无法解析为绝对路径：{exception.Message}");
                return false;
            }

            if (!Directory.Exists(poolRoot))
            {
                failure = CommandResult.Failure($"池子根目录不存在：{poolRoot}");
                return false;
            }

            return true;
        }

        private static IReadOnlyList<string> ResolveIdentifiers(string poolRoot, string requirementIdentifier)
        {
            if (!string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                return new[] { requirementIdentifier.Trim() };
            }

            return PoolPaths.EnumerateRequirementIdentifiers(poolRoot);
        }

        private static string RelativeTo(string repositoryRoot, string path)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
