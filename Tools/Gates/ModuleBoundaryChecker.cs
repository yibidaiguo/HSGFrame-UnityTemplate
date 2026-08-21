using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 模块边界检查器（《结构规范-代码》第四节）：模块的公开面 = Contracts + Events，其余都是私有。
    /// 模块 X 之外的业务代码只准写 <c>&lt;根&gt;.X.Contracts</c> / <c>&lt;根&gt;.X.Events</c>，
    /// 引到对方的 Service / State / Data / View / Utilities 或模块根类型即违规。
    /// 靠「越界了会红」把「两个模块该怎么对话」从口头约定变成机器规矩。
    /// </summary>
    public static class ModuleBoundaryChecker
    {
        /// <summary>模块的公开面：只有这两个职责夹允许被别的模块直写。</summary>
        private static readonly string[] PublicSegments = { "Contracts", "Events" };

        private const string ModulesDirectoryName = "Modules";

        // 工具链是编辑器侧的东西，天然要深入模块内部（关卡编辑器不认识关卡数据就没法工作），
        // 它也不进包、不参与模块之间的耦合。这不是「暂时豁免」而是永久的范围之外，
        // 所以写死在这里而不是挂进豁免清单——豁免清单是留给欠账的，欠账要能燃尽。
        private static readonly string[] OutOfScopeSegments = { "Toolkit" };

        private static readonly string[] DefaultSkipSegments = { "bin", "obj", ".git", "Library", "Temp" };

        /// <summary>
        /// 扫业务代码树，返回全部越界引用。
        /// </summary>
        /// <param name="scriptsRootDirectory">业务代码根目录，即 <c>Assets/Game/Scripts</c>。</param>
        /// <param name="configuration">门禁配置，豁免路径从这里取。</param>
        public static IReadOnlyList<GateFinding> Check(string scriptsRootDirectory, GateConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootDirectory) || !Directory.Exists(scriptsRootDirectory))
            {
                return Array.Empty<GateFinding>();
            }

            var scriptsRoot = Path.GetFullPath(scriptsRootDirectory);
            var moduleNames = ReadModuleNames(scriptsRoot);
            if (moduleNames.Count == 0)
            {
                return Array.Empty<GateFinding>();
            }

            var exemptPaths = configuration?.ModuleBoundaryExemptPaths ?? Array.Empty<string>();
            var patterns = moduleNames.ToDictionary(
                moduleName => moduleName,
                moduleName => new Regex(
                    $@"(?<![\w.])(?:[A-Za-z_]\w*\.)+{Regex.Escape(moduleName)}\.([A-Za-z_]\w*)",
                    RegexOptions.Compiled),
                StringComparer.Ordinal);

            var skipSegments = DefaultSkipSegments
                .Concat(configuration?.SourceScanSkipSegments ?? Array.Empty<string>())
                .ToArray();

            var findings = new List<GateFinding>();
            foreach (var filePath in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                // 跳过判定只看**扫描根以下**的路径段。拿绝对路径去比会把扫描根自己的祖先目录算进去
                // ——临时目录里那个 Temp 段就能让整棵树一个文件都扫不到，而检查照样报「0 条违规」。
                var relativePath = Path.GetRelativePath(scriptsRoot, filePath).Replace('\\', '/');
                var segments = relativePath.Split('/');
                if (segments.Any(segment => skipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (IsOutOfScope(relativePath) || IsExempt(relativePath, exemptPaths))
                {
                    continue;
                }

                findings.AddRange(CheckFile(filePath, relativePath, patterns));
            }

            findings.Sort((left, right) => string.CompareOrdinal(left.Location, right.Location));
            return findings;
        }

        /// <summary>
        /// 读出 <c>Scripts/Modules/</c> 下的模块名。模块夹是唯一的事实源——
        /// 谁是模块由目录决定，不用另开一份清单去和现实对账。
        /// </summary>
        /// <param name="scriptsRootDirectory">业务代码根目录。</param>
        public static IReadOnlyList<string> ReadModuleNames(string scriptsRootDirectory)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootDirectory))
            {
                return Array.Empty<string>();
            }

            var modulesRoot = Path.Combine(scriptsRootDirectory, ModulesDirectoryName);
            if (!Directory.Exists(modulesRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(modulesRoot)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<GateFinding> CheckFile(
            string filePath,
            string relativePath,
            IReadOnlyDictionary<string, Regex> patterns)
        {
            var owner = ReadOwnerModule(relativePath);
            var findings = new List<GateFinding>();
            var lines = File.ReadAllLines(filePath);

            var inBlockComment = false;
            var inVerbatimString = false;
            var rawStringFenceLength = 0;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                // 注释与字符串里出现的限定名不是引用。复用命名检查器那份剥离逻辑，
                // 免得两处各写一份、行为悄悄分叉。
                var code = NamingChecker.StripNonCode(lines[lineIndex], ref inBlockComment, ref inVerbatimString, ref rawStringFenceLength);
                foreach (var pair in patterns)
                {
                    if (string.Equals(pair.Key, owner, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (Match match in pair.Value.Matches(code))
                    {
                        var segment = match.Groups[1].Value;
                        if (PublicSegments.Contains(segment, StringComparer.Ordinal))
                        {
                            continue;
                        }

                        findings.Add(new GateFinding(
                            $"{filePath}:{lineIndex + 1}",
                            $"引用了模块「{pair.Key}」的私有面「{match.Value}」，模块的公开面只有 Contracts 与 Events",
                            $"改成引用 {pair.Key}.Contracts / {pair.Key}.Events，或让 {pair.Key} 发事件、这边订阅；" +
                            "两个模块都要的类型上提到 Scripts/Shared/",
                            "Specifications/structure-code.md 第四节"));
                    }
                }
            }

            return findings;
        }

        // 文件落在 Modules/<X>/ 底下时，X 就是它的归属模块；模块内部怎么引自己不归这条规矩管。
        private static string ReadOwnerModule(string relativePath)
        {
            var segments = relativePath.Split('/');
            if (segments.Length >= 3 && string.Equals(segments[0], ModulesDirectoryName, StringComparison.Ordinal))
            {
                return segments[1];
            }

            return null;
        }

        private static bool IsOutOfScope(string relativePath)
        {
            var firstSegment = relativePath.Split('/')[0];
            return OutOfScopeSegments.Contains(firstSegment, StringComparer.Ordinal);
        }

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
    }
}
