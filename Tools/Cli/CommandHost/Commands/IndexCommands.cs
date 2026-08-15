using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost;
using Template.Toolkit.Indexing;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>索引命令的参数。</summary>
    public sealed class IndexArguments
    {
        /// <summary>模板根目录，索引的扫描源与产出路径都以它为基准。</summary>
        [Summary("模板根目录，索引的扫描源与产出路径都以它为基准")]
        public string TemplateRoot { get; set; }

        /// <summary>索引配置文件路径，默认 Tools/Indexing/Config/index-config.json。</summary>
        [Summary("索引配置文件路径，默认 Tools/Indexing/Config/index-config.json")]
        [DefaultValue("Tools/Indexing/Config/index-config.json")]
        public string ConfigurationPath { get; set; }

        /// <summary>为 true 时从上次的断点续跑，缺省从头跑。</summary>
        [Summary("为 true 时从上次的断点续跑，缺省从头跑")]
        [DefaultValue(false)]
        public bool Resume { get; set; }

        /// <summary>为 true 时增量重建：文件大小与修改时间都没变的条目复用上次的哈希。</summary>
        [Summary("为 true 时增量重建：文件大小与修改时间都没变的条目复用上次的哈希")]
        [DefaultValue(false)]
        public bool Incremental { get; set; }
    }

    /// <summary>索引命令：重建四类索引与校验索引新鲜度。</summary>
    public static class IndexCommands
    {
        private const string DefaultConfigurationPath = "Tools/Indexing/Config/index-config.json";

        /// <summary>
        /// 重建配置里的全部索引，输出每类索引的条目数。
        /// </summary>
        /// <param name="arguments">索引参数。</param>
        [EditorCommand("index.rebuild")]
        [Summary("重建全部四类索引")]
        public static CommandResult Rebuild(IndexArguments arguments)
        {
            var templateRoot = RequireTemplateRoot(arguments);
            if (templateRoot == null)
            {
                return CommandResult.Failure("参数 TemplateRoot 为必填项");
            }

            var configuration = LoadConfiguration(arguments, templateRoot);
            var lines = new List<string>();

            var progress = CommandProgress.Load(
                CommandExecutionContext.ProgressRootDirectory,
                "index.rebuild",
                CommandExecutionContext.ArgumentsJson,
                arguments.Resume);

            foreach (var definition in configuration.Definitions)
            {
                var executed = progress.RunStep(definition.IndexName, () =>
                {
                    var outputPath = Path.Combine(templateRoot, definition.OutputPath);
                    var document = arguments.Incremental && File.Exists(outputPath)
                        ? IndexBuilder.BuildIncremental(templateRoot, definition, IndexDocument.LoadFromFile(outputPath))
                        : IndexBuilder.Build(templateRoot, definition);
                    document.SaveToFile(outputPath);

                    var line = $"{definition.IndexName}：{document.Entries.Count} 条";
                    if (arguments.Incremental)
                    {
                        line += $"，复用 {document.ReusedEntryCount} 条";
                    }
                    lines.Add(line);
                });

                if (!executed)
                {
                    lines.Add($"{definition.IndexName}：断点已完成，跳过");
                }
            }

            progress.Complete();

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
            var templateRoot = RequireTemplateRoot(arguments);
            if (templateRoot == null)
            {
                return CommandResult.Failure("参数 TemplateRoot 为必填项");
            }

            // 配置路径不存在或索引文件损坏时，给结构化失败而不是把裸异常抛给转发层。
            try
            {
                var configuration = LoadConfiguration(arguments, templateRoot);
                var problems = IndexFreshnessChecker.Check(templateRoot, configuration);

                if (problems.Count == 0)
                {
                    return CommandResult.Success("索引全部新鲜");
                }

                return CommandResult.Failure($"索引新鲜度校验失败，问题 {problems.Count} 条", problems);
            }
            // 只接住「配置或索引文件本身有问题」这三类。接 Exception 会把实现里的
            // NullReference 之类的 bug 一并伪装成体面的失败消息，那才是真正难查的。
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException)
            {
                return CommandResult.Failure(
                    $"校验索引新鲜度失败：{exception.Message}。" +
                    $"位置：{arguments.ConfigurationPath ?? DefaultConfigurationPath}；" +
                    "修复：确认索引配置文件存在且是合法 JSON，再重跑；" +
                    "参考：Tools/Indexing/Config/index-config.json");
            }
        }

        private static string RequireTemplateRoot(IndexArguments arguments)
        {
            return string.IsNullOrWhiteSpace(arguments?.TemplateRoot) ? null : arguments.TemplateRoot;
        }

        private static IndexConfiguration LoadConfiguration(IndexArguments arguments, string templateRoot)
        {
            var configurationPath = string.IsNullOrWhiteSpace(arguments.ConfigurationPath)
                ? DefaultConfigurationPath
                : arguments.ConfigurationPath;

            // 相对路径一律按模板根拼。基准只有一个，是这类「换个目录深度就全断」的缺陷的根治办法：
            // 上一轮测试基线就是因为记了相对仓库根的路径，模板搬个位置便全部「文件已消失」。
            var fullPath = Path.IsPathRooted(configurationPath)
                ? configurationPath
                : Path.Combine(templateRoot, configurationPath);

            return IndexConfiguration.LoadFromFile(fullPath);
        }
    }
}
