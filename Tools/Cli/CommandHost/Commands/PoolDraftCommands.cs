using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>面板建需求命令 pool.draft 的参数。</summary>
    public sealed class PoolDraftArguments
    {
        /// <summary>标题，必填。</summary>
        [Summary("标题，必填")]
        public string Title { get; set; }

        /// <summary>类型，必填。</summary>
        [Summary("类型，必填")]
        public string Kind { get; set; }

        /// <summary>描述，缺省空串。</summary>
        [Summary("描述，缺省空串")]
        [DefaultValue("")]
        public string Description { get; set; }

        /// <summary>验收标准，一行一条，缺省空串。</summary>
        [Summary("验收标准，一行一条，缺省空串")]
        [DefaultValue("")]
        public string AcceptanceCriteria { get; set; }

        /// <summary>专项 id，缺省空串。</summary>
        [Summary("专项 id，缺省空串")]
        [DefaultValue("")]
        public string Epic { get; set; }

        /// <summary>提交人，缺省空串。</summary>
        [Summary("提交人，缺省空串")]
        [DefaultValue("")]
        public string Submitter { get; set; }

        /// <summary>目标（系统类附加字段），缺省空串。</summary>
        [Summary("目标（系统类附加字段），缺省空串")]
        [DefaultValue("")]
        public string Goal { get; set; }

        /// <summary>玩法（系统类附加字段），缺省空串。</summary>
        [Summary("玩法（系统类附加字段），缺省空串")]
        [DefaultValue("")]
        public string Gameplay { get; set; }

        /// <summary>现状（修改类附加字段），缺省空串。</summary>
        [Summary("现状（修改类附加字段），缺省空串")]
        [DefaultValue("")]
        public string Current { get; set; }

        /// <summary>期望（修改类附加字段），缺省空串。</summary>
        [Summary("期望（修改类附加字段），缺省空串")]
        [DefaultValue("")]
        public string Expected { get; set; }

        /// <summary>实际（缺陷类附加字段），缺省空串。</summary>
        [Summary("实际（缺陷类附加字段），缺省空串")]
        [DefaultValue("")]
        public string Actual { get; set; }

        /// <summary>复现步骤（缺陷类附加字段），缺省空串。</summary>
        [Summary("复现步骤（缺陷类附加字段），缺省空串")]
        [DefaultValue("")]
        public string ReproSteps { get; set; }

        /// <summary>池子根目录，缺省 Pools。</summary>
        [Summary("池子根目录，缺省 Pools")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>仓库根目录，缺省当前目录。</summary>
        [Summary("仓库根目录，缺省当前目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>写完信封后是否立即跑一轮入站，缺省 true。</summary>
        [Summary("写完信封后是否立即跑一轮入站，缺省 true")]
        [DefaultValue(true)]
        public bool AutoPull { get; set; }
    }

    /// <summary>
    /// 面板建需求命令 pool.draft 的 CLI 入口：把表单落成一条 panel 信封写入收件箱，
    /// AutoPull 时接着按要求 schema 跑一轮入站，并只报本份信封的处理结果。
    /// </summary>
    public static class PoolDraftCommands
    {
        /// <summary>
        /// 面板建需求：先把表单字段落成信封写入收件箱；AutoPull 为 true 时紧跟一轮入站，
        /// 本份信封被拒收或无法解析时整条命令判失败，其余信封的结果不算本命令的成败。
        /// </summary>
        /// <param name="arguments">面板建需求命令参数。</param>
        [EditorCommand("pool.draft")]
        [Summary("面板建需求：把表单落成信封，可选紧跟一轮入站")]
        public static CommandResult Draft(PoolDraftArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Title))
            {
                return CommandResult.Failure("参数 Title 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Kind))
            {
                return CommandResult.Failure("参数 Kind 为必填项");
            }

            var repositoryRoot = ResolveRoot(arguments.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var poolRoot = ResolveRoot(arguments.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            var acceptanceCriteria = SplitLines(arguments.AcceptanceCriteria);
            var extraFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["目标"] = arguments.Goal ?? "",
                ["玩法"] = arguments.Gameplay ?? "",
                ["现状"] = arguments.Current ?? "",
                ["期望"] = arguments.Expected ?? "",
                ["实际"] = arguments.Actual ?? "",
                ["复现步骤"] = arguments.ReproSteps ?? ""
            };

            string filePath;
            try
            {
                filePath = PanelDraftWriter.Write(
                    poolRoot,
                    arguments.Submitter ?? "",
                    arguments.Title.Trim(),
                    arguments.Kind.Trim(),
                    arguments.Description ?? "",
                    acceptanceCriteria,
                    arguments.Epic ?? "",
                    extraFields,
                    DateTimeOffset.Now);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"信封写盘失败：{exception.Message}");
            }

            var lines = new List<string> { $"信封已写入：{Relativize(filePath, poolRoot)}" };
            if (!arguments.AutoPull)
            {
                return CommandResult.Success("信封已写入", lines);
            }

            IReadOnlyList<IntakeOutcome> outcomes;
            try
            {
                var schema = PoolSchemaLoader.Load(poolRoot, "需求");
                outcomes = RequirementIntake.Run(repositoryRoot, poolRoot, schema, DateTimeOffset.Now);
            }
            catch (FileNotFoundException exception)
            {
                return CommandResult.Failure(exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"入站失败：{exception.Message}");
            }

            // 按来源文件路径匹配，只取本份信封的处理结果。
            IntakeOutcome thisOne = null;
            foreach (var outcome in outcomes)
            {
                if (string.Equals(outcome.SourceFilePath, filePath, StringComparison.Ordinal))
                {
                    thisOne = outcome;
                    break;
                }
            }

            if (thisOne == null)
            {
                lines.Add("注意：本次的信封没有出现在入站结果里，请手动重跑 pool.pull");
                return CommandResult.Failure("本份信封未处理", lines);
            }

            lines.Add(thisOne.ToDisplayText());
            if (thisOne.Decision == IntakeDecision.Rejected)
            {
                return CommandResult.Failure($"本份信封被拒收：{thisOne.Message}", lines);
            }

            if (thisOne.Decision == IntakeDecision.Unreadable)
            {
                return CommandResult.Failure($"本份信封无法解析：{thisOne.Message}", lines);
            }

            return CommandResult.Success($"本份信封处理完成：{thisOne.Message}", lines);
        }

        /// <summary>按换行分隔的一段文本拆成行列表：去掉空行与首尾空白。</summary>
        private static IReadOnlyList<string> SplitLines(string text)
        {
            return (text ?? "")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        // 五条命令共用的根目录解析：空白取默认值，转绝对路径，目录不存在即失败。
        // 成功时返回绝对路径、failureMessage 为空串；失败时返回 null、failureMessage 为中文原因。
        private static string ResolveRoot(string value, string fallback, string parameterName, string displayName, out string failureMessage)
        {
            failureMessage = "";
            var root = string.IsNullOrWhiteSpace(value) ? fallback : value;

            string absoluteRoot;
            try
            {
                absoluteRoot = Path.GetFullPath(root);
            }
            catch (Exception exception)
            {
                failureMessage = $"参数 {parameterName} 无法解析为绝对路径：{exception.Message}";
                return null;
            }

            if (!Directory.Exists(absoluteRoot))
            {
                failureMessage = $"{displayName}目录不存在：{absoluteRoot}";
                return null;
            }

            return absoluteRoot;
        }

        /// <summary>把绝对值路径改成相对给定基准路径的可读相对路径；不在基准内则原样返回。</summary>
        private static string Relativize(string path, string basePath)
        {
            var fullBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullBase, StringComparison.Ordinal)
                ? fullPath.Substring(fullBase.Length)
                : fullPath;
        }
    }
}
