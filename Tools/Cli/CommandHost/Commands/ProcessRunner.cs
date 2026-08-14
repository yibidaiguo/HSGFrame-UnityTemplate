using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>
    /// 启动外部进程并收集标准输出的小工具，供各命令复用。
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// 启动一个外部进程，等待结束，返回退出码与合并后的逐行输出。
        /// </summary>
        /// <param name="fileName">可执行文件名，例如 dotnet。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">进程工作目录。</param>
        public static (int ExitCode, List<string> OutputLines) Run(string fileName, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8
            };

            var outputLines = new List<string>();

            using (var process = new Process { StartInfo = startInfo })
            {
                // 用异步事件逐行收集，避免先读尽标准输出再读错误流造成缓冲区死锁。
                process.OutputDataReceived += (sender, eventArgs) => AppendLine(outputLines, eventArgs.Data);
                process.ErrorDataReceived += (sender, eventArgs) => AppendLine(outputLines, eventArgs.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return (process.ExitCode, outputLines);
            }
        }

        private static void AppendLine(List<string> outputLines, string line)
        {
            // 空行没有信息量，直接丢弃。
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            outputLines.Add(line);
        }
    }
}
