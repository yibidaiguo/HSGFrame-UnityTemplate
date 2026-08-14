using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Indexing;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>索引命令的参数。</summary>
    public sealed class IndexArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>索引配置文件路径，默认 Template/Tools/Indexing/Config/index-config.json。</summary>
        [Summary("索引配置文件路径，默认 Template/Tools/Indexing/Config/index-config.json")]
        [DefaultValue("Template/Tools/Indexing/Config/index-config.json")]
        public string ConfigurationPath { get; set; }
    }

    /// <summary>索引命令：重建四类索引与校验索引新鲜度。</summary>
    public static class IndexCommands
    {
        private const string DefaultConfigurationPath = "Template/Tools/Indexing/Config/index-config.json";

        /// <summary>
        /// 重建配置里的全部索引，输出每类索引的条目数。
        /// </summary>
        /// <param name="arguments">索引参数。</param>
        [EditorCommand("index.rebuild")]
        [Summary("重建全部四类索引")]
        public static CommandResult Rebuild(IndexArguments arguments)
        {
            var repositoryRoot = RequireRepositoryRoot(arguments);
            if (repositoryRoot == null)
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var configuration = LoadConfiguration(arguments, repositoryRoot);
            var lines = new List<string>();

            foreach (var definition in configuration.Definitions)
            {
                var document = IndexBuilder.Build(repositoryRoot, definition);
                document.SaveToFile(Path.Combine(repositoryRoot, definition.OutputPath));
                lines.Add($"{definition.IndexName}：{document.Entries.Count} 条");
            }

            return CommandResult.Success($"索引重建完成，共 {configuration.Definitions.Count} 类", lines);
        }

        /// <summary>
        /// 校验索引新鲜度，有缺失或过期时返回失败。
        /// </summary>
        /// <param name="arguments">索引参数。</param>
        [EditorCommand("index.check")]
        [Summary("校验索引新鲜度")]
        public static CommandResult Check(IndexArguments arguments)
        {
            var repositoryRoot = RequireRepositoryRoot(arguments);
            if (repositoryRoot == null)
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var configuration = LoadConfiguration(arguments, repositoryRoot);
            var problems = IndexFreshnessChecker.Check(repositoryRoot, configuration);

            if (problems.Count == 0)
            {
                return CommandResult.Success("索引全部新鲜");
            }

            return CommandResult.Failure($"索引新鲜度校验失败，问题 {problems.Count} 条", problems);
        }

        private static string RequireRepositoryRoot(IndexArguments arguments)
        {
            return string.IsNullOrWhiteSpace(arguments?.RepositoryRoot) ? null : arguments.RepositoryRoot;
        }

        private static IndexConfiguration LoadConfiguration(IndexArguments arguments, string repositoryRoot)
        {
            var configurationPath = string.IsNullOrWhiteSpace(arguments.ConfigurationPath)
                ? DefaultConfigurationPath
                : arguments.ConfigurationPath;

            var fullPath = Path.IsPathRooted(configurationPath)
                ? configurationPath
                : Path.Combine(repositoryRoot, configurationPath);

            return IndexConfiguration.LoadFromFile(fullPath);
        }
    }
}
