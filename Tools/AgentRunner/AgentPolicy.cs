using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.AgentRunner
{
    /// <summary>
    /// 执行器围栏策略：命令白名单前缀、写盘拒绝清单与各项上限。
    /// 从 <c>Tools/AgentRunner/Config/agent-policy.json</c> 读入（进 git，由人维护）——
    /// 执行端自己被拒绝写这份文件与角色档案，围栏不许由被围的一方放宽。
    /// </summary>
    public sealed class AgentPolicy
    {
        /// <summary>
        /// 构造一份策略。
        /// </summary>
        /// <param name="commandAllowPrefixes">命令白名单前缀（序数匹配，命中任一前缀才许跑）。</param>
        /// <param name="writeDenyPrefixes">写盘拒绝的仓库相对路径前缀（正斜杠形式）。</param>
        /// <param name="writeDenyFiles">写盘拒绝的仓库相对文件路径（正斜杠形式，全字匹配）。</param>
        /// <param name="commandTimeoutSeconds">单条命令的超时秒数。</param>
        /// <param name="commandOutputLimit">命令输出保留的最大字符数，超出截头留尾。</param>
        /// <param name="fileReadLimit">单次读文件返回的最大字符数。</param>
        public AgentPolicy(
            IReadOnlyList<string> commandAllowPrefixes,
            IReadOnlyList<string> writeDenyPrefixes,
            IReadOnlyList<string> writeDenyFiles,
            int commandTimeoutSeconds,
            int commandOutputLimit,
            int fileReadLimit)
        {
            CommandAllowPrefixes = commandAllowPrefixes ?? Array.Empty<string>();
            WriteDenyPrefixes = writeDenyPrefixes ?? Array.Empty<string>();
            WriteDenyFiles = writeDenyFiles ?? Array.Empty<string>();
            CommandTimeoutSeconds = commandTimeoutSeconds;
            CommandOutputLimit = commandOutputLimit;
            FileReadLimit = fileReadLimit;
        }

        /// <summary>命令白名单前缀（序数匹配，命中任一前缀才许跑）。</summary>
        public IReadOnlyList<string> CommandAllowPrefixes { get; }

        /// <summary>写盘拒绝的仓库相对路径前缀（正斜杠形式）。</summary>
        public IReadOnlyList<string> WriteDenyPrefixes { get; }

        /// <summary>写盘拒绝的仓库相对文件路径（正斜杠形式，全字匹配）。</summary>
        public IReadOnlyList<string> WriteDenyFiles { get; }

        /// <summary>单条命令的超时秒数。</summary>
        public int CommandTimeoutSeconds { get; }

        /// <summary>命令输出保留的最大字符数，超出截头留尾。</summary>
        public int CommandOutputLimit { get; }

        /// <summary>单次读文件返回的最大字符数。</summary>
        public int FileReadLimit { get; }

        /// <summary>策略文件的仓库相对路径。</summary>
        public const string PolicyRelativePath = "Tools/AgentRunner/Config/agent-policy.json";

        /// <summary>
        /// 从仓库根读策略文件。文件缺失或坏掉都算失败——没有围栏就不许放执行端出去跑。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="policy">读出的策略；失败时为 null。</param>
        /// <param name="failureReason">失败原因；成功时为空串。</param>
        public static bool TryLoad(string repositoryRoot, out AgentPolicy policy, out string failureReason)
        {
            policy = null;
            failureReason = "";
            var filePath = Path.Combine(repositoryRoot ?? "", "Tools", "AgentRunner", "Config", "agent-policy.json");
            if (!File.Exists(filePath))
            {
                failureReason = $"围栏策略文件不存在：{filePath}";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                failureReason = $"围栏策略文件不是合法 JSON：{exception.Message}";
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    failureReason = "围栏策略文件顶层必须是对象";
                    return false;
                }

                policy = new AgentPolicy(
                    ReadStringArray(root, "命令白名单前缀"),
                    ReadStringArray(root, "写盘拒绝前缀"),
                    ReadStringArray(root, "写盘拒绝文件"),
                    ReadInt(root, "命令超时秒", 600),
                    ReadInt(root, "命令输出上限字符", 8000),
                    ReadInt(root, "读文件上限字符", 60000));
                return true;
            }
        }

        /// <summary>判定一条命令是否在白名单内（任一前缀序数命中即放行）。</summary>
        /// <param name="commandText">要跑的整条命令。</param>
        public bool IsCommandAllowed(string commandText)
        {
            var trimmed = (commandText ?? "").TrimStart();
            return CommandAllowPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>判定一个仓库相对路径（正斜杠形式）是否被写盘拒绝。</summary>
        /// <param name="relativePath">仓库相对路径，正斜杠形式。</param>
        public bool IsWriteDenied(string relativePath)
        {
            var normalized = (relativePath ?? "").Replace('\\', '/');
            if (WriteDenyFiles.Any(file => string.Equals(file, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return WriteDenyPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> ReadStringArray(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(text => text.Length > 0)
                .ToList();
        }

        private static int ReadInt(JsonElement root, string key, int fallback)
        {
            if (root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return element.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return fallback;
        }
    }
}
