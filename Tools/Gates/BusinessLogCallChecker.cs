using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 业务层裸日志检查器（《结构规范-代码》第六节）：业务代码打日志走 <c>HSGFrame.Logging</c> 的接口，
    /// 不写裸 <c>UnityEngine.Debug.Log</c>。Modules/ 与 Shared/ 是纯逻辑层，连 UnityEngine 都不该出现；
    /// View/ 是表现层，也要走日志接口而非直接对控制台说话。Boot/（AOT 启动装配）与 Toolkit/（编辑器
    /// 工具链）本来就对着引擎说话，在检查范围之外。
    /// </summary>
    public static class BusinessLogCallChecker
    {
        /// <summary>受管的三棵子树：模块、共享与表现。启动装配与工具链不归这里管。</summary>
        private static readonly string[] InScopeFirstSegments = { "Modules", "Shared", "View" };

        private static readonly string[] DefaultSkipSegments = { "bin", "obj", ".git", "Library", "Temp" };

        /// <summary>裸 Debug 调用：可带也可不带 UnityEngine. 前缀，方法名与左括号一起抓，注释字符串已先行剥离。</summary>
        private static readonly Regex DebugCallPattern = new Regex(
            @"(?<![\w.])(?:UnityEngine\.)?Debug\.(Log|LogWarning|LogError|LogFormat|LogWarningFormat|LogErrorFormat|LogException|LogAssertion)\s*\(",
            RegexOptions.Compiled);

        /// <summary>
        /// 扫业务代码树，返回全部裸 Unity 日志调用。
        /// </summary>
        /// <param name="scriptsRootDirectory">业务代码根目录，即 <c>Assets/Game/Scripts</c>。</param>
        /// <param name="exemptPaths">豁免清单，每项是相对根目录的路径前缀（正斜杠），命中即整份文件跳过；为 null 按空清单处理。</param>
        public static IReadOnlyList<GateFinding> Check(string scriptsRootDirectory, IReadOnlyList<string> exemptPaths)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootDirectory) || !Directory.Exists(scriptsRootDirectory))
            {
                return Array.Empty<GateFinding>();
            }

            var scriptsRoot = Path.GetFullPath(scriptsRootDirectory);
            var exempt = exemptPaths ?? Array.Empty<string>();

            var findings = new List<GateFinding>();
            foreach (var filePath in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                // 跳过判定只看**扫描根以下**的路径段。拿绝对路径去比会把扫描根自己的祖先目录算进去
                // ——临时目录里那个 Temp 段就能让整棵树一个文件都扫不到，而检查照样报「0 条违规」。
                var relativePath = Path.GetRelativePath(scriptsRoot, filePath).Replace('\\', '/');
                if (!IsInScope(relativePath)
                    || HasSkipSegment(relativePath)
                    || IsExempt(relativePath, exempt))
                {
                    continue;
                }

                findings.AddRange(CheckFile(filePath));
            }

            findings.Sort((left, right) => string.CompareOrdinal(left.Location, right.Location));
            return findings;
        }

        /// <summary>文件是否落在受管的三棵子树（Modules/、Shared/、View/）里。</summary>
        /// <param name="relativePath">相对扫描根目录的路径，正斜杠分隔。</param>
        private static bool IsInScope(string relativePath)
        {
            var firstSegment = relativePath.Split('/')[0];
            return InScopeFirstSegments.Contains(firstSegment, StringComparer.Ordinal);
        }

        /// <summary>相对路径里是否带着要跳过的目录段（生成物、版本库、临时目录）。</summary>
        /// <param name="relativePath">相对扫描根目录的路径，正斜杠分隔。</param>
        private static bool HasSkipSegment(string relativePath)
        {
            return relativePath.Split('/')
                .Any(segment => DefaultSkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>相对路径是否命中豁免前缀，命中即整份文件跳过。</summary>
        /// <param name="relativePath">相对扫描根目录的路径，正斜杠分隔。</param>
        /// <param name="exemptPaths">豁免路径前缀清单。</param>
        private static bool IsExempt(string relativePath, IReadOnlyList<string> exemptPaths)
        {
            foreach (var exemptPath in exemptPaths)
            {
                if (!string.IsNullOrWhiteSpace(exemptPath)
                    && relativePath.StartsWith(exemptPath.Replace('\\', '/'), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>逐行剥离注释与字符串后找裸 Debug 调用，每命中一次报一条发现。</summary>
        /// <param name="filePath">要检查的源文件全路径。</param>
        private static IReadOnlyList<GateFinding> CheckFile(string filePath)
        {
            var findings = new List<GateFinding>();
            var lines = File.ReadAllLines(filePath);

            var inBlockComment = false;
            var inVerbatimString = false;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                // 注释与字符串里的 Debug.Log 不是调用。复用命名检查器那份剥离逻辑，
                // 免得两处各写一份、行为悄悄分叉。
                var code = NamingChecker.StripNonCode(lines[lineIndex], ref inBlockComment, ref inVerbatimString);
                foreach (Match match in DebugCallPattern.Matches(code))
                {
                    // 命中的文本带着结尾空白与左括号，剥掉只留调用名本身。
                    var matchedText = match.Value.TrimEnd(' ', '\t', '(');
                    findings.Add(new GateFinding(
                        $"{filePath}:{lineIndex + 1}",
                        $"业务层写了裸 {matchedText}，日志要走 HSGFrame.Logging",
                        "改成注入 HSGFrame.Logging 的日志接口；View 层要落到 Unity 控制台就用现成的 UnityConsoleLogSink",
                        "规范/结构规范-代码.md 第六节"));
                }
            }

            return findings;
        }
    }
}
