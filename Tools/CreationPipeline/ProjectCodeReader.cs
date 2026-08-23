using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>读一批文件的结果。</summary>
    /// <param name="Text">拼好的正文，能直接追加进提示词；一个都没读到时为空串。</param>
    /// <param name="ReadPaths">真读到的仓库相对路径。</param>
    /// <param name="Notes">拒了谁、为什么，一句一条；要如实进回话与流水。</param>
    public sealed record ProjectCodeReadResult(
        string Text, IReadOnlyList<string> ReadPaths, IReadOnlyList<string> Notes);

    /// <summary>
    /// 助手读项目代码的**唯一入口**，也是那条边界本身。
    ///
    /// 助手的权限是「能读代码、不能改代码」。「不能改」靠它压根没有写的路来保证；
    /// **「能读」这条得自己划边界**——它是一条能把内容送出这台机器的路
    /// （读到的东西会进提示词，发给下游模型）。所以这里是白名单而不是黑名单：
    /// 黑名单要穷举所有不该读的东西，漏一条就是漏一条；
    /// 白名单漏一条只是少读一个目录，人会发现并来提。
    ///
    /// 能读的是**回答问题时真要看的那几类事实**：Unity 工程的代码、池子、规范、配置表结构。
    /// 光给代码读不准——「背包最多几格」在配置表结构里，「这一屏有哪些元素」在界面规格里。
    ///
    /// **`Tools/` 整棵树不给读**：那是这条管线自己的代码与配置，跟「这个游戏是什么样」无关，
    /// 而密钥就住在 `Tools/CreationPipeline/Config/local.json`（决策 5）。
    /// 不逐个文件去挡，是因为挡文件要穷举、漏一个就是漏一个；
    /// 不给整棵树只会少读一些无关的东西。
    /// </summary>
    public static class ProjectCodeReader
    {
        /// <summary>一次最多读几个文件。</summary>
        public const int MaximumFileCount = 6;

        /// <summary>一次最多读多少字节。超了就截断并如实说。</summary>
        public const int MaximumTotalBytes = 60 * 1024;

        /// <summary>单个文件最多读多少字节。</summary>
        public const int MaximumSingleFileBytes = 24 * 1024;

        /// <summary>
        /// 允许读的目录前缀（仓库相对，正斜杠）。
        ///
        /// 四类，都是**回答问题时真要看的事实**：Unity 工程的代码、
        /// 池子（需求 / 界面规格 / 设计库）、规范（各层基线）、配置表结构。
        /// 光有代码读不准——「背包最多几格」写在配置表结构里，
        /// 「这一屏有哪些元素」写在界面规格里，只看 .cs 只能猜。
        ///
        /// **工作流本身不给**：`Tools/` 底下是这条管线自己的代码与配置，
        /// 它跟「这个游戏是什么样」无关，而那底下混着台账与本机配置——
        /// 密钥就住在 `Tools/CreationPipeline/Config/local.json`（决策 5）。
        /// 不是逐个文件去挡，是**整棵树都不在白名单里**：
        /// 挡文件要穷举，漏一个就是漏一个；不给整棵树只会少读一些无关的东西。
        ///
        /// 框架包只到 `Runtime/`：编辑器脚本与测试不是模块行为的一部分。
        /// </summary>
        private static readonly string[] AllowedPrefixes =
        {
            "UnityProject/Assets/Game/",
            "Packages/com.hsgframe.",
            "Pools/",
            "Specifications/",
            "Config/Schema/"
        };

        /// <summary>框架包底下只许读这一段：`Packages/com.hsgframe.x/Runtime/…`。</summary>
        private const string PackageRuntimeSegment = "/Runtime/";

        /// <summary>
        /// 允许读的扩展名：代码、界面定义、以及池子与规范用的 json / md。
        /// 二进制与压缩包一律不读——读了也没意义，还撑爆提示词。
        /// </summary>
        private static readonly string[] AllowedExtensions =
        {
            ".cs", ".asmdef", ".uxml", ".uss", ".json", ".md"
        };

        /// <summary>
        /// 白名单那几棵树的绝对路径——文件清单照它去枚举，
        /// **与 <see cref="TryResolve"/> 共用同一份前缀表**，免得清单里出现读不了的路径。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<string> AllowedRoots(string repositoryRoot)
        {
            var roots = new List<string>();
            foreach (var prefix in AllowedPrefixes)
            {
                // 前缀可能是半个目录名（Packages/com.hsgframe.），那时枚举它的父目录，
                // 逐个文件仍要过 TryResolve，所以宽一点不会漏判。
                var trimmed = prefix.TrimEnd('/');
                var slash = trimmed.LastIndexOf('/');
                var directory = trimmed.Contains('.') && slash >= 0 ? trimmed.Substring(0, slash) : trimmed;
                var full = Path.Combine(repositoryRoot, directory.Replace('/', Path.DirectorySeparatorChar));
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }

            return roots;
        }

        /// <summary>
        /// 读一批文件。
        ///
        /// 拒绝一个文件**不影响别的**，但每一次拒绝都要留一句话：
        /// 静默少读一个文件，模型会照着不完整的材料下结论，而没人知道它少看了什么。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="relativePaths">要读的仓库相对路径。</param>
        public static ProjectCodeReadResult Read(string repositoryRoot, IReadOnlyList<string> relativePaths)
        {
            var notes = new List<string>();
            var readPaths = new List<string>();
            var builder = new StringBuilder();
            var totalBytes = 0;

            if (relativePaths == null || relativePaths.Count == 0)
            {
                notes.Add("没给要读的文件");
                return new ProjectCodeReadResult("", readPaths, notes);
            }

            var rootFullPath = Path.GetFullPath(repositoryRoot);

            foreach (var requested in relativePaths)
            {
                if (readPaths.Count >= MaximumFileCount)
                {
                    notes.Add($"一次最多读 {MaximumFileCount} 个文件，后面的没读："
                        + $"{relativePaths.Count - readPaths.Count} 个");
                    break;
                }

                if (totalBytes >= MaximumTotalBytes)
                {
                    notes.Add($"读到 {MaximumTotalBytes / 1024} KB 上限了，后面的没读");
                    break;
                }

                if (!TryResolve(rootFullPath, requested, out var fullPath, out var relative, out var reason))
                {
                    notes.Add($"{requested}：{reason}");
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(fullPath);
                }
                catch (Exception exception)
                    when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    notes.Add($"{relative}：读不动（{exception.Message}）");
                    continue;
                }

                var truncated = false;
                if (Encoding.UTF8.GetByteCount(text) > MaximumSingleFileBytes)
                {
                    // 按字符粗截：这是给模型看的材料，不是给编译器的，
                    // 截在半个多字节字符上只会多一个乱码，不值得为它做一遍精确的字节切分。
                    text = text.Substring(0, Math.Min(text.Length, MaximumSingleFileBytes / 3));
                    truncated = true;
                }

                builder.Append("### ").Append(relative).Append(truncated ? "（截断）" : "").Append('\n');
                builder.Append("```\n").Append(text.TrimEnd()).Append("\n```\n\n");

                totalBytes += Encoding.UTF8.GetByteCount(text);
                readPaths.Add(relative);

                if (truncated)
                {
                    notes.Add($"{relative}：太长，只读了开头一段");
                }
            }

            return new ProjectCodeReadResult(builder.ToString(), readPaths, notes);
        }

        /// <summary>
        /// 把请求的路径解析成一个**确定在白名单里**的绝对路径。
        ///
        /// 三道：① 解析成绝对路径之后必须仍在仓库根底下（挡 `../`）；
        /// ② 仓库相对路径必须命中白名单前缀；③ 扩展名必须在允许之列。
        /// **顺序不能换**：先解析再比前缀，否则 `UnityProject/Assets/Game/Scripts/../../../../secret`
        /// 这种字符串会命中前缀而实际指到仓库外面。
        /// </summary>
        /// <param name="rootFullPath">仓库根的绝对路径。</param>
        /// <param name="requested">请求的路径。</param>
        /// <param name="fullPath">解析出的绝对路径。</param>
        /// <param name="relative">仓库相对路径，正斜杠。</param>
        /// <param name="reason">拒绝原因；通过时为空串。</param>
        public static bool TryResolve(
            string rootFullPath, string requested, out string fullPath, out string relative, out string reason)
        {
            fullPath = "";
            relative = "";
            reason = "";

            var trimmed = (requested ?? "").Trim().Replace('\\', '/');
            if (trimmed.Length == 0)
            {
                reason = "路径是空的";
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(Path.Combine(rootFullPath, trimmed));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
            {
                reason = "路径不合法";
                return false;
            }

            if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                reason = "指到仓库外面去了，不读";
                return false;
            }

            relative = Path.GetRelativePath(rootFullPath, fullPath).Replace('\\', '/');

            var allowed = false;
            foreach (var prefix in AllowedPrefixes)
            {
                if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                reason = "不在允许读的目录里（能读：UnityProject/Assets/Game/、"
                    + "Packages/com.hsgframe.*/Runtime/、Pools/、Specifications/、Config/Schema/；"
                    + "工作流那棵树 Tools/ 不给读）";
                return false;
            }

            // 框架包底下只许读 Runtime/：编辑器脚本与测试不是模块行为的一部分。
            if (relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                && relative.IndexOf(PackageRuntimeSegment, StringComparison.OrdinalIgnoreCase) < 0)
            {
                reason = "框架包底下只读 Runtime/ 那一段";
                return false;
            }

            var extension = Path.GetExtension(relative);
            var extensionAllowed = false;
            foreach (var candidate in AllowedExtensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    extensionAllowed = true;
                    break;
                }
            }

            if (!extensionAllowed)
            {
                reason = $"这种扩展名不读（{(extension.Length == 0 ? "没有扩展名" : extension)}）";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                reason = "这个文件不在";
                return false;
            }

            return true;
        }
    }
}
