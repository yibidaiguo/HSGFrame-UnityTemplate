using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.Mcp
{
    /// <summary>项目 MCP 服务器入口：stdio 逐行读请求、逐行回响应。</summary>
    public static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>进入 stdio 循环，直到标准输入关闭。</summary>
        public static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var handler = new McpRequestHandler(typeof(Template.Toolkit.CommandHost.Program).Assembly);

            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string response;
                try
                {
                    response = handler.Handle(line);
                }
                catch (Exception exception)
                {
                    response = JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = (string)null,
                        error = new { code = -32603, message = $"内部错误：{exception.Message}" }
                    }, JsonOptions);
                }

                Console.WriteLine(response);
            }

            return 0;
        }
    }
}
