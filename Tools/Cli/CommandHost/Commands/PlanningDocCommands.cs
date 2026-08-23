using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>渲染模块策划案的参数。</summary>
    public sealed class PlanRenderArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录；留空取 <仓库根>/Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }

        /// <summary>渲染哪个模块；留空表示全渲。</summary>
        [Summary("模块名，与 Scripts/Modules 下的目录同名；留空表示把已有的策划案全渲一遍")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>干跑：算出全文但不写盘。</summary>
        [Summary("干跑：算出全文但不写盘")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>模块策划案门禁的参数。</summary>
    public sealed class PlanGateArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录；留空取 <仓库根>/Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }
    }

    /// <summary>
    /// 模块策划案命令族：渲染与校验。
    ///
    /// 与需求文档那一族（`doc.render` / `gate.reqdoc`）**形状一样但对象不同**：
    /// 那边一条需求一份，做完归档；这边一个模块一份，常驻，随需求验收更新。
    /// </summary>
    public static class PlanningDocCommands
    {
        /// <summary>
        /// 渲染模块策划案的生成区：需求 / 界面与交互 / 配置表结构 / 参考图 / 代码公开面。
        /// 人写区一个字都不碰。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("plan.render")]
        [Summary("渲染模块策划案的生成区（人写区不碰）；留空模块名表示全渲一遍")]
        public static CommandResult Render(PlanRenderArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var repositoryRoot = ResolveRepositoryRoot(arguments.RepositoryRoot);
            var poolRoot = ResolvePoolRoot(repositoryRoot, arguments.PoolRoot);

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var modules = ResolveModules(repositoryRoot, poolRoot, arguments.Module);
            if (modules.Count == 0)
            {
                return CommandResult.Failure(
                    "没有可渲的模块：给一个 Module，或者先在 Pools/Designs/Modules/ 下建一份策划案");
            }

            var lines = new List<string>();
            var changed = 0;
            foreach (var module in modules)
            {
                PlanningDocumentRenderOutcome outcome;
                try
                {
                    outcome = PlanningDocumentRenderer.Render(
                        repositoryRoot, poolRoot, module, specification, arguments.DryRun);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    lines.Add($"{module}：写不动——{exception.Message}");
                    continue;
                }

                lines.Add($"{module}：{(outcome.IsCreated ? "新建" : outcome.IsChanged ? "刷新" : "无变化")}"
                    + $"　{Relative(repositoryRoot, outcome.DocumentPath)}");
                foreach (var note in outcome.Notes)
                {
                    lines.Add("  " + note);
                }

                if (outcome.IsChanged)
                {
                    changed++;
                }
            }

            return CommandResult.Success(
                $"模块策划案渲了 {modules.Count} 份，动了 {changed} 份"
                    + (arguments.DryRun ? "（干跑，没写盘）" : ""),
                lines);
        }

        /// <summary>
        /// 模块策划案门禁：必备键、必填小节、生成区没被手改、配置表声明可解析。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("gate.plandoc")]
        [Summary("模块策划案门禁：必备键、必填小节、生成区未被手改、配置表声明可解析")]
        public static CommandResult Gate(PlanGateArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var repositoryRoot = ResolveRepositoryRoot(arguments.RepositoryRoot);
            var poolRoot = ResolvePoolRoot(repositoryRoot, arguments.PoolRoot);

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = PlanningDocumentChecker.Check(repositoryRoot, poolRoot, specification);
            var lines = new List<string>();
            foreach (var finding in findings)
            {
                lines.Add(finding.ToDisplayText());
            }

            var count = ModulePlanDirectories(poolRoot).Count;
            return findings.Count == 0
                ? CommandResult.Success($"模块策划案门禁（策划案 {count} 份）通过，问题 0 条", lines)
                : CommandResult.Failure($"模块策划案门禁不通过，问题 {findings.Count} 条", lines);
        }

        /// <summary>要渲哪些模块：给了名字就只渲那一个，没给就渲已经建过策划案的全部。</summary>
        private static IReadOnlyList<string> ResolveModules(string repositoryRoot, string poolRoot, string module)
        {
            if (!string.IsNullOrWhiteSpace(module))
            {
                return new[] { module.Trim() };
            }

            return ModulePlanDirectories(poolRoot);
        }

        /// <summary>池子里已经有策划案的模块名，按名字排序。</summary>
        private static IReadOnlyList<string> ModulePlanDirectories(string poolRoot)
        {
            var root = PoolPaths.ModulePlanRoot(poolRoot);
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var directory in Directory.GetDirectories(root))
            {
                names.Add(Path.GetFileName(directory));
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static string ResolveRepositoryRoot(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Directory.GetCurrentDirectory() : value;
        }

        private static string ResolvePoolRoot(string repositoryRoot, string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Path.Combine(repositoryRoot, "Pools") : value;
        }

        private static string Relative(string repositoryRoot, string path)
        {
            return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        }
    }
}
