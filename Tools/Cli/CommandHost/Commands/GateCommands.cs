using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Gates;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>命名与注释规范门禁命令的参数。</summary>
    public sealed class GateNamingArguments
    {
        /// <summary>要扫描的源文件根目录。</summary>
        [Summary("要扫描的源文件根目录")]
        public string RootDirectory { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>测试基线锁命令的参数。</summary>
    public sealed class GateBaselineArguments
    {
        /// <summary>模板根目录：测试与基线都在模板内，路径以它为基准才不随模板放在哪里而变。</summary>
        [Summary("模板根目录，测试与基线路径都以它为基准")]
        public string TemplateRoot { get; set; }

        /// <summary>为 true 时重建基线，否则校验基线。</summary>
        [Summary("为 true 时重建基线，否则校验基线")]
        [DefaultValue(false)]
        public bool UpdateBaseline { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>改动路径白名单门禁命令的参数。</summary>
    public sealed class GateWhitelistArguments
    {
        /// <summary>按换行分隔的改动路径文本，没有改动时留空。</summary>
        // 标成选填：刚生成出来的新项目还没有 git 仓库，git status 吐不出任何东西，
        // 这时候「没有改动路径」是正常状态，不该让门禁因为参数为空而红。
        [Summary("按换行分隔的改动路径文本，没有改动时留空")]
        [DefaultValue("")]
        public string ChangedPathsText { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>文档长度门禁命令的参数。</summary>
    public sealed class GateDocumentArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>要扫描的文档目录，相对仓库根，默认 Doc。</summary>
        [Summary("要扫描的文档目录，相对仓库根，默认 Doc")]
        [DefaultValue("Doc")]
        public string DocumentDirectory { get; set; }

        /// <summary>模板根目录，留空时只扫仓库根下的文档目录。</summary>
        // 模板自带的 CLAUDE.md / 开始使用.md / 迁移清单.md 不在 Doc/ 下，
        // 不把模板根一并扫进来，这几份文档永远逃过文档长度门禁。
        [Summary("模板根目录，留空时只扫仓库根下的文档目录")]
        [DefaultValue("")]
        public string TemplateRoot { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>.meta 完整性门禁命令的参数。</summary>
    public sealed class MetaGateArguments
    {
        /// <summary>UnityProject/Assets 的路径。</summary>
        [Summary("UnityProject/Assets 的路径")]
        public string AssetsRootDirectory { get; set; }

        /// <summary>门禁配置文件路径，默认取模板内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取模板内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>模块边界门禁命令的参数。</summary>
    public sealed class GateModuleBoundaryArguments
    {
        /// <summary>业务代码根目录，即 Assets/Game/Scripts。</summary>
        [Summary("业务代码根目录，即 UnityProject/Assets/Game/Scripts")]
        public string ScriptsRootDirectory { get; set; }

        /// <summary>门禁配置文件路径，默认取模板内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取模板内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>模块边界门禁命令：模块的公开面只有 Contracts 与 Events，其余都是私有。</summary>
    public static class GateModuleBoundaryCommand
    {
        /// <summary>
        /// 跑模块边界检查，返回结构化发现列表。
        /// </summary>
        /// <param name="arguments">模块边界门禁参数。</param>
        [EditorCommand("gate.moduleboundary")]
        [Summary("模块边界门禁：模块之外只准引它的 Contracts 与 Events")]
        public static CommandResult Execute(GateModuleBoundaryArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.ScriptsRootDirectory))
            {
                return CommandResult.Failure("参数 ScriptsRootDirectory 为必填项");
            }

            if (!Directory.Exists(arguments.ScriptsRootDirectory))
            {
                return CommandResult.Failure(
                    $"位置：{arguments.ScriptsRootDirectory}；原因：业务代码根目录不存在；" +
                    "修复：把 ScriptsRootDirectory 指向 Assets/Game/Scripts；" +
                    "参考：UnityProject/Assets/Game/Scripts");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.ScriptsRootDirectory));

            var moduleNames = ModuleBoundaryChecker.ReadModuleNames(arguments.ScriptsRootDirectory);
            var findings = ModuleBoundaryChecker.Check(arguments.ScriptsRootDirectory, configuration);

            // 模块数报出来是有用的：Modules/ 空掉或者路径传错时这条检查会「全绿」，
            // 只有这个数能把「真没违规」和「根本没扫到东西」分开。
            return GateCommandSupport.ToResult(
                $"模块边界门禁（模块 {moduleNames.Count} 个：{string.Join("、", moduleNames)}）", findings);
        }
    }

    /// <summary>命名与注释规范门禁命令：缩写、公开类型中文摘要、目录命名。</summary>
    public static class GateNamingCommand
    {
        /// <summary>
        /// 跑命名与注释规范检查，返回结构化发现列表。
        /// </summary>
        /// <param name="arguments">命名门禁参数。</param>
        [EditorCommand("gate.naming")]
        [Summary("命名与注释规范门禁：缩写、公开类型中文摘要、目录命名")]
        public static CommandResult Execute(GateNamingArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.RootDirectory))
            {
                return CommandResult.Failure("参数 RootDirectory 为必填项");
            }

            // 锚点用扫描根：解析器会先看「锚点 + 相对路径」在不在，不在才逐级上溯，
            // 所以扫描根既可以是模板目录本身，也可以是它的子目录。
            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.RootDirectory));

            var findings = NamingChecker.Check(
                NamingChecker.EnumerateSourceFiles(arguments.RootDirectory, configuration.SourceScanSkipSegments),
                configuration);

            return GateCommandSupport.ToResult("命名与注释规范门禁", findings);
        }
    }

    /// <summary>通用性门禁命令的参数。</summary>
    public sealed class GateGenericArguments
    {
        /// <summary>要扫描的源文件根目录。</summary>
        [Summary("要扫描的源文件根目录")]
        public string RootDirectory { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>通用性门禁命令：宿主项目专属名字有没有焊进通用件。</summary>
    public static class GateGenericCommand
    {
        /// <summary>
        /// 跑通用性检查，返回结构化发现列表。
        /// </summary>
        /// <param name="arguments">通用性门禁参数。</param>
        [EditorCommand("gate.generic")]
        [Summary("通用性门禁：标识符、菜单路径、路径字面量里不允许出现宿主项目专属名字")]
        public static CommandResult Execute(GateGenericArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.RootDirectory))
            {
                return CommandResult.Failure("参数 RootDirectory 为必填项");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.RootDirectory));

            var findings = GenericNameChecker.Check(
                NamingChecker.EnumerateSourceFiles(arguments.RootDirectory, configuration.SourceScanSkipSegments),
                configuration);

            return GateCommandSupport.ToResult("通用性门禁", findings);
        }
    }

    /// <summary>测试基线锁命令：登记或校验测试源文件哈希。</summary>
    public static class GateBaselineCommand
    {
        /// <summary>
        /// 重建或校验测试基线。
        /// </summary>
        /// <param name="arguments">基线命令参数。</param>
        [EditorCommand("gate.baseline")]
        [Summary("测试基线锁：登记或校验测试文件哈希")]
        public static CommandResult Execute(GateBaselineArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.TemplateRoot))
            {
                return CommandResult.Failure("参数 TemplateRoot 为必填项");
            }

            var configurationPath = GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.TemplateRoot);
            var configuration = GateConfiguration.LoadFromFile(configurationPath);
            var baselinePath = GateCommandSupport.ResolveBaselinePath(configurationPath);

            if (arguments.UpdateBaseline)
            {
                TestBaselineLock.WriteBaseline(arguments.TemplateRoot, configuration, baselinePath);
                return CommandResult.Success("测试基线已重建");
            }

            var findings = TestBaselineLock.Check(arguments.TemplateRoot, configuration, baselinePath);
            return GateCommandSupport.ToResult("测试基线校验", findings);
        }
    }

    /// <summary>改动路径白名单门禁命令：校验改动路径是否落在白名单内。</summary>
    public static class GateWhitelistCommand
    {
        /// <summary>
        /// 校验改动路径白名单。
        /// </summary>
        /// <param name="arguments">白名单命令参数。</param>
        [EditorCommand("gate.whitelist")]
        [Summary("改动路径白名单门禁：校验改动路径是否落在白名单内")]
        public static CommandResult Execute(GateWhitelistArguments arguments)
        {
            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, Environment.CurrentDirectory));

            var changedPaths = GateCommandSupport.SplitLines(arguments.ChangedPathsText);
            var findings = FileWhitelistChecker.Check(changedPaths, configuration);

            return GateCommandSupport.ToResult("改动路径白名单门禁", findings);
        }
    }

    /// <summary>文档长度门禁命令：检查文档行数是否超限。</summary>
    public static class GateDocumentCommand
    {
        /// <summary>
        /// 检查文档行数是否超限。
        /// </summary>
        /// <param name="arguments">文档门禁参数。</param>
        [EditorCommand("gate.doc")]
        [Summary("文档长度门禁：检查单文档行数是否超限")]
        public static CommandResult Execute(GateDocumentArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.RepositoryRoot));

            var documentDirectory = string.IsNullOrWhiteSpace(arguments.DocumentDirectory)
                ? "Doc"
                : arguments.DocumentDirectory;
            var fullDirectory = Path.Combine(arguments.RepositoryRoot, documentDirectory);

            var documentPaths = new List<string>();
            GateCommandSupport.CollectMarkdownFiles(fullDirectory, configuration, documentPaths);

            if (!string.IsNullOrWhiteSpace(arguments.TemplateRoot))
            {
                GateCommandSupport.CollectMarkdownFiles(arguments.TemplateRoot, configuration, documentPaths);
            }

            var findings = DocumentLengthChecker.Check(
                arguments.RepositoryRoot,
                documentPaths.Distinct(StringComparer.OrdinalIgnoreCase),
                configuration);
            return GateCommandSupport.ToResult("文档长度门禁", findings);
        }
    }

    /// <summary>.meta 完整性门禁命令：Unity 资产缺失或孤儿 .meta 各报一条。</summary>
    public static class GateMetaCommand
    {
        /// <summary>
        /// 检查 Assets 目录下每个资产是否都有配对的 .meta。
        /// </summary>
        /// <param name="arguments">.meta 完整性门禁参数。</param>
        [EditorCommand("gate.meta")]
        [Summary("Unity 资产的 .meta 完整性门禁：缺失与孤儿各报一条")]
        public static CommandResult CheckMeta(MetaGateArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.AssetsRootDirectory))
            {
                return CommandResult.Failure("参数 AssetsRootDirectory 为必填项");
            }

            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.AssetsRootDirectory));

            var findings = MetaIntegrityChecker.Check(arguments.AssetsRootDirectory, configuration);

            return GateCommandSupport.ToResult("meta 完整性门禁", findings);
        }
    }

    /// <summary>门禁命令的公共路径解析与结果封装。</summary>
    internal static class GateCommandSupport
    {
        private const string DefaultConfigurationRelativePath = "Tools/Gates/Config/gate-config.json";

        // 模板根里躺着 Unity 的 Library、包缓存、构建产物，里面的 *.md 成千上万且不归本仓库管。
        // 跳过段与命名门禁用的是同一套：一处配置管两处扫描，免得两边慢慢分叉。
        private static readonly string[] DocumentScanSkipSegments =
        {
            "bin", "obj", ".git", "Library", "Temp", "Logs", "Build", "Bundles",
            "PackageCache", "HybridCLRData", "HybridCLRGenerate", "node_modules"
        };

        internal static void CollectMarkdownFiles(string rootDirectory, GateConfiguration configuration, List<string> results)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                return;
            }

            var skipSegments = DocumentScanSkipSegments
                .Concat(configuration.SourceScanSkipSegments ?? Array.Empty<string>())
                .ToArray();

            var rootFullPath = Path.GetFullPath(rootDirectory);
            foreach (var filePath in Directory.EnumerateFiles(rootDirectory, "*.md", SearchOption.AllDirectories))
            {
                // 段匹配只看扫描根内部的相对路径：跳过名单针对的是仓库内的生成物目录
                // （Library、bin、obj……），拿绝对路径整串去匹配会把系统临时目录的
                // Temp 段误认成生成物目录，测试用临时目录搭树时全部文件都被跳过。
                var relative = Path.GetRelativePath(rootFullPath, Path.GetFullPath(filePath)).Replace('\\', '/');
                var skipped = relative.Split('/').Any(segment =>
                    skipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
                if (!skipped)
                {
                    results.Add(filePath);
                }
            }
        }

        // 命令框架现在会把 [DefaultValue] 声明的相对路径填进参数对象，所以这里拿到的
        // ConfigurationPath 多半已经是一条相对路径而不是 null。相对路径先按调用点给的锚点拼，
        // 拼不到就沿目录树逐级往上找：从仓库根直接调 gate.meta 时锚点是
        // <模板根>/UnityProject/Assets，往上两级正好是模板根。两步都落空才退回
        // 「锚点 + 相对路径」，让报错消息里那条路径还看得出调用方想找的是什么。
        internal static string ResolveConfigurationPath(string configuredPath, string anchorDirectory)
        {
            var relativePath = string.IsNullOrWhiteSpace(configuredPath)
                ? DefaultConfigurationRelativePath
                : configuredPath;

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            var anchor = string.IsNullOrWhiteSpace(anchorDirectory)
                ? Environment.CurrentDirectory
                : anchorDirectory;

            var combined = Path.GetFullPath(Path.Combine(anchor, relativePath));
            if (File.Exists(combined))
            {
                return combined;
            }

            return SearchUpward(anchor, relativePath)
                ?? SearchUpward(Environment.CurrentDirectory, relativePath)
                ?? combined;
        }

        // 从起点目录逐级往上找相对路径指的那个文件，找到就返回绝对路径，找不到返回 null。
        private static string SearchUpward(string startDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            {
                return null;
            }

            var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                current = current.Parent;
            }

            return null;
        }

        internal static string ResolveBaselinePath(string configurationPath)
        {
            // 基线与门禁配置永远同目录：模板可能是仓库子目录，也可能自己就是仓库根，
            // 拼死带模板目录名的路径，在后一种形态下会指到不存在的地方。
            var configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath));
            return Path.Combine(configurationDirectory, "test-baseline.json");
        }

        internal static CommandResult ToResult(string gateName, IReadOnlyList<GateFinding> findings)
        {
            if (findings.Count == 0)
            {
                return CommandResult.Success($"{gateName}通过，问题 0 条");
            }

            var lines = findings.Select(finding => finding.ToDisplayText()).ToList();
            return CommandResult.Failure($"{gateName}失败，问题 {findings.Count} 条", lines);
        }

        internal static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Enumerable.Empty<string>();
            }

            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }
    }
}
