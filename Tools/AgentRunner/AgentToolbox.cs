using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.AgentRunner
{
    /// <summary>
    /// 执行端可用的本地工具：读文件、写文件、列目录、跑白名单命令。
    /// 每个工具都过两道围栏：路径必须落在仓库根之内（防逃逸），写盘与命令再各查一遍策略。
    /// 工具结果一律是给模型看的文本；出错也回文本（说明被拒原因），不抛异常打断循环。
    /// </summary>
    public sealed class AgentToolbox
    {
        private readonly string _repositoryRoot;
        private readonly AgentPolicy _policy;

        /// <summary>
        /// 构造一个工具箱。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（绝对路径）。</param>
        /// <param name="policy">围栏策略。</param>
        /// <param name="allowWrite">是否提供 write_file。只读角色（verifier / explore）传 false，机械强制而不是靠嘱咐。</param>
        public AgentToolbox(string repositoryRoot, AgentPolicy policy, bool allowWrite)
        {
            _repositoryRoot = Path.GetFullPath(repositoryRoot ?? ".");
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            AllowWrite = allowWrite;
        }

        /// <summary>是否提供 write_file。</summary>
        public bool AllowWrite { get; }

        /// <summary>chat/completions 请求体里的 tools 数组；只读工具箱不声明 write_file。</summary>
        public JsonArray BuildToolDefinitions()
        {
            var definitions = new JsonArray
            {
                FunctionDefinition("read_file", "读一个仓库内文件的文本内容；大文件用 start_line/line_count 分段读，别整读",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "仓库相对路径，正斜杠" },
                            ["start_line"] = new JsonObject { ["type"] = "integer", ["description"] = "起始行号（1 起），省略从头读" },
                            ["line_count"] = new JsonObject { ["type"] = "integer", ["description"] = "最多读多少行，省略读到上限" }
                        },
                        ["required"] = new JsonArray { "path" }
                    })
            };
            if (AllowWrite)
            {
                definitions.Add(FunctionDefinition("write_file", "把整份文本写入仓库内文件（覆盖写；父目录不存在会自动建）",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "仓库相对路径，正斜杠" },
                            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "完整文件内容" }
                        },
                        ["required"] = new JsonArray { "path", "content" }
                    }));
            }

            definitions.Add(FunctionDefinition("list_directory", "列一个仓库内目录的子项（目录名带尾斜杠）",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "仓库相对路径，正斜杠；空串表示仓库根" }
                    },
                    ["required"] = new JsonArray { "path" }
                }));
            definitions.Add(FunctionDefinition("run_command", "在仓库根目录跑一条白名单命令，返回合并的输出与退出码",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["command"] = new JsonObject { ["type"] = "string", ["description"] = "整条命令，如 dotnet test Solutions/Template.sln" }
                    },
                    ["required"] = new JsonArray { "command" }
                }));
            return definitions;
        }

        /// <summary>
        /// 执行一次工具调用；未知工具名或参数不合法时返回说明文本，不抛异常。
        /// </summary>
        /// <param name="toolName">工具名。</param>
        /// <param name="argumentsJson">模型给的参数 JSON 文本。</param>
        public string Execute(string toolName, string argumentsJson)
        {
            JsonObject arguments;
            try
            {
                arguments = JsonNode.Parse(argumentsJson ?? "{}") as JsonObject;
            }
            catch (JsonException exception)
            {
                return "工具参数不是合法 JSON：" + exception.Message;
            }

            if (arguments == null)
            {
                return "工具参数必须是 JSON 对象";
            }

            switch (toolName)
            {
                case "read_file":
                    return ReadFile(ReadString(arguments, "path"), ReadInt(arguments, "start_line"), ReadInt(arguments, "line_count"));
                case "write_file":
                    return AllowWrite
                        ? WriteFile(ReadString(arguments, "path"), ReadString(arguments, "content"))
                        : "这个角色是只读的，write_file 不可用";
                case "list_directory":
                    return ListDirectory(ReadString(arguments, "path"));
                case "run_command":
                    return RunCommand(ReadString(arguments, "command"));
                default:
                    return $"没有叫「{toolName}」的工具";
            }
        }

        /// <summary>读文件；可按行号范围分段读（大文件整读只会撞截断，白费 token）；超出上限截头留尾并注明。</summary>
        /// <param name="relativePath">仓库相对路径。</param>
        /// <param name="startLine">起始行号（1 起）；0 或负数表示从头读。</param>
        /// <param name="lineCount">最多读多少行；0 或负数表示不按行数限。</param>
        public string ReadFile(string relativePath, int startLine = 0, int lineCount = 0)
        {
            if (!TryResolveInsideRepository(relativePath, out var fullPath, out var reason))
            {
                return reason;
            }

            if (!File.Exists(fullPath))
            {
                return $"文件不存在：{relativePath}";
            }

            string text;
            try
            {
                if (startLine > 0 || lineCount > 0)
                {
                    var lines = File.ReadAllLines(fullPath);
                    var skip = Math.Max(0, startLine - 1);
                    var take = lineCount > 0 ? lineCount : lines.Length;
                    var slice = lines.Skip(skip).Take(take).ToArray();
                    text = $"（第 {skip + 1} 行起，共 {slice.Length} 行，全文 {lines.Length} 行）\n" + string.Join("\n", slice);
                }
                else
                {
                    text = File.ReadAllText(fullPath);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return $"读不了 {relativePath}：{exception.Message}";
            }

            return TruncateKeepingTail(text, _policy.FileReadLimit);
        }

        /// <summary>写文件；先过路径围栏，再过写盘拒绝清单。</summary>
        /// <param name="relativePath">仓库相对路径。</param>
        /// <param name="content">完整文件内容。</param>
        public string WriteFile(string relativePath, string content)
        {
            if (!TryResolveInsideRepository(relativePath, out var fullPath, out var reason))
            {
                return reason;
            }

            var normalized = NormalizeRelative(fullPath);
            if (_policy.IsWriteDenied(normalized))
            {
                return $"写盘被围栏拒绝：{normalized} 在拒绝清单里，这类文件由派活方维护";
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? _repositoryRoot);
                File.WriteAllText(fullPath, content ?? "", new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return $"写不进 {normalized}：{exception.Message}";
            }

            return $"已写入 {normalized}（{(content ?? "").Length} 字符）";
        }

        /// <summary>列目录；目录名带尾斜杠，按序数序。</summary>
        /// <param name="relativePath">仓库相对路径；空串表示仓库根。</param>
        public string ListDirectory(string relativePath)
        {
            if (!TryResolveInsideRepository(string.IsNullOrEmpty(relativePath) ? "." : relativePath, out var fullPath, out var reason))
            {
                return reason;
            }

            if (!Directory.Exists(fullPath))
            {
                return $"目录不存在：{relativePath}";
            }

            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Select(entry => Directory.Exists(entry) ? Path.GetFileName(entry) + "/" : Path.GetFileName(entry))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            return entries.Count == 0 ? "（空目录）" : string.Join("\n", entries);
        }

        /// <summary>跑一条白名单命令：pwsh 无配置执行，工作目录钉在仓库根，超时必杀，输出截头留尾。</summary>
        /// <param name="commandText">整条命令。</param>
        public string RunCommand(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return "命令为空";
            }

            if (!_policy.IsCommandAllowed(commandText))
            {
                return $"命令被围栏拒绝（不在白名单前缀里）：{commandText}\n"
                    + "白名单前缀：" + string.Join("、", _policy.CommandAllowPrefixes);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(commandText);

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return "起不了 pwsh 进程";
                }

                var standardOutput = process.StandardOutput.ReadToEndAsync();
                var standardError = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(Math.Max(1, _policy.CommandTimeoutSeconds) * 1000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return $"命令超过 {_policy.CommandTimeoutSeconds} 秒未结束，已连子进程一起杀掉：{commandText}";
                }

                var combined = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
                return $"退出码={process.ExitCode}\n" + TruncateKeepingTail(combined, _policy.CommandOutputLimit);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                return $"命令执行失败：{exception.Message}";
            }
        }

        /// <summary>把绝对路径转成仓库相对的正斜杠形式。</summary>
        /// <param name="fullPath">仓库内的绝对路径。</param>
        public string NormalizeRelative(string fullPath)
        {
            return Path.GetRelativePath(_repositoryRoot, fullPath).Replace('\\', '/');
        }

        /// <summary>
        /// 把相对路径解析成仓库内的绝对路径；解析结果落在仓库根之外一律拒绝（防 ../ 逃逸）。
        /// </summary>
        /// <param name="relativePath">仓库相对路径。</param>
        /// <param name="fullPath">解析出的绝对路径。</param>
        /// <param name="reason">被拒时给模型看的原因。</param>
        public bool TryResolveInsideRepository(string relativePath, out string fullPath, out string reason)
        {
            fullPath = "";
            reason = "";
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                reason = "路径为空";
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(Path.Combine(_repositoryRoot, relativePath));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is PathTooLongException || exception is NotSupportedException)
            {
                reason = $"路径不合法：{exception.Message}";
                return false;
            }

            if (!fullPath.StartsWith(_repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, _repositoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"路径逃出仓库根，被拒绝：{relativePath}";
                return false;
            }

            return true;
        }

        private static JsonObject FunctionDefinition(string name, string description, JsonObject parameters)
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["parameters"] = parameters
                }
            };
        }

        private static string ReadString(JsonObject arguments, string key)
        {
            return arguments.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        private static int ReadInt(JsonObject arguments, string key)
        {
            return arguments.TryGetPropertyValue(key, out var node) && node is JsonValue value
                && value.TryGetValue<int>(out var number)
                ? number
                : 0;
        }

        /// <summary>超长文本截头留尾：结论通常在尾部（测试结果、报错摘要都在最后几行）。</summary>
        private static string TruncateKeepingTail(string text, int limit)
        {
            var content = text ?? "";
            if (limit <= 0 || content.Length <= limit)
            {
                return content;
            }

            var omitted = content.Length - limit;
            return $"……（截断：前略 {omitted} 字符）\n" + content.Substring(omitted);
        }
    }
}
