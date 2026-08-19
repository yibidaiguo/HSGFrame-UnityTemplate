using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;
using Template.Toolkit.Gates;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>供给对账门禁命令的参数。</summary>
    public sealed class GateProvisionArguments
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

    /// <summary>下游边界门禁命令的参数。</summary>
    public sealed class GateBridgeBoundaryArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>层边界门禁命令的参数。</summary>
    public sealed class GateLayerBoundaryArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>Unity 资产根目录，相对当前工作目录。</summary>
        [Summary("Unity 资产根目录，相对当前工作目录")]
        [DefaultValue("UnityProject/Assets")]
        public string UnityAssetsDirectory { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>创作管线的三道门禁命令：供给对账、下游边界、层边界。</summary>
    public static class PipelineGateCommands
    {
        /// <summary>
        /// 跑供给对账：扫全部 driver，逐个把指纹与当前 schema/设计池哈希对账。
        /// </summary>
        /// <param name="arguments">供给对账门禁参数。</param>
        [EditorCommand("gate.provision")]
        [Summary("供给对账门禁：schema 哈希与设计池哈希要和指纹一致")]
        public static CommandResult Provision(GateProvisionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.PoolRoot) ? "Pools" : arguments.PoolRoot);
            var report = ProvisionReconciler.Reconcile(repositoryRoot, poolRoot);

            // ToResult 只吃 GateFinding，把管线侧的 PoolFinding 四字段一一映射过去。
            var gateFindings = report.Findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult(
                $"供给对账门禁（driver {report.DriverNames.Count} 个，其中已供给 {report.ProvisionedCount} 个）",
                gateFindings);
        }

        /// <summary>
        /// 跑下游边界检查：引擎与管线代码里不许出现 driver 名，driver 名只能是运行时参数。
        /// </summary>
        /// <param name="arguments">下游边界门禁参数。</param>
        [EditorCommand("gate.bridgeboundary")]
        [Summary("下游边界门禁：driver 名不许焊进引擎代码")]
        public static CommandResult BridgeBoundary(GateBridgeBoundaryArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, repositoryRoot));

            // 扫描根写死这三处：引擎、命令层与面板，都是引擎/管线层代码。
            string[] scanRoots = { "Tools/CreationPipeline", "Tools/Cli", "Tools/Dashboard" };
            var driverNames = BridgeBoundaryChecker.ReadDriverNames(repositoryRoot);
            var findings = BridgeBoundaryChecker.Check(repositoryRoot, scanRoots, configuration);
            return GateCommandSupport.ToResult(
                $"下游边界门禁（driver {driverNames.Count} 个：{string.Join("、", driverNames)}）",
                findings);
        }

        /// <summary>
        /// 跑层边界检查：协作/过程数据不许落 Unity 资产树，游戏代码不许引用协作层路径。
        /// </summary>
        /// <param name="arguments">层边界门禁参数。</param>
        [EditorCommand("gate.layerboundary")]
        [Summary("层边界门禁：产品层零协作感知")]
        public static CommandResult LayerBoundary(GateLayerBoundaryArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, repositoryRoot));

            var unityAssetsDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.UnityAssetsDirectory)
                ? "UnityProject/Assets"
                : arguments.UnityAssetsDirectory);
            var findings = LayerBoundaryChecker.Check(repositoryRoot, unityAssetsDirectory, configuration);
            return GateCommandSupport.ToResult("层边界门禁", findings);
        }
    }
}
