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

    /// <summary>收件箱归档命令的参数。</summary>
    public sealed class AssetImportArguments
    {
        /// <summary>收件箱目录。</summary>
        [Summary("收件箱目录")]
        public string InboxDirectory { get; set; }

        /// <summary>Assets 根目录，路由表里的目标目录相对它解析。</summary>
        [Summary("Assets 根目录，路由表里的目标目录相对它解析")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>路由表路径，留空时取收件箱目录下的 归档路由.json。</summary>
        [Summary("路由表路径，留空时取收件箱目录下的 归档路由.json")]
        [DefaultValue("")]
        public string RoutingTablePath { get; set; }

        /// <summary>为 true 时只列出计划而不落盘。</summary>
        [Summary("为 true 时只列出计划而不落盘")]
        [DefaultValue(false)]
        public bool PlanOnly { get; set; }
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

    /// <summary>收件箱归档命令：按路由把资产分派到正式目录并按规则改名。</summary>
    public static class AssetImportCommand
    {
        /// <summary>把收件箱里的资产按路由归档到正式目录并按规则改名。</summary>
        /// <param name="arguments">归档参数。</param>
        [EditorCommand("asset.import")]
        [Summary("把收件箱里的资产按路由归档到正式目录并按规则改名")]
        public static CommandResult Execute(AssetImportArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.InboxDirectory) || !Directory.Exists(arguments.InboxDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.InboxDirectory}；原因：收件箱目录不存在或未提供；修复：传入存在的收件箱目录；参考：asset.import 把收件箱资产归档到正式目录");
            }

            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory) || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在或未提供；修复：传入存在的 Assets 根目录；参考：路由表里的目标目录相对 Assets 根解析");
            }

            var assetsRoot = Path.GetFullPath(arguments.AssetsRootDirectory);

            // [DefaultValue] 只让命令框架把参数判成选填，不会把默认值填进参数对象，这里自己兜底。
            var routingTablePath = string.IsNullOrWhiteSpace(arguments.RoutingTablePath)
                ? Path.Combine(arguments.InboxDirectory, "归档路由.json")
                : arguments.RoutingTablePath;

            AssetRoutingTable routingTable;
            try
            {
                routingTable = AssetRoutingTable.LoadFromFile(routingTablePath);
            }
            catch (AssetRoutingException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            IReadOnlyList<AssetArchivePlan> plans;
            try
            {
                plans = AssetInboxArchiver.Plan(arguments.InboxDirectory, assetsRoot, routingTable);
            }
            catch (AssetRoutingException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            if (plans.Count == 0)
            {
                return CommandResult.Success("收件箱里没有可归档的文件");
            }

            var lines = new List<string>();
            foreach (var plan in plans)
            {
                var displayDirectory = Path.GetRelativePath(assetsRoot, plan.TargetDirectory).Replace('\\', '/');
                lines.Add($"{Path.GetFileName(plan.SourcePath)} → {displayDirectory}/{plan.TargetFileName}");
            }

            if (arguments.PlanOnly)
            {
                return CommandResult.Success($"待归档 {plans.Count} 个文件", lines);
            }

            int movedCount;
            try
            {
                movedCount = AssetInboxArchiver.Apply(plans);
            }
            catch (IOException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            return CommandResult.Success($"已归档 {movedCount} 个文件", lines);
        }
    }

    /// <summary>资产引用扫描命令的参数。</summary>
    public sealed class AssetReferencesArguments
    {
        /// <summary>Assets 根目录。</summary>
        [Summary("Assets 根目录")]
        public string AssetsRootDirectory { get; set; }
    }

    /// <summary>资产引用扫描命令：按 .meta 的 guid 报出无人引用的资产与悬空引用。</summary>
    public static class AssetReferencesCommand
    {
        /// <summary>扫描 Assets 根下的 guid 级引用关系。</summary>
        /// <param name="arguments">扫描参数。</param>
        [EditorCommand("asset.references")]
        [Summary("按 .meta 的 guid 扫描资产引用，报出无人引用的资产与悬空引用")]
        public static CommandResult Execute(AssetReferencesArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory)
                || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在；" +
                    "修复：把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录；" +
                    "参考：UnityProject/Assets");
            }

            // Packages 与 PackageCache 里的资产也持有 guid：场景与预制引用 UPM 包里的脚本时
            // 写的就是那些 guid，不把它们算进认领表的话每个引用都会被误报成悬空。
            var unityProjectRoot = Directory.GetParent(Path.GetFullPath(arguments.AssetsRootDirectory))?.FullName;
            var guidSourceDirectories = CollectGuidSourceDirectories(unityProjectRoot);

            var report = AssetReferenceScanner.Scan(
                arguments.AssetsRootDirectory,
                scannedExtensions: null,
                additionalGuidSourceDirectories: guidSourceDirectories);

            var lines = new List<string>();
            foreach (var path in report.UnreferencedAssetPaths)
            {
                lines.Add($"无人引用：{path}");
            }

            var danglingCount = 0;
            foreach (var pair in report.DanglingReferences)
            {
                foreach (var guid in pair.Value)
                {
                    lines.Add($"悬空引用：{pair.Key} 指向 guid {guid}，找不到对应资产");
                    danglingCount++;
                }
            }

            // 悬空引用是真错（引用断了），无人引用只是线索（可能是刚加进来还没接线），
            // 所以只有前者让命令失败。
            if (danglingCount > 0)
            {
                return CommandResult.Failure($"引用扫描发现悬空引用 {danglingCount} 条", lines);
            }

            return CommandResult.Success(
                $"引用扫描完成：无人引用 {report.UnreferencedAssetPaths.Count} 个，悬空引用 0 条",
                lines);
        }

        // guid 来源目录：内嵌包目录、包缓存，外加 manifest.json 里那些 file: 引用的本地包。
        // file: 包是就地引用的，Unity 不会把它们复制进 PackageCache，所以必须从 manifest 里解析出来，
        // 否则引用它们脚本的每个资产都会被误报成悬空引用。
        private static string[] CollectGuidSourceDirectories(string unityProjectRoot)
        {
            if (string.IsNullOrEmpty(unityProjectRoot))
            {
                return Array.Empty<string>();
            }

            var packagesDirectory = Path.Combine(unityProjectRoot, "Packages");
            var directories = new List<string>
            {
                packagesDirectory,
                Path.Combine(unityProjectRoot, "Library", "PackageCache"),
            };

            var manifestPath = Path.Combine(packagesDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return directories.ToArray();
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (!document.RootElement.TryGetProperty("dependencies", out var dependencies))
                {
                    return directories.ToArray();
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    var value = dependency.Value.GetString();
                    if (value == null || !value.StartsWith("file:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    directories.Add(Path.GetFullPath(Path.Combine(packagesDirectory, value.Substring("file:".Length))));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // manifest 坏掉时退回只用内嵌包与包缓存，扫描照常进行。
            }

            return directories.ToArray();
        }
    }

    /// <summary>依赖方向校验命令的参数。</summary>
    public sealed class AssetDependenciesArguments
    {
        /// <summary>Assets 根目录。</summary>
        [Summary("Assets 根目录")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>依赖方向规则文件路径，缺省时用模板自带的那份。</summary>
        [Summary("依赖方向规则文件路径，缺省时用 Tools/AssetPipeline/Config/依赖方向规则.json")]
        [DefaultValue("Tools/AssetPipeline/Config/依赖方向规则.json")]
        public string RulesPath { get; set; }
    }

    /// <summary>依赖方向校验命令：按目录层级判「谁不许引用谁」，引用完整性之外的那一类问题。</summary>
    public static class AssetDependenciesCommand
    {
        /// <summary>按规则检查 Assets 根下的资产引用方向。</summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("asset.dependencies")]
        [Summary("按目录前缀规则检查资产引用方向，报出「谁不许引用谁」的违规")]
        public static CommandResult Execute(AssetDependenciesArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory)
                || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在；" +
                    "修复：把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录；" +
                    "参考：UnityProject/Assets");
            }

            // [DefaultValue] 只让框架把参数判成选填，并不会把默认值填进参数对象，所以这里自己兜底。
            var rulesPath = string.IsNullOrWhiteSpace(arguments.RulesPath)
                ? Path.Combine("Tools", "AssetPipeline", "Config", "依赖方向规则.json")
                : arguments.RulesPath;

            if (!File.Exists(rulesPath))
            {
                return CommandResult.Failure(
                    $"位置：{rulesPath}；原因：依赖方向规则文件不存在；" +
                    "修复：把 RulesPath 指向规则文件，或在模板里补一份；" +
                    "参考：Tools/AssetPipeline/Config/依赖方向规则.json");
            }

            var rules = AssetDependencyRuleSet.LoadFromFile(rulesPath);
            var violations = AssetDependencyDirectionChecker.Check(arguments.AssetsRootDirectory, rules);

            var lines = violations.Select(violation => violation.ToDisplayText()).ToList();
            if (violations.Count > 0)
            {
                return CommandResult.Failure($"依赖方向校验发现违规 {violations.Count} 条（规则 {rules.Count} 条）", lines);
            }

            return CommandResult.Success($"依赖方向校验通过：规则 {rules.Count} 条，违规 0 条", lines);
        }
    }

    /// <summary>打包分组校验命令的参数。</summary>
    public sealed class AssetBundleGroupsArguments
    {
        /// <summary>Assets 根目录。</summary>
        [Summary("Assets 根目录")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>打包分组规则文件路径，缺省时用模板自带的那份。</summary>
        [Summary("打包分组规则文件路径，缺省时用 Tools/AssetPipeline/Config/打包分组规则.json")]
        [DefaultValue("Tools/AssetPipeline/Config/打包分组规则.json")]
        public string RulesPath { get; set; }
    }

    /// <summary>打包分组校验命令：按目录分组判「谁该和谁打进同一个包」。</summary>
    public static class AssetBundleGroupsCommand
    {
        /// <summary>按规则检查 Assets 根下的资产分组，报出共享资产未落共享组与未分组资产。</summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("asset.bundlegroups")]
        [Summary("按打包分组规则检查资产分组，报出共享资产未落共享组与未分组资产")]
        public static CommandResult Execute(AssetBundleGroupsArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory)
                || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在；" +
                    "修复：把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录；" +
                    "参考：UnityProject/Assets");
            }

            // 框架会把 [DefaultValue] 的值填进参数对象（CommandArgumentBinder.ApplyDefaults），
            // 这句兜底只兜「显式传了空串」的情况。
            var rulesPath = string.IsNullOrWhiteSpace(arguments.RulesPath)
                ? Path.Combine("Tools", "AssetPipeline", "Config", "打包分组规则.json")
                : arguments.RulesPath;

            if (!File.Exists(rulesPath))
            {
                return CommandResult.Failure(
                    $"位置：{rulesPath}；原因：打包分组规则文件不存在；" +
                    "修复：把 RulesPath 指向规则文件，或在模板里补一份；" +
                    "参考：Tools/AssetPipeline/Config/打包分组规则.json");
            }

            var ruleSet = AssetBundleGroupRuleSet.LoadFromFile(rulesPath);
            var violations = AssetBundleGroupChecker.Check(arguments.AssetsRootDirectory, ruleSet);

            var lines = violations.Select(violation => violation.ToDisplayText()).ToList();
            var groupCount = ruleSet.Groups?.Count ?? 0;
            if (violations.Count > 0)
            {
                return CommandResult.Failure($"打包分组校验发现违规 {violations.Count} 条（分组 {groupCount} 个）", lines);
            }

            return CommandResult.Success($"打包分组校验通过：分组 {groupCount} 个，违规 0 条", lines);
        }
    }

    /// <summary>加载分组校验命令的参数。</summary>
    public sealed class AssetLoadGroupsArguments
    {
        /// <summary>Assets 根目录。</summary>
        [Summary("Assets 根目录")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>打包分组规则文件路径，缺省时用模板自带的那份；加载分组字段就写在它的分组条目上。</summary>
        [Summary("打包分组规则文件路径，缺省时用 Tools/AssetPipeline/Config/打包分组规则.json")]
        [DefaultValue("Tools/AssetPipeline/Config/打包分组规则.json")]
        public string RulesPath { get; set; }

        /// <summary>YooAsset 收集器配置路径，缺省时用工程里的那份。</summary>
        [Summary("YooAsset 收集器配置路径，缺省时用 UnityProject/Assets/Game/Settings/Resource/BundleCollectorSetting.asset")]
        [DefaultValue("UnityProject/Assets/Game/Settings/Resource/BundleCollectorSetting.asset")]
        public string CollectorSettingPath { get; set; }
    }

    /// <summary>加载分组校验命令：查动态分组的加载分组字段、预制体落点，以及收集器 group 与分组条目的对账。</summary>
    public static class AssetLoadGroupsCommand
    {
        /// <summary>按规则检查加载分组字段、预制体落点与收集器对账。</summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("asset.loadgroups")]
        [Summary("检查动态分组的加载分组字段、预制体落点，以及收集器 group 与分组条目对账")]
        public static CommandResult Execute(AssetLoadGroupsArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory)
                || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在；" +
                    "修复：把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录；" +
                    "参考：UnityProject/Assets");
            }

            var rulesPath = string.IsNullOrWhiteSpace(arguments.RulesPath)
                ? Path.Combine("Tools", "AssetPipeline", "Config", "打包分组规则.json")
                : arguments.RulesPath;

            if (!File.Exists(rulesPath))
            {
                return CommandResult.Failure(
                    $"位置：{rulesPath}；原因：打包分组规则文件不存在；" +
                    "修复：把 RulesPath 指向规则文件，或在模板里补一份；" +
                    "参考：Tools/AssetPipeline/Config/打包分组规则.json");
            }

            var collectorSettingPath = string.IsNullOrWhiteSpace(arguments.CollectorSettingPath)
                ? Path.Combine("UnityProject", "Assets", "Game", "Settings", "Resource", "BundleCollectorSetting.asset")
                : arguments.CollectorSettingPath;

            var ruleSet = AssetBundleGroupRuleSet.LoadFromFile(rulesPath);
            var violations = AssetLoadGroupChecker.Check(arguments.AssetsRootDirectory, ruleSet, collectorSettingPath);

            var lines = violations.Select(violation => violation.ToDisplayText()).ToList();
            var groupCount = ruleSet.Groups?.Count ?? 0;
            if (violations.Count > 0)
            {
                return CommandResult.Failure($"加载分组校验发现违规 {violations.Count} 条（分组 {groupCount} 个）", lines);
            }

            return CommandResult.Success($"加载分组校验通过：分组 {groupCount} 个，违规 0 条", lines);
        }
    }

    /// <summary>导入规则覆盖校验命令的参数。</summary>
    public sealed class AssetRuleCoverageArguments
    {
        /// <summary>Assets 根目录。</summary>
        [Summary("Assets 根目录")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>覆盖范围配置路径，缺省时用模板自带的那份。</summary>
        [Summary("覆盖范围配置路径，缺省时用 Tools/AssetPipeline/Config/规则覆盖范围.json")]
        [DefaultValue("Tools/AssetPipeline/Config/规则覆盖范围.json")]
        public string SettingsPath { get; set; }
    }

    /// <summary>导入规则覆盖校验命令：放了资产的目录必须能解析到一份导入规则。</summary>
    public static class AssetRuleCoverageCommand
    {
        /// <summary>检查扫描根下每个放了资产的目录是否被导入规则覆盖。</summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("asset.rulecoverage")]
        [Summary("检查放了资产的目录是否都被导入规则覆盖")]
        public static CommandResult Execute(AssetRuleCoverageArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory)
                || !Directory.Exists(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.AssetsRootDirectory}；原因：Assets 根目录不存在；" +
                    "修复：把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录；" +
                    "参考：UnityProject/Assets");
            }

            // 框架会把 [DefaultValue] 的值填进参数对象（CommandArgumentBinder.ApplyDefaults），
            // 这句兜底只兜「显式传了空串」的情况。
            var settingsPath = string.IsNullOrWhiteSpace(arguments.SettingsPath)
                ? Path.Combine("Tools", "AssetPipeline", "Config", "规则覆盖范围.json")
                : arguments.SettingsPath;

            if (!File.Exists(settingsPath))
            {
                return CommandResult.Failure(
                    $"位置：{settingsPath}；原因：规则覆盖范围配置不存在；" +
                    "修复：把 SettingsPath 指向配置文件，或在模板里补一份；" +
                    "参考：Tools/AssetPipeline/Config/规则覆盖范围.json");
            }

            var settings = AssetRuleCoverageSettings.LoadFromFile(settingsPath);
            var violations = AssetRuleCoverageChecker.Check(arguments.AssetsRootDirectory, settings);

            var lines = violations.Select(violation => violation.ToDisplayText()).ToList();
            var scanRootCount = settings.ScanRoots?.Count ?? 0;
            if (violations.Count > 0)
            {
                return CommandResult.Failure($"导入规则覆盖校验发现违规 {violations.Count} 条（扫描根 {scanRootCount} 个）", lines);
            }

            return CommandResult.Success($"导入规则覆盖校验通过：扫描根 {scanRootCount} 个，违规 0 条", lines);
        }
    }
}
