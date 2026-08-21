using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.Mcp
{
    // 写操作安全底线（方案模块 5）：本层只把命令层转发成 MCP 工具，命令层的每一处产出都落文件系统，
    // 天然被门禁的白名单与基线锁覆盖。Handle 内保持没有任何直接改资产的旁路。
    /// <summary>把命令层转发成 MCP 工具的 JSON-RPC 请求处理器。</summary>
    public sealed class McpRequestHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly IReadOnlyList<CommandDescriptor> _commands;

        /// <summary>用要扫描的程序集构造处理器。</summary>
        /// <param name="assemblies">要反射扫描命令的程序集。</param>
        public McpRequestHandler(params Assembly[] assemblies)
        {
            _commands = CommandRegistry.ScanAssemblies(assemblies);
        }

        /// <summary>处理一条单行 JSON-RPC 请求，返回一条单行 JSON 响应。</summary>
        /// <param name="requestJson">请求 JSON。</param>
        public string Handle(string requestJson)
        {
            using var request = JsonDocument.Parse(requestJson);
            var root = request.RootElement;

            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString()
                : null;
            var id = root.TryGetProperty("id", out var idElement)
                ? idElement.GetRawText()
                : "null";

            switch (method)
            {
                case "initialize":
                    return Success(id, new
                    {
                        protocolVersion = "2024-11-05",
                        serverInfo = new { name = "template-project-mcp", version = "0.1.0" },
                        capabilities = new { tools = new { } }
                    });

                case "tools/list":
                    return Success(id, BuildToolsList());

                case "tools/call":
                    return HandleCall(id, root);

                default:
                    return Error(id, -32601, $"未知方法：{method}");
            }
        }

        private object BuildToolsList()
        {
            var tools = _commands.Select(command => new
            {
                name = command.CommandName.Replace('.', '_'),
                description = command.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = BuildProperties(command),
                    required = command.ParameterSchemas
                        .Where(parameter => parameter.IsRequired)
                        .Select(parameter => parameter.ParameterName)
                        .ToArray()
                }
            }).ToArray();

            return new { tools };
        }

        private static Dictionary<string, object> BuildProperties(CommandDescriptor command)
        {
            var properties = new Dictionary<string, object>();
            foreach (var parameter in command.ParameterSchemas)
            {
                properties[parameter.ParameterName] = new
                {
                    type = MapType(parameter.ParameterTypeName),
                    description = parameter.Description
                };
            }

            return properties;
        }

        private static string MapType(string parameterTypeName)
        {
            switch (parameterTypeName)
            {
                case "Int32":
                case "Int64":
                    return "integer";
                case "Single":
                case "Double":
                    return "number";
                case "Boolean":
                    return "boolean";
                default:
                    return "string";
            }
        }

        private string HandleCall(string id, JsonElement root)
        {
            var hasParams = root.TryGetProperty("params", out var paramsElement)
                && paramsElement.ValueKind == JsonValueKind.Object;
            var toolName = hasParams && paramsElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrEmpty(toolName))
            {
                return Error(id, -32602, "缺少 params.name");
            }

            // 工具名由命令名的 . 换成 _ 得来，这里换回去即可定位命令。
            var commandName = toolName.Replace('_', '.');
            var descriptor = _commands.FirstOrDefault(command => command.CommandName == commandName);
            if (descriptor == null)
            {
                return Error(id, -32602, $"未知工具：{toolName}");
            }

            // 走绑定器而不是裸反序列化：MCP 这条路也要吃到 [DefaultValue] 填的值，
            // 否则同一条命令从命令行调和从 MCP 调会拿到两套不同的参数。
            var argumentsJson = hasParams
                && paramsElement.TryGetProperty("arguments", out var argumentsElement)
                && argumentsElement.ValueKind == JsonValueKind.Object
                ? argumentsElement.GetRawText()
                : "{}";
            // 先校验再绑定，与命令宿主同一条路（Program.cs）：
            // 认不出来的参数键要当场报错，不许静默忽略——两条入口的行为必须一样，
            // 否则「从 MCP 调」与「从命令行调」会给出两种结果。
            var diagnostics = CommandArgumentValidator.Validate(descriptor, argumentsJson);
            if (diagnostics.Count > 0)
            {
                // 参数问题走 isError 的工具结果，不走 JSON-RPC 协议错误：
                // 协议错误是「这个请求本身不合法」，而参数写错属于「工具跑失败了」，
                // 调用方要看到的是那四要素诊断，不是一个协议码。
                var lines = new List<string> { $"参数校验失败，问题 {diagnostics.Count} 条" };
                lines.AddRange(diagnostics.Select(diagnostic =>
                    $"位置：{diagnostic.Location}；原因：{diagnostic.Reason}；修复：{diagnostic.FixAction}"));
                var failure = new
                {
                    content = new[] { new { type = "text", text = BuildDiagnosticText(lines) } },
                    isError = true
                };
                return Success(id, failure);
            }

            var arguments = CommandArgumentBinder.Bind(descriptor, argumentsJson);

            CommandResult commandResult;
            try
            {
                commandResult = descriptor.Invoke(arguments);
            }
            catch (Exception exception)
            {
                var inner = exception is TargetInvocationException invocationException && invocationException.InnerException != null
                    ? invocationException.InnerException
                    : exception;
                return Error(id, -32603, $"命令执行失败：{inner.Message}");
            }

            var callResult = new
            {
                content = new[] { new { type = "text", text = BuildResultText(commandResult) } },
                isError = !commandResult.IsSuccess
            };
            return Success(id, callResult);
        }

        /// <summary>把诊断行拼成一段文本；换行符从 Environment 取，别在源码里写字面换行。</summary>
        private static string BuildDiagnosticText(List<string> lines)
        {
            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildResultText(CommandResult commandResult)
        {
            var lines = new List<string> { commandResult.Message };
            lines.AddRange(commandResult.OutputLines);
            return string.Join("\n", lines);
        }

        private static string Success(string id, object result)
        {
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";
        }

        private static string Error(string id, int code, string message)
        {
            var errorJson = JsonSerializer.Serialize(new { code, message }, JsonOptions);
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{errorJson}}}";
        }
    }
}
