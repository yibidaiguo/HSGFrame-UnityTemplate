using Template.Toolkit.Mcp;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>MCP 转发层测试。</summary>
    public class McpRequestHandlerTests
    {
        [Fact]
        public void ListsToolsWithInputSchema()
        {
            var handler = CreateHandler();

            var response = handler.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}");

            Assert.Contains("compile_check", response);
            Assert.Contains("inputSchema", response);
        }

        [Fact]
        public void ReturnsMethodNotFoundForUnknownMethod()
        {
            var handler = CreateHandler();

            var response = handler.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"nonexistent\",\"params\":{}}");

            Assert.Contains("-32601", response);
        }

        [Fact]
        public void CallsToolAndReportsErrorForFailedCommand()
        {
            var handler = CreateHandler();

            var response = handler.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"index_check\",\"arguments\":{\"RepositoryRoot\":\"\"}}}");

            Assert.Contains("\"isError\":true", response);
            Assert.Contains("必填", response);
        }

        private static McpRequestHandler CreateHandler()
        {
            return new McpRequestHandler(typeof(Template.Toolkit.CommandHost.Program).Assembly);
        }
    }
}
