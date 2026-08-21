using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 全仓路径 ASCII 门禁：目录名与文件名一律只许 ASCII，中文只许出现在**文件内容**里
    /// （注释、文案、数据值、JSON 的键都随意）。
    ///
    /// 为什么值得一道门禁：中文路径是一类**低频但很贵**的故障源——
    /// git 在不同 `core.quotepath` 下显示不一致、CI 容器 locale 不是 UTF-8 时路径会烂、
    /// 某些 .NET / MSBuild / Unity 的路径处理在非 ASCII 下有历史坑、命令行里还要额外操心引号。
    /// 本仓踩过近亲：`gate.ps1` 输出重定向到文件时子进程的 JSON 日志会丢（编码相关）。
    ///
    /// **与命名门禁的分工**：那一道只作用于「含 .cs 的目录」，管不到 `Doc/` 与 `Pools/`；
    /// 这一道扫全仓，只查一件事——路径里有没有非 ASCII 字符。
    ///
    /// **迁移期用 warn 模式**：照样逐条列出来，但不判红。存量清完再翻成 block（待办 1 的 f 批）。
    /// </summary>
    public static class PathAsciiChecker
    {
        /// <summary>不扫的目录名：第三方、生成物、机器状态，跟这道规矩无关。</summary>
        private static readonly string[] SkipSegments =
        {
            ".git", ".claude", ".idea", ".vs", "bin", "obj", "Library", "Temp", "Logs",
            "Build", "Builds", "Bundles", "PackageCache", "HybridCLRData", "HybridCLRGenerate",
            "node_modules", "MemoryCaptures", "_Scratch", "tmp", "Memory"
        };

        /// <summary>
        /// 扫一遍仓库，返回每一条带非 ASCII 字符的路径。
        /// 报的是**仓库相对路径**，且指出是哪一段不合规——只说「这个路径不行」会让人不知道改哪一节。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="configuration">门禁配置，读豁免名单与模式。</param>
        public static IReadOnlyList<GateFinding> Check(string repositoryRoot, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            {
                return findings;
            }

            var exemptions = configuration?.PathAsciiExemptPrefixes ?? Array.Empty<string>();
            var root = Path.GetFullPath(repositoryRoot);

            foreach (var path in Walk(root))
            {
                var relative = ToRelative(root, path);
                if (relative.Length == 0)
                {
                    continue;
                }

                if (exemptions.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                var offending = relative
                    .Split('/')
                    .Where(segment => !IsAscii(segment))
                    .ToList();
                if (offending.Count == 0)
                {
                    continue;
                }

                findings.Add(new GateFinding(
                    relative,
                    $"路径里有非 ASCII 名字：{string.Join(" / ", offending.Distinct(StringComparer.Ordinal))}",
                    "把目录名与文件名改成 ASCII；中文留在文件内容里（注释、文案、数据值都不受限）",
                    "Doc/Backlog.md"));
            }

            return findings;
        }

        /// <summary>一个字符串是不是全 ASCII（含空格与常见符号；这里只拦非 ASCII，不管风格）。</summary>
        public static bool IsAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            foreach (var ch in text)
            {
                if (ch > 127)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>递归走目录，产出全部文件路径；跳过段里的目录整棵不进。</summary>
        private static IEnumerable<string> Walk(string directory)
        {
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                var current = pending.Pop();

                string[] subdirectories;
                string[] files;
                try
                {
                    subdirectories = Directory.GetDirectories(current);
                    files = Directory.GetFiles(current);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                foreach (var subdirectory in subdirectories)
                {
                    var name = Path.GetFileName(subdirectory);
                    if (SkipSegments.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Push(subdirectory);
                }
            }
        }

        /// <summary>绝对路径转仓库相对路径，正斜杠。</summary>
        private static string ToRelative(string root, string path)
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Replace('\\', '/');
            }

            return full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
    }
}
