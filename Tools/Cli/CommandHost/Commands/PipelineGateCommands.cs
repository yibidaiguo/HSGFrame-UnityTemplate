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

    /// <summary>资产规格门禁命令的参数。</summary>
    public sealed class GateAssetSpecArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id；空表示全扫。</summary>
        [Summary("需求 id；空表示全扫")]
        [DefaultValue("")]
        public string Requirement { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string Module { get; set; }
    }

    /// <summary>资产规格门禁命令：资产请求的规格与落点必须符合资产规格数据。</summary>
    public static class GateAssetSpecCommand
    {
        /// <summary>
        /// 跑资产规格门禁：逐份资产请求核对资产类型、落点、命名与规格；不传 Requirement 时全扫。
        /// </summary>
        /// <param name="arguments">资产规格门禁参数。</param>
        [EditorCommand("gate.assetspec")]
        [Summary("资产规格门禁：资产请求的规格与落点必须符合资产规格数据")]
        public static CommandResult Execute(GateAssetSpecArguments arguments)
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

            var catalog = AssetSpecCatalog.Load(repositoryRoot, arguments.Module);
            var findings = string.IsNullOrWhiteSpace(arguments.Requirement)
                ? AssetSpecInspector.InspectAll(repositoryRoot, arguments.Module)
                : AssetSpecInspector.Inspect(repositoryRoot, arguments.Requirement, arguments.Module);

            var gateFindings = findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult($"资产规格门禁（资产类型 {catalog.Types.Count} 个）", gateFindings);
        }
    }

    /// <summary>放行策略门禁命令的参数。</summary>
    public sealed class GateReleaseArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }
    }

    /// <summary>放行策略门禁命令：三层放行策略数据的合法性。</summary>
    public static class GateReleaseCommand
    {
        /// <summary>
        /// 跑放行策略门禁：三层就近合并，把合并过程中发现的违规（放宽被拒、非法值、
        /// 基线独有键被下层写等）一次报出。
        /// </summary>
        /// <param name="arguments">放行策略门禁命令参数。</param>
        [EditorCommand("gate.release")]
        [Summary("放行策略门禁：三层策略数据的合法性")]
        public static CommandResult Execute(GateReleaseArguments arguments)
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

            var catalog = ReleasePolicyCatalog.Load(repositoryRoot, arguments.ModuleName);
            var gateFindings = catalog.Findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult($"放行策略门禁（策略键 {catalog.Policies.Count} 条）", gateFindings);
        }
    }

    /// <summary>配方门禁命令的参数。</summary>
    public sealed class GateRecipeArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>配方门禁命令：每个生图 driver 的配方映射与依赖清单静态合法性。</summary>
    public static class GateRecipeCommand
    {
        /// <summary>
        /// 跑配方门禁：扫 Bridges/ 下每个含 driver.json 的目录，逐个配方核对映射、锚点与依赖声明。
        /// </summary>
        /// <param name="arguments">配方门禁命令参数。</param>
        [EditorCommand("gate.recipe")]
        [Summary("配方门禁：配方映射与依赖清单的静态合法性")]
        public static CommandResult Execute(GateRecipeArguments arguments)
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

            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            var driverNames = new List<string>();
            if (Directory.Exists(bridgesDirectory))
            {
                foreach (var directoryPath in Directory.EnumerateDirectories(bridgesDirectory))
                {
                    if (File.Exists(Path.Combine(directoryPath, "driver.json")))
                    {
                        driverNames.Add(Path.GetFileName(directoryPath));
                    }
                }

                driverNames.Sort(StringComparer.Ordinal);
            }

            var findings = new List<PoolFinding>();
            var recipeCount = 0;
            foreach (var driverName in driverNames)
            {
                recipeCount += RecipeDefinition.DiscoverNames(repositoryRoot, driverName).Count;
                findings.AddRange(RecipeInspector.Inspect(repositoryRoot, driverName));
            }

            var gateFindings = findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult($"配方门禁（driver {driverNames.Count} 个，配方 {recipeCount} 个）", gateFindings);
        }
    }

    /// <summary>模型门禁命令的参数。</summary>
    public sealed class GateModelArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }
    }

    /// <summary>
    /// 模型门禁命令：每个模型资产请求都要能算出一份完整的加工计划，也就是规格数据够不够用。
    /// 不查真模型文件——仓库里现在一个模型都没有。
    /// </summary>
    public static class GateModelCommand
    {
        /// <summary>
        /// 跑模型门禁：扫 _Tasks/ 下全部资产请求，只挑域是资产.模型的逐个构建加工计划，
        /// 把构建发现汇总。_Tasks 不存在或零个模型资产时通过，问题 0 条。
        /// </summary>
        /// <param name="arguments">模型门禁命令参数。</param>
        [EditorCommand("gate.model")]
        [Summary("模型门禁：加工计划的可产出性")]
        public static CommandResult Execute(GateModelArguments arguments)
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

            var findings = new List<PoolFinding>();
            var modelAssetCount = 0;

            var tasksDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (Directory.Exists(tasksDirectory))
            {
                foreach (var requirementDirectory in Directory.EnumerateDirectories(tasksDirectory))
                {
                    var requestDirectory = Path.Combine(requirementDirectory, "资产请求");
                    if (!Directory.Exists(requestDirectory))
                    {
                        continue;
                    }

                    foreach (var requestFile in Directory.EnumerateFiles(requestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        var request = AssetRequest.Read(requestFile);
                        if (!string.Equals(request.Domain, "资产.模型", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        modelAssetCount++;
                        findings.AddRange(ProcessingPlanBuilder.Build(repositoryRoot, request, arguments.ModuleName ?? "").Findings);
                    }
                }
            }

            var gateFindings = findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult($"模型门禁（模型资产 {modelAssetCount} 个）", gateFindings);
        }
    }
}
