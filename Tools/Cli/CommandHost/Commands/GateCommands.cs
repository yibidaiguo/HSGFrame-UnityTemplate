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
        [DefaultValue("Template/Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>测试基线锁命令的参数。</summary>
    public sealed class GateBaselineArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>为 true 时重建基线，否则校验基线。</summary>
        [Summary("为 true 时重建基线，否则校验基线")]
        [DefaultValue(false)]
        public bool UpdateBaseline { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Template/Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>改动路径白名单门禁命令的参数。</summary>
    public sealed class GateWhitelistArguments
    {
        /// <summary>按换行分隔的改动路径文本。</summary>
        [Summary("按换行分隔的改动路径文本")]
        public string ChangedPathsText { get; set; }

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Template/Tools/Gates/Config/gate-config.json")]
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

        /// <summary>门禁配置文件路径，默认取仓库内的 gate-config.json。</summary>
        [Summary("门禁配置文件路径，默认取仓库内的 gate-config.json")]
        [DefaultValue("Template/Tools/Gates/Config/gate-config.json")]
        public string ConfigurationPath { get; set; }
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

            // RootDirectory 是扫描根（可以是 Template 这类子目录），拼配置路径要用进程工作目录，
            // 否则会拼出 Template/Template/Tools/... 这种走不通的路径。
            var configuration = GateConfiguration.LoadFromFile(
                GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, Environment.CurrentDirectory));

            var findings = NamingChecker.Check(
                NamingChecker.EnumerateSourceFiles(arguments.RootDirectory, configuration.SourceScanSkipSegments),
                configuration);

            return GateCommandSupport.ToResult("命名与注释规范门禁", findings);
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
            if (string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var configurationPath = GateCommandSupport.ResolveConfigurationPath(arguments.ConfigurationPath, arguments.RepositoryRoot);
            var configuration = GateConfiguration.LoadFromFile(configurationPath);
            var baselinePath = GateCommandSupport.ResolveBaselinePath(configurationPath);

            if (arguments.UpdateBaseline)
            {
                TestBaselineLock.WriteBaseline(arguments.RepositoryRoot, configuration, baselinePath);
                return CommandResult.Success("测试基线已重建");
            }

            var findings = TestBaselineLock.Check(arguments.RepositoryRoot, configuration, baselinePath);
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

            var documentPaths = Directory.Exists(fullDirectory)
                ? Directory.EnumerateFiles(fullDirectory, "*.md", SearchOption.AllDirectories)
                : Enumerable.Empty<string>();

            var findings = DocumentLengthChecker.Check(arguments.RepositoryRoot, documentPaths, configuration);
            return GateCommandSupport.ToResult("文档长度门禁", findings);
        }
    }

    /// <summary>门禁命令的公共路径解析与结果封装。</summary>
    internal static class GateCommandSupport
    {
        private const string DefaultConfigurationRelativePath = "Template/Tools/Gates/Config/gate-config.json";

        internal static string ResolveConfigurationPath(string configuredPath, string repositoryRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(repositoryRoot, DefaultConfigurationRelativePath);
        }

        internal static string ResolveBaselinePath(string configurationPath)
        {
            // 基线与门禁配置永远同目录：模板可能是仓库子目录，也可能自己就是仓库根，
            // 拼死 "Template/..." 在后一种形态下会指到不存在的路径。
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
