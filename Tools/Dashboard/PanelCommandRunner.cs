using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Template.Toolkit.Dashboard
{
    /// <summary>一次面板命令执行的结局：是否放行、退出码、输出与拒绝原因。</summary>
    public sealed class PanelCommandOutcome
    {
        /// <summary>
        /// 构造一次命令执行的结局。
        /// </summary>
        /// <param name="isAllowed">是否通过白名单并实际执行。</param>
        /// <param name="exitCode">进程退出码；未执行或超时终止时为 -1。</param>
        /// <param name="output">标准输出与标准错误拼起来的文本，截断到前 20000 字符。</param>
        /// <param name="rejectReason">白名单拒绝或未配置宿主的原因；执行过时为空串。</param>
        public PanelCommandOutcome(bool isAllowed, int exitCode, string output, string rejectReason)
        {
            IsAllowed = isAllowed;
            ExitCode = exitCode;
            Output = output ?? "";
            RejectReason = rejectReason ?? "";
        }

        /// <summary>是否通过白名单并实际执行。</summary>
        [JsonPropertyName("允许")]
        public bool IsAllowed { get; }

        /// <summary>进程退出码；未执行或超时终止时为 -1。</summary>
        [JsonPropertyName("退出码")]
        public int ExitCode { get; }

        /// <summary>标准输出与标准错误拼起来的文本，截断到前 20000 字符。</summary>
        [JsonPropertyName("输出")]
        public string Output { get; }

        /// <summary>白名单拒绝或未配置宿主的原因；执行过时为空串。</summary>
        [JsonPropertyName("原因")]
        public string RejectReason { get; }
    }

    /// <summary>
    /// 面板命令执行器：真去起 CLI 子进程跑命令宿主。
    /// 面板传来的是一整行 &lt;命令名&gt; --键 值 …，而命令宿主的 run 只吃
    /// &lt;命令名&gt; --arguments-file &lt;json 路径&gt;，所以这里先把命令行拆成参数对象、
    /// 落成一份临时 JSON，再把那份文件的路径喂给宿主，跑完即删。
    /// 先过白名单，不过不起进程；进程超时杀树终止；输出拼标准输出与标准错误并截断。
    /// </summary>
    public sealed class PanelCommandRunner
    {
        private static readonly JsonSerializerOptions ArgumentsJsonOptions =
            new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

        private readonly string _commandHostProjectPath;

        /// <summary>
        /// 构造命令执行器；宿主工程路径传空白时记下来，之后每次调用都返回「未配置命令宿主」的失败结果，不抛。
        /// </summary>
        /// <param name="commandHostProjectPath">命令宿主工程的 .csproj 路径。</param>
        public PanelCommandRunner(string commandHostProjectPath)
        {
            _commandHostProjectPath = commandHostProjectPath ?? "";
        }

        /// <summary>
        /// 执行一条命令行。
        /// 白名单不过直接返回拒绝结果，不起进程；通过则起 dotnet run 子进程，等待上限 120 秒，
        /// 超时杀掉进程树并返回固定失败结果。输出为标准输出 + 标准错误拼起来截断到前 20000 字符。
        /// </summary>
        /// <param name="commandLine">面板传来的整条命令行。</param>
        public PanelCommandOutcome Run(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(_commandHostProjectPath))
            {
                return new PanelCommandOutcome(false, -1, "", "未配置命令宿主");
            }

            if (!PanelCommandWhitelist.IsAllowed(commandLine, out _, out var rejectReason))
            {
                return new PanelCommandOutcome(false, -1, "", rejectReason);
            }

            var commandName = ParseCommandLine(commandLine, out var argumentsJson);
            var argumentsFilePath = Path.Combine(
                Path.GetTempPath(),
                "面板命令-" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                File.WriteAllText(argumentsFilePath, argumentsJson, new UTF8Encoding(false));
                return RunHost(commandName, argumentsFilePath);
            }
            finally
            {
                try
                {
                    File.Delete(argumentsFilePath);
                }
                catch (IOException)
                {
                    // 临时参数文件删不掉不影响这次调用的结论，留给系统临时目录自清。
                }
            }
        }

        /// <summary>
        /// 执行一条结构化命令：命令名过同一个白名单，参数 JSON 原样落临时文件喂给宿主。
        /// 多行文本（描述、验收标准）进不了命令行拆解，走这条通道。
        /// </summary>
        /// <param name="commandName">命令名，如 pool.draft。</param>
        /// <param name="argumentsJson">参数对象的 JSON 文本。</param>
        public PanelCommandOutcome RunWithArguments(string commandName, string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(_commandHostProjectPath))
            {
                return new PanelCommandOutcome(false, -1, "", "未配置命令宿主");
            }

            if (!PanelCommandWhitelist.IsAllowed(commandName ?? "", out _, out var rejectReason))
            {
                return new PanelCommandOutcome(false, -1, "", rejectReason);
            }

            var argumentsFilePath = Path.Combine(
                Path.GetTempPath(),
                "面板命令-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(argumentsFilePath, argumentsJson ?? "{}", new UTF8Encoding(false));
                return RunHost(commandName, argumentsFilePath);
            }
            finally
            {
                try
                {
                    File.Delete(argumentsFilePath);
                }
                catch (IOException)
                {
                    // 临时参数文件删不掉不影响这次调用的结论，留给系统临时目录自清。
                }
            }
        }

        /// <summary>
        /// 把一整行 &lt;命令名&gt; --键 值 … 拆成命令名与参数 JSON。
        /// 后面不跟值的 --键 视为布尔真；整数与 true/false 按原类型写，其余一律写成字符串。
        /// </summary>
        /// <param name="commandLine">面板传来的整条命令行。</param>
        /// <param name="argumentsJson">拆出来的参数对象的 JSON 文本。</param>
        internal static string ParseCommandLine(string commandLine, out string argumentsJson)
        {
            var pieces = commandLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var commandName = pieces.Length > 0 ? pieces[0] : "";
            var arguments = new JsonObject();

            for (var index = 1; index < pieces.Length; index++)
            {
                if (!pieces[index].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = pieces[index].Substring(2);
                if (key.Length == 0)
                {
                    continue;
                }

                var hasValue = index + 1 < pieces.Length
                    && !pieces[index + 1].StartsWith("--", StringComparison.Ordinal);
                if (!hasValue)
                {
                    arguments[key] = JsonValue.Create(true);
                    continue;
                }

                arguments[key] = ToJsonValue(pieces[index + 1]);
                index++;
            }

            argumentsJson = arguments.ToJsonString(ArgumentsJsonOptions);
            return commandName;
        }

        private static JsonNode ToJsonValue(string raw)
        {
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(true);
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(false);
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return JsonValue.Create(number);
            }

            return JsonValue.Create(raw);
        }

        private PanelCommandOutcome RunHost(string commandName, string argumentsFilePath)
        {
            var startInfo = BuildStartInfo(commandName, argumentsFilePath);
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return new PanelCommandOutcome(false, -1, "", "命令宿主启动失败");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // 进程已自行退出时再 Kill 会抛异常，忽略即可。
                    }

                    try
                    {
                        Task.WaitAll(stdoutTask, stderrTask);
                    }
                    catch (Exception)
                    {
                        // 读任务异常不影响返回超时结论。
                    }

                    return new PanelCommandOutcome(false, -1, "命令超过 120 秒未结束，已终止", "");
                }

                Task.WaitAll(stdoutTask, stderrTask);
                var output = Truncate(stdoutTask.Result + stderrTask.Result);
                if (process.ExitCode != 0)
                {
                    // 「宿主没编译过」这一类失败，光把 MSBuild 的英文报错甩到面板上，
                    // 看的人还得自己猜下一步。猜得出来的就替他说出来。
                    var hint = DescribeMissingBuildOutput(output);
                    if (hint != null)
                    {
                        output = hint + Environment.NewLine + Environment.NewLine + output;
                    }
                }

                return new PanelCommandOutcome(true, process.ExitCode, output, "");
            }
        }

        /// <summary>
        /// 拼 dotnet run 的进程启动参数；全部走参数列表，绝不拼接 shell。
        ///
        /// <para>
        /// 带 <c>--no-build</c> 是必需的，不是提速：命令宿主的工程引用链里有本看板工程
        /// （它要用 <see cref="PipelineRunner"/> 与 <see cref="PipelineDefinition"/>——
        /// 这两个类跟 HTTP 服务毫无关系，只是当初放错了程序集）。看板一跑起来就锁着自己的
        /// 输出文件，不带这个开关的话，MSBuild 会先去重编译那条链、复制不动被锁的文件、
        /// 整条命令以退出码 1 收场。**面板上每一个按钮都会这么死**——裁决、批准、抽查、
        /// 试跑、命令台全在内。这件事从面板有按钮那天起就是坏的，一直没人真点过一次。
        /// </para>
        /// <para>
        /// 代价说清楚：用的是已经编译好的产物。刚改完命令宿主又没编译的话，这里跑的是旧代码。
        /// 所以产物缺失时不能装作没事——见 <see cref="DescribeMissingBuildOutput"/>。
        /// 真正的修法是把那两个类挪出看板程序集、断掉命令宿主对看板的引用，那是另一批的事。
        /// </para>
        /// </summary>
        /// <param name="commandName">命令名。</param>
        /// <param name="argumentsFilePath">临时参数文件路径。</param>
        private ProcessStartInfo BuildStartInfo(string commandName, string argumentsFilePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(_commandHostProjectPath);
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--verbosity");
            startInfo.ArgumentList.Add("quiet");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(commandName);
            startInfo.ArgumentList.Add("--arguments-file");
            startInfo.ArgumentList.Add(argumentsFilePath);
            return startInfo;
        }

        /// <summary>
        /// 判断一次失败是不是「命令宿主还没编译过」，是就给一句人能照着做的话。
        /// 不是这一类返回 null——把别的失败也说成没编译，是拿一个猜测盖住真原因。
        /// </summary>
        /// <param name="output">子进程的全部输出。</param>
        internal static string DescribeMissingBuildOutput(string output)
        {
            if (output == null)
            {
                return null;
            }

            var looksMissing = output.Contains("--no-build", StringComparison.Ordinal)
                || output.Contains("MSB1009", StringComparison.Ordinal)
                || output.Contains("not find", StringComparison.OrdinalIgnoreCase)
                || output.Contains("找不到", StringComparison.Ordinal);
            if (!looksMissing)
            {
                return null;
            }

            return "命令宿主的编译产物不在（面板用 --no-build 起它，理由见 BuildStartInfo 的注释）。"
                + "先跑一次 dotnet build Solutions/Template.sln 再回来点——"
                + "注意跑之前要先停掉看板进程，它锁着自己的输出文件。";
        }

        /// <summary>把输出截断到前 20000 字符；超了在末尾补一行截断提示。</summary>
        private static string Truncate(string output)
        {
            const int maximumLength = 20000;
            if (output.Length <= maximumLength)
            {
                return output;
            }

            return output.Substring(0, maximumLength) + Environment.NewLine + "……输出截断";
        }
    }
}
