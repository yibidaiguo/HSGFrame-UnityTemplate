using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Mcp;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>MCP 转发层覆盖测试：tools/list 覆盖全部命令，tools/call 转发链路通畅。</summary>
    public class McpToolCoverageTests
    {
        [Fact]
        public void ToolListCountMatchesCommandCount()
        {
            var handler = CreateHandler();
            var commands = CommandRegistry.ScanAssemblies(typeof(Template.Toolkit.CommandHost.Program).Assembly);

            var tools = ListTools(handler);

            Assert.Equal(commands.Count, tools.GetArrayLength());
        }

        [Fact]
        public void EveryCommandAppearsAsTool()
        {
            var handler = CreateHandler();
            var commands = CommandRegistry.ScanAssemblies(typeof(Template.Toolkit.CommandHost.Program).Assembly);
            var tools = ListTools(handler);
            var toolNames = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToHashSet();

            foreach (var command in commands)
            {
                Assert.Contains(command.CommandName.Replace('.', '_'), toolNames);
            }
        }

        [Fact]
        public void EveryToolHasDescription()
        {
            var handler = CreateHandler();
            var tools = ListTools(handler);

            foreach (var tool in tools.EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
            }
        }

        [Fact]
        public void EveryToolRequiredMatchesCommandRequiredParameters()
        {
            var handler = CreateHandler();
            var commands = CommandRegistry.ScanAssemblies(typeof(Template.Toolkit.CommandHost.Program).Assembly);
            var tools = ListTools(handler);

            foreach (var command in commands)
            {
                var tool = tools.EnumerateArray().Single(item =>
                    item.GetProperty("name").GetString() == command.CommandName.Replace('.', '_'));
                var required = tool.GetProperty("inputSchema").GetProperty("required")
                    .EnumerateArray().Select(element => element.GetString()).ToHashSet();
                var expected = command.ParameterSchemas.Where(parameter => parameter.IsRequired)
                    .Select(parameter => parameter.ParameterName).ToHashSet();

                Assert.True(expected.SetEquals(required), $"命令 {command.CommandName} 的必填参数与工具 required 不一致");
            }
        }

        [Fact]
        public void ToolCallReportsErrorForMissingTemplateRoot()
        {
            var handler = CreateHandler();
            var nonexistentRoot = (Path.GetTempPath() + "index-check-nonexistent-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');

            var response = handler.Handle(
                $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"index_check\",\"arguments\":{{\"TemplateRoot\":\"{nonexistentRoot}\"}}}}}}");

            Assert.Contains("\"isError\":true", response);
            Assert.Contains("校验索引新鲜度失败", response);
        }

        [Fact]
        public void ToolCallUnknownToolReturnsErrorNotException()
        {
            var handler = CreateHandler();

            var response = handler.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"no_such_tool\",\"arguments\":{}}}");

            Assert.Contains("-32602", response);
            Assert.DoesNotContain("内部错误", response);
        }

        private static McpRequestHandler CreateHandler()
        {
            return new McpRequestHandler(typeof(Template.Toolkit.CommandHost.Program).Assembly);
        }

        private static JsonElement ListTools(McpRequestHandler handler)
        {
            var response = handler.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}");
            using var document = JsonDocument.Parse(response);
            return document.RootElement.GetProperty("result").GetProperty("tools").Clone();
        }
    }
}
