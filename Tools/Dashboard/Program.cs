using System;
using System.Text;

namespace Template.Toolkit.Dashboard
{
    /// <summary>看板命令行入口：起 HTTP 服务，并把标准输入的每一行当作日志推送给浏览器。</summary>
    public static class Program
    {
        /// <summary>解析端口参数、启动服务、逐行转发标准输入，直到标准输入关闭。</summary>
        /// <param name="args">命令行参数，支持 --port &lt;端口&gt;。</param>
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var port = ReadPort(args);
            var channel = new LogEventChannel();
            using (var server = new DashboardServer(channel, port))
            {
                server.Start();
                Console.WriteLine($"看板已启动：http://localhost:{server.Port}/");

                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    channel.Publish(line);
                }
            }

            return 0;
        }

        private static int ReadPort(string[] args)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == "--port" && int.TryParse(args[index + 1], out var parsedPort))
                {
                    return parsedPort;
                }
            }

            return 0;
        }
    }
}
