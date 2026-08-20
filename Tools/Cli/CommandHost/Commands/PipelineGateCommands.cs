using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        /// <summary>池子根目录，相对 RepositoryRoot 解析。</summary>
        [Summary("池子根目录，相对 RepositoryRoot 解析")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>放行策略门禁命令：三层放行策略数据的合法性。</summary>
    public static class GateReleaseCommand
    {
        /// <summary>
        /// 跑放行策略门禁：三层就近合并，把合并过程中发现的违规（放宽被拒、非法值、
        /// 基线独有键被下层写等）一次报出；再加载放行流水，查流水本身的格式问题。
        /// </summary>
        /// <param name="arguments">放行策略门禁命令参数。</param>
        [EditorCommand("gate.release")]
        [Summary("放行策略门禁：三层策略数据与放行流水的合法性")]
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

            // 加载放行流水：流水格式与策略数据一起查。
            var poolRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(arguments.PoolRoot)
                    ? Path.Combine(repositoryRoot, "Pools")
                    : Path.Combine(repositoryRoot, arguments.PoolRoot));
            var ledger = ReleaseLedger.Load(poolRoot);
            var ledgerFile = PoolPaths.ReleaseLedgerFile(poolRoot);
            var ledgerReference = "Doc/创作管线子文档/03-执行引擎.md";

            // 账本读不动：一条 finding 顶上，后面四条不再查——账本都读不动，逐条查没有意义。
            if (ledger.LoadFailureReason.Length > 0)
            {
                gateFindings.Add(new GateFinding(
                    ledgerFile,
                    $"放行流水加载有问题：{ledger.LoadFailureReason}",
                    "修好放行流水.json 或删掉它重来",
                    ledgerReference));
            }
            else
            {
                foreach (var entry in ledger.Entries)
                {
                    // 引用正则常量用字符串连接，不用插值字符串——常量里的 {4} 会被插值吃掉。
                    if (!Regex.IsMatch(entry.Identifier, ReleaseLedgerEntry.IdentifierPatternText))
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            "放行流水条目 id「" + entry.Identifier + "」不匹配 "
                                + ReleaseLedgerEntry.IdentifierPatternText,
                            "改成 RL- 加四位数字",
                            ledgerReference));
                    }

                    if (string.IsNullOrEmpty(entry.RequirementIdentifier))
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            $"流水条目 {entry.Identifier} 的需求id为空",
                            "补上 需求id",
                            ledgerReference));
                    }

                    if (string.IsNullOrEmpty(entry.Grade))
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            $"流水条目 {entry.Identifier} 的风险级为空",
                            "补上 风险级",
                            ledgerReference));
                    }

                    if (entry.Scopes.Count == 0)
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            $"流水条目 {entry.Identifier} 的范围为空数组",
                            "补上 范围",
                            ledgerReference));
                    }

                    if (!IsAllowedSpotCheckState(entry.SpotCheckState))
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            $"流水条目 {entry.Identifier} 的抽查状态「{entry.SpotCheckState}」不在合法值里",
                            $"改成 {string.Join("、", ReleaseLedgerEntry.AllowedSpotCheckStates)} 之一",
                            ledgerReference));
                    }

                    if (string.Equals(entry.SpotCheckState, "发现问题", StringComparison.Ordinal)
                        && string.IsNullOrEmpty(entry.SpotCheckConclusion)
                        && string.IsNullOrEmpty(entry.RevertCommit))
                    {
                        gateFindings.Add(new GateFinding(
                            ledgerFile,
                            $"流水条目 {entry.Identifier} 抽查记了发现问题，却既没写结论也没记回滚提交，这笔账查不出所以然",
                            "补上 抽查结论 或 回滚提交",
                            ledgerReference));
                    }
                }
            }

            return GateCommandSupport.ToResult(
                $"放行策略门禁（策略键 {catalog.Policies.Count} 条，放行流水 {ledger.Entries.Count} 条，未抽查 {ledger.UncheckedCount()} 条）",
                gateFindings);
        }

        private static bool IsAllowedSpotCheckState(string state)
        {
            foreach (var allowed in ReleaseLedgerEntry.AllowedSpotCheckStates)
            {
                if (string.Equals(state, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

    /// <summary>冲突可见门禁命令的参数。</summary>
    public sealed class GateConflictArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }
    }

    /// <summary>
    /// 冲突可见门禁命令：冲突列表格式合法且未销账数可见。未销账不判红——冲突不拦执行，
    /// 这道门禁只查格式，未销账数只是报出来让人看见。
    /// </summary>
    public static class GateConflictCommand
    {
        /// <summary>
        /// 跑冲突可见门禁：把列表加载问题、id 模式、发现阶段、已裁决但选择非法、未决却带裁决对象
        /// 五类问题转成 finding；空列表或列表不存在是通过，不判红。
        /// </summary>
        /// <param name="arguments">冲突可见门禁命令参数。</param>
        [EditorCommand("gate.conflict")]
        [Summary("冲突可见门禁：冲突列表格式合法且未销账数可见")]
        public static CommandResult Execute(GateConflictArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            ConflictList list;
            try
            {
                list = ConflictList.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"冲突列表加载失败：{exception.Message}");
            }

            var conflictFile = PoolPaths.ConflictListFile(poolRoot);
            var reference = "Doc/创作管线子文档/01-池子与需求模型.md";
            var findings = new List<PoolFinding>();

            if (list.LoadFailureReason.Length > 0)
            {
                findings.Add(new PoolFinding(
                    conflictFile,
                    $"冲突列表加载有问题：{list.LoadFailureReason}",
                    "把冲突列表修成合法 JSON 数组",
                    reference));
            }

            foreach (var entry in list.Entries)
            {
                if (!Regex.IsMatch(entry.Identifier, ConflictEntry.IdentifierPatternText))
                {
                    findings.Add(new PoolFinding(
                        conflictFile,
                        $"冲突条目 id「{entry.Identifier}」不匹配 {ConflictEntry.IdentifierPatternText}",
                        "改成 CF- 加四位数字",
                        reference));
                }

                if (!IsAllowedStage(entry.DiscoveryStage))
                {
                    findings.Add(new PoolFinding(
                        conflictFile,
                        $"冲突 {entry.Identifier} 的发现阶段「{entry.DiscoveryStage}」不在合法值里",
                        $"改成 {string.Join("、", ConflictEntry.AllowedStages)} 之一",
                        reference));
                }

                if (string.Equals(entry.State, ConflictEntry.ResolvedState, StringComparison.Ordinal)
                    && !IsAllowedChoice(entry.Choice))
                {
                    findings.Add(new PoolFinding(
                        conflictFile,
                        $"冲突 {entry.Identifier} 已裁决但选择「{entry.Choice}」为空或不在三个合法值里",
                        $"补上 {string.Join("、", ConflictEntry.AllowedChoices)} 之一",
                        reference));
                }

                if (string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal)
                    && entry.HasResolutionPayload)
                {
                    findings.Add(new PoolFinding(
                        conflictFile,
                        $"冲突 {entry.Identifier} 状态是未决但裁决对象非空",
                        "未决条目的 裁决 置回 null",
                        reference));
                }
            }

            var gateFindings = findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult(
                $"冲突可见门禁（冲突 {list.Entries.Count} 条，未销账 {list.PendingCount()} 条）",
                gateFindings);
        }

        private static bool IsAllowedStage(string discoveryStage)
        {
            foreach (var allowed in ConflictEntry.AllowedStages)
            {
                if (string.Equals(discoveryStage, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedChoice(string choice)
        {
            foreach (var allowed in ConflictEntry.AllowedChoices)
            {
                if (string.Equals(choice, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>晋升门禁命令的参数。</summary>
    public sealed class GatePromotionArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>晋升阈值条数，缺省 3。</summary>
        [Summary("晋升阈值条数，缺省 3")]
        [DefaultValue(3)]
        public int Threshold { get; set; }
    }

    /// <summary>
    /// 晋升门禁命令：意见库格式合法且晋升提案可见。有晋升提案不判红——提案是待办，不是违规；
    /// 这道门禁只查意见库的格式，提案数只是报出来让人看见。
    /// </summary>
    public static class GatePromotionCommand
    {
        /// <summary>意见 id 模式：OP- 加四位数字。</summary>
        private const string OpinionIdentifierPatternText = "^OP-\\d{4}$";

        /// <summary>
        /// 跑晋升门禁：把意见库加载问题、id 模式、可规则化性合法值、类别/模块为空四类问题
        /// 转成 finding；空库或目录不存在是通过，不判红。
        /// </summary>
        /// <param name="arguments">晋升门禁命令参数。</param>
        [EditorCommand("gate.promotion")]
        [Summary("晋升门禁：意见库格式合法且晋升提案可见")]
        public static CommandResult Execute(GatePromotionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            ReviewOpinionBook book;
            try
            {
                book = ReviewOpinionBook.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"意见库加载失败：{exception.Message}");
            }

            var opinionDirectory = PoolPaths.ReviewOpinionDirectory(poolRoot);
            var reference = "Doc/创作管线子文档/03-执行引擎.md";
            var findings = new List<PoolFinding>();

            if (book.LoadFailureReason.Length > 0)
            {
                findings.Add(new PoolFinding(
                    opinionDirectory,
                    $"意见库加载有问题：{book.LoadFailureReason}",
                    "把坏条目修成合法意见 JSON",
                    reference));
            }

            foreach (var opinion in book.Opinions)
            {
                if (!Regex.IsMatch(opinion.Identifier, OpinionIdentifierPatternText))
                {
                    findings.Add(new PoolFinding(
                        opinionDirectory,
                        $"意见条目 id「{opinion.Identifier}」不匹配 {OpinionIdentifierPatternText}",
                        "改成 OP- 加四位数字",
                        reference));
                }

                if (!ReviewOpinionBook.IsAllowedRulability(opinion.Rulability))
                {
                    findings.Add(new PoolFinding(
                        opinionDirectory,
                        $"意见 {opinion.Identifier} 的可规则化性「{opinion.Rulability}」不在合法值里",
                        $"改成 {string.Join("、", ReviewOpinionBook.AllowedRulabilityValues)} 之一",
                        reference));
                }

                if (string.IsNullOrEmpty(opinion.Category) || string.IsNullOrEmpty(opinion.ModuleName))
                {
                    findings.Add(new PoolFinding(
                        opinionDirectory,
                        $"意见 {opinion.Identifier} 的问题类别或模块为空",
                        "补上问题类别与模块",
                        reference));
                }
            }

            var proposals = PromotionProposalBuilder.Build(book, arguments.Threshold);
            var gateFindings = findings
                .Select(finding => new GateFinding(finding.Location, finding.Reason, finding.FixAction, finding.ReferenceExamplePath))
                .ToList();
            return GateCommandSupport.ToResult(
                $"晋升门禁（意见 {book.Opinions.Count} 条，达阈值的提案 {proposals.Count} 条）",
                gateFindings);
        }
    }
}
