using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.AgentRunner
{
    /// <summary>一次分派的结果。</summary>
    public sealed class AgentDispatchResult
    {
        /// <summary>
        /// 构造一份结果。
        /// </summary>
        /// <param name="succeeded">分派是否走完（模型正常收尾；内容好坏由派活方验收）。</param>
        /// <param name="reportText">回报正文（可能按上限截过，全文在报告文件里）。</param>
        /// <param name="failureReason">没走完时的原因。</param>
        /// <param name="rounds">轮数。</param>
        /// <param name="totalTokens">token 总数。</param>
        /// <param name="workspaceChanged">跑完后工作区与跑前是否不同。</param>
        /// <param name="transcriptPath">转录文件路径。</param>
        /// <param name="reportPath">完整回报文件路径。</param>
        public AgentDispatchResult(
            bool succeeded,
            string reportText,
            string failureReason,
            int rounds,
            int totalTokens,
            bool workspaceChanged,
            string transcriptPath,
            string reportPath)
        {
            Succeeded = succeeded;
            ReportText = reportText ?? "";
            FailureReason = failureReason ?? "";
            Rounds = rounds;
            TotalTokens = totalTokens;
            WorkspaceChanged = workspaceChanged;
            TranscriptPath = transcriptPath ?? "";
            ReportPath = reportPath ?? "";
        }

        /// <summary>分派是否走完（模型正常收尾；内容好坏由派活方验收）。</summary>
        public bool Succeeded { get; }

        /// <summary>回报正文（可能按上限截过，全文在报告文件里）。</summary>
        public string ReportText { get; }

        /// <summary>没走完时的原因。</summary>
        public string FailureReason { get; }

        /// <summary>轮数。</summary>
        public int Rounds { get; }

        /// <summary>token 总数。</summary>
        public int TotalTokens { get; }

        /// <summary>跑完后工作区与跑前是否不同。</summary>
        public bool WorkspaceChanged { get; }

        /// <summary>转录文件路径。</summary>
        public string TranscriptPath { get; }

        /// <summary>完整回报文件路径。</summary>
        public string ReportPath { get; }
    }

    /// <summary>
    /// 分派编排：角色档案 + 任务书 → 工具循环 → 回报与工作区指纹。
    /// 执行配置走 <c>local.json</c> 的「下游配置.oaicompat」与「执行后端密钥」——
    /// 与创作管线的执行后端同一个钱包、同一套协议（决策 80：按协议写，不按厂商写）。
    /// </summary>
    public static class AgentDispatch
    {
        /// <summary>四个角色名；档案住在 Tools/AgentRunner/Roles/&lt;角色&gt;.md。</summary>
        public static readonly string[] KnownRoles = { "implementer", "verifier", "operator", "explore" };

        /// <summary>
        /// 组一次分派要发的系统提示与任务书；组不出来（角色不存在、任务书不存在、配置缺）给原因。
        /// 拆出来是给干跑与测试用：不碰网络就能验证组装。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="roleName">角色名。</param>
        /// <param name="taskFilePath">任务书文件路径。</param>
        /// <param name="systemText">组好的系统提示。</param>
        /// <param name="taskText">任务书全文。</param>
        /// <param name="failureReason">失败原因。</param>
        public static bool TryAssemble(
            string repositoryRoot,
            string roleName,
            string taskFilePath,
            out string systemText,
            out string taskText,
            out string failureReason)
        {
            systemText = "";
            taskText = "";
            failureReason = "";

            if (!KnownRoles.Contains(roleName ?? ""))
            {
                failureReason = $"角色「{roleName}」不认识，只认：{string.Join("、", KnownRoles)}";
                return false;
            }

            var roleFile = Path.Combine(repositoryRoot, "Tools", "AgentRunner", "Roles", roleName + ".md");
            if (!File.Exists(roleFile))
            {
                failureReason = $"角色档案不存在：{roleFile}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(taskFilePath) || !File.Exists(taskFilePath))
            {
                failureReason = $"任务书文件不存在：{taskFilePath}";
                return false;
            }

            systemText = File.ReadAllText(roleFile) + "\n\n" + ToolProtocolText;
            taskText = File.ReadAllText(taskFilePath);
            return true;
        }

        /// <summary>
        /// 跑一次分派。传输与工具都在本进程内，跑完写完整回报到 Logs/agent/。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（绝对路径）。</param>
        /// <param name="roleName">角色名。</param>
        /// <param name="taskFilePath">任务书文件路径。</param>
        /// <param name="maxRounds">轮数上限。</param>
        /// <param name="maxReportChars">回报正文的字符上限，超出截头留尾（全文在报告文件里）。</param>
        /// <param name="modelOverride">模型名覆盖；空串用配置里的。</param>
        public static AgentDispatchResult Run(
            string repositoryRoot,
            string roleName,
            string taskFilePath,
            int maxRounds,
            int maxReportChars,
            string modelOverride)
        {
            if (!AgentPolicy.TryLoad(repositoryRoot, out var policy, out var policyReason))
            {
                return Failure(policyReason);
            }

            if (!TryAssemble(repositoryRoot, roleName, taskFilePath, out var systemText, out var taskText, out var assembleReason))
            {
                return Failure(assembleReason);
            }

            var settings = LocalBridgeSettings.Load(repositoryRoot);
            if (!settings.Loaded)
            {
                return Failure(settings.LoadFailureReason);
            }

            // driver 名不进代码：按「执行后端」port 查路由表，哪个 driver 挂着这个 port 就用哪个。
            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.TryResolvePort("执行后端", out var driverName, out var routeReason))
            {
                return Failure($"执行后端没有可用的 driver：{routeReason}");
            }

            if (!settings.TryGetDriverConfiguration(driverName, out var configuration))
            {
                return Failure($"local.json 里没有「下游配置.{driverName}」，执行后端没法调");
            }

            var endpoint = ReadString(configuration, "地址");
            var timeoutSeconds = ReadInt(configuration, "超时秒", 180);

            // 模型这一格可能配的是哨兵「自动」：那就从这个 driver 上次能力探测回来的清单里现挑，
            // 依据写进 stderr 的日志——挑了谁不说出来，这一档就成了黑箱。
            // chat/completions 必须带 model，所以这里**挑不出来就得失败**，
            // 不能像生图那样「不发 model 参数、由下游按自己的默认来」。
            var modelName = ModelSelection.Resolve(repositoryRoot, driverName, ReadString(configuration, "模型"), modelOverride ?? "", out var modelNote);
            if (modelNote.Length > 0)
            {
                Console.Error.WriteLine("agent.dispatch 模型：" + modelNote);
            }

            if (endpoint.Length == 0)
            {
                return Failure($"「下游配置.{driverName}」缺「地址」");
            }

            if (modelName.Length == 0)
            {
                return Failure($"执行后端 {driverName} 的模型没法确定：{(modelNote.Length == 0 ? $"「下游配置.{driverName}」缺「模型」" : modelNote)}");
            }

            if (!settings.TryGetSecret("执行后端密钥", out var secretKey) || secretKey.Length == 0)
            {
                return Failure("local.json 里没有「执行后端密钥」");
            }

            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var logDirectory = Path.Combine(repositoryRoot, "Logs", "agent");
            var transcriptPath = Path.Combine(logDirectory, $"{stamp}-{roleName}.jsonl");
            var reportPath = Path.Combine(logDirectory, $"{stamp}-{roleName}-report.md");

            var fingerprintBefore = WorkspaceFingerprint(repositoryRoot);

            // verifier 与 explore 是只读角色：连 write_file 工具都不给，机械强制而不是靠嘱咐。
            var allowWrite = roleName is "implementer" or "operator";
            var toolbox = new AgentToolbox(repositoryRoot, policy, allowWrite);
            var transport = new HttpChatTransport(endpoint, modelName, secretKey, timeoutSeconds);
            var loop = new AgentLoop(transport, toolbox, transcriptPath);
            var loopResult = loop.Run(systemText, taskText, maxRounds);

            var fingerprintAfter = WorkspaceFingerprint(repositoryRoot);
            var workspaceChanged = !string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal);

            try
            {
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(reportPath, loopResult.FinalText, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 报告写不进不吞结果，正文照样从返回值出去。
            }

            var reportText = TruncateKeepingTail(loopResult.FinalText, maxReportChars);
            return new AgentDispatchResult(
                loopResult.AbortReason.Length == 0,
                reportText,
                loopResult.AbortReason,
                loopResult.Rounds,
                loopResult.TotalTokens,
                workspaceChanged,
                transcriptPath,
                reportPath);
        }

        /// <summary>
        /// 工作区指纹：git status --porcelain 全文的 SHA256。verifier 跑完指纹变了 = 它动了工作区，
        /// 那一轮验证作废——这是原来「工作区哨兵」的同一道防线。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string WorkspaceFingerprint(string repositoryRoot)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("status");
                startInfo.ArgumentList.Add("--porcelain");
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return "";
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30_000);
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                return "";
            }
        }

        /// <summary>工具协议：拼在角色档案后面，四个角色共用一份，不在档案里各抄一遍。</summary>
        public const string ToolProtocolText =
            "## 你的工具\n"
            + "你通过函数调用使用四个工具，全部以仓库根为基准：\n"
            + "- read_file{path, start_line?, line_count?}：读文件；大文件按行号分段读，整读只会撞截断。\n"
            + "- write_file{path, content}：整份覆盖写；部分文件在写盘拒绝清单里，被拒了就写进回报，不要绕。\n"
            + "- list_directory{path}：列目录。\n"
            + "- run_command{command}：跑白名单命令（dotnet test/build、git 只读含 git grep、门禁脚本）；不在白名单的命令会被拒。\n"
            + "干完活把最终回报作为纯文本消息返回（不再带工具调用）。回报的形状按你角色档案「返回什么」那节写；\n"
            + "回报里不要有过程独白（「让我看看」「Let me…」这类），直接按形状输出。";

        private static AgentDispatchResult Failure(string reason)
        {
            return new AgentDispatchResult(false, "", reason, 0, 0, false, "", "");
        }

        private static string ReadString(System.Text.Json.JsonElement configuration, string key)
        {
            return configuration.ValueKind == System.Text.Json.JsonValueKind.Object
                && configuration.TryGetProperty(key, out var element)
                && element.ValueKind == System.Text.Json.JsonValueKind.String
                ? element.GetString() ?? ""
                : "";
        }

        private static int ReadInt(System.Text.Json.JsonElement configuration, string key, int fallback)
        {
            if (configuration.ValueKind == System.Text.Json.JsonValueKind.Object
                && configuration.TryGetProperty(key, out var element)
                && element.ValueKind == System.Text.Json.JsonValueKind.Number)
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

        private static string TruncateKeepingTail(string text, int limit)
        {
            var content = text ?? "";
            if (limit <= 0 || content.Length <= limit)
            {
                return content;
            }

            var omitted = content.Length - limit;
            return $"……（截断：前略 {omitted} 字符，全文见报告文件）\n" + content.Substring(omitted);
        }
    }
}
