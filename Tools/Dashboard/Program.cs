using System;
using System.IO;
using System.Text;

namespace Template.Toolkit.Dashboard
{
    /// <summary>看板命令行入口：起 HTTP 服务，并把标准输入的每一行当作日志推送给浏览器。</summary>
    public static class Program
    {
        /// <summary>
        /// 解析参数、启动服务、逐行转发标准输入，直到标准输入关闭。
        /// 传 --stop-file 时改走常驻模式：不读标准输入，轮询停止文件，文件出现即退出——
        /// 后台起服务时没有可挂住的 stdin（管道一 EOF 服务当场退，踩过），停止文件是 assist.serve 已用的惯例。
        /// </summary>
        /// <param name="args">命令行参数，支持 --port &lt;端口&gt;、--repository-root &lt;仓库根&gt; 与 --stop-file &lt;路径&gt;。</param>
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var port = ReadPort(args);
            var repositoryRoot = ReadOption(args, "--repository-root") ?? FindRepositoryRoot();
            var poolRoot = repositoryRoot == null ? null : Path.Combine(repositoryRoot, "Pools");
            var commandHostProjectPath = repositoryRoot == null
                ? null
                : Path.Combine(repositoryRoot, "Tools", "Cli", "CommandHost", "CommandHost.csproj");

            var channel = new LogEventChannel();
            using (var server = new DashboardServer(channel, port, repositoryRoot, poolRoot, commandHostProjectPath))
            {
                server.Start();
                Console.WriteLine($"看板已启动：http://localhost:{server.Port}/");
                Console.WriteLine($"创作管线面板：http://localhost:{server.Port}/panel");
                if (repositoryRoot == null)
                {
                    // 面板十六页全靠现读仓库里的文件，找不到仓库根就只剩日志页能用——这件事必须说出来，
                    // 不然用户看到的是十六页齐刷刷的「取数据失败」，却不知道是没找到仓库根。
                    Console.WriteLine("知会：没找到仓库根（当前目录往上找不到 .git），面板十六页会返回未配置；可用 --repository-root 指定。");
                }

                var stopFilePath = ReadOption(args, "--stop-file");
                if (stopFilePath != null)
                {
                    Console.WriteLine($"常驻模式：出现停止文件即退出（{stopFilePath}）。");
                    while (!File.Exists(stopFilePath))
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                else
                {
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
            }

            return 0;
        }

        private static int ReadPort(string[] args)
        {
            var raw = ReadOption(args, "--port");
            return raw != null && int.TryParse(raw, out var parsedPort) ? parsedPort : 0;
        }

        private static string ReadOption(string[] args, string optionName)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == optionName)
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        /// <summary>从当前目录逐级往上找 .git，找到就是仓库根；找不到返回 null。</summary>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
