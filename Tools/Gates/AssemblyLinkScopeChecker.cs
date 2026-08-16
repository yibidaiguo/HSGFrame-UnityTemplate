using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 装配对账检查器（《结构规范-代码》第三节 R3）：Logic.Core.csproj 的链接范围必须与 Game.Logic 的覆盖一致。
    /// 链接范围就是 Game.Logic 的定义——csproj 与 asmdef/asmref 装配两处一偏，这里就红。
    /// </summary>
    public static class AssemblyLinkScopeChecker
    {
        /// <summary>csproj 里标识业务代码落点的那段路径，之后的部分才是相对 Scripts 根的 glob。</summary>
        private const string ScriptsMarker = "UnityProject/Assets/Game/Scripts/";

        private const string GameLogicAssemblyName = "Game.Logic";

        private const string ReferenceDocumentPath = "规范/结构规范-代码.md 第三节";

        private const string FixActionText = "要么把它挪出链接范围，要么让它归 Game.Logic——链接范围就是 Game.Logic 的定义";

        /// <summary>与扫描根无关的目录段：拿相对 scripts 根的路径比，不拿绝对路径比（绝对路径里的 Temp 段会误杀整棵树）。</summary>
        private static readonly string[] SkipSegments = { "bin", "obj", ".git", "Library", "Temp" };

        /// <summary>
        /// 对账 Logic.Core.csproj 的链接范围与 Game.Logic 的装配覆盖，返回两边不一致的文件清单。
        /// 集合一由 csproj 的 Include/Exclude glob 决定，集合二由 asmdef/asmref 向上归属决定，
        /// 对称差即违规。入参为空、文件或目录不存在、csproj 解析失败时返回空清单，不抛异常。
        /// </summary>
        /// <param name="projectFilePath">Logic.Core.csproj 的完整路径。</param>
        /// <param name="scriptsRootDirectory">业务代码根目录 <c>Assets/Game/Scripts</c>。</param>
        public static IReadOnlyList<GateFinding> Check(string projectFilePath, string scriptsRootDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath)
                || string.IsNullOrWhiteSpace(scriptsRootDirectory)
                || !File.Exists(projectFilePath)
                || !Directory.Exists(scriptsRootDirectory))
            {
                return Array.Empty<GateFinding>();
            }

            var scriptsRoot = Path.GetFullPath(scriptsRootDirectory);
            var compileGlobs = ReadCompileGlobs(projectFilePath);
            if (compileGlobs.Count == 0)
            {
                return Array.Empty<GateFinding>();
            }

            var linkedPaths = new HashSet<string>(StringComparer.Ordinal);
            var gameLogicPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var filePath in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(scriptsRoot, filePath).Replace('\\', '/');
                if (HasSkipSegment(relativePath))
                {
                    continue;
                }

                if (IsLinked(relativePath, compileGlobs))
                {
                    linkedPaths.Add(relativePath);
                }

                if (string.Equals(ReadOwningAssembly(filePath, scriptsRoot), GameLogicAssemblyName, StringComparison.Ordinal))
                {
                    gameLogicPaths.Add(relativePath);
                }
            }

            var findings = new List<GateFinding>();
            foreach (var relativePath in linkedPaths.Except(gameLogicPaths).OrderBy(path => path, StringComparer.Ordinal))
            {
                findings.Add(new GateFinding(
                    relativePath,
                    "Logic.Core.csproj 链接了它，但它不归 Game.Logic",
                    FixActionText,
                    ReferenceDocumentPath));
            }

            foreach (var relativePath in gameLogicPaths.Except(linkedPaths).OrderBy(path => path, StringComparer.Ordinal))
            {
                findings.Add(new GateFinding(
                    relativePath,
                    "它归 Game.Logic，但 Logic.Core.csproj 没链接它",
                    FixActionText,
                    ReferenceDocumentPath));
            }

            findings.Sort((left, right) => string.CompareOrdinal(left.Location, right.Location));
            return findings;
        }

        /// <summary>
        /// 从 csproj 读出全部 <c>&lt;Compile&gt;</c> 条目的链接 glob。只处理 Include 里含
        /// <c>UnityProject/Assets/Game/Scripts/</c> 的条目（UnityShim 这类别的树不碰），
        /// Exclude 用分号拆成多条，各自取 Scripts 标记之后那一段当 glob。
        /// </summary>
        /// <param name="projectFilePath">Logic.Core.csproj 的完整路径。</param>
        private static IReadOnlyList<CompileGlob> ReadCompileGlobs(string projectFilePath)
        {
            var result = new List<CompileGlob>();
            try
            {
                var document = XDocument.Load(projectFilePath);
                foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Compile"))
                {
                    var include = element.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include) || !include.Contains(ScriptsMarker, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var includeGlob = include.Substring(include.IndexOf(ScriptsMarker, StringComparison.Ordinal) + ScriptsMarker.Length);
                    var excludeGlobs = new List<string>();
                    var exclude = element.Attribute("Exclude")?.Value;
                    if (!string.IsNullOrWhiteSpace(exclude))
                    {
                        foreach (var part in exclude.Split(';'))
                        {
                            var trimmedPart = part.Trim();
                            if (trimmedPart.Contains(ScriptsMarker, StringComparison.Ordinal))
                            {
                                excludeGlobs.Add(trimmedPart.Substring(
                                    trimmedPart.IndexOf(ScriptsMarker, StringComparison.Ordinal) + ScriptsMarker.Length));
                            }
                        }
                    }

                    result.Add(new CompileGlob(includeGlob, excludeGlobs));
                }
            }
            catch (Exception exception) when (exception is XmlException or IOException)
            {
                return Array.Empty<CompileGlob>();
            }

            return result;
        }

        /// <summary>判断相对路径是否命中任一条 Compile 的 Include 且不命中该条目的任何 Exclude。</summary>
        private static bool IsLinked(string relativePath, IReadOnlyList<CompileGlob> compileGlobs)
        {
            foreach (var compileGlob in compileGlobs)
            {
                if (compileGlob.IncludeRegex.IsMatch(relativePath)
                    && !compileGlob.ExcludeRegexes.Any(regex => regex.IsMatch(relativePath)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从文件所在目录起逐级向上找第一个含 asmdef/asmref 的目录，决定文件归哪个程序集。
        /// asmdef 的归属是文件名去扩展名；asmref 的归属读 JSON 里的 reference 字段（本仓库写的是程序集名）。
        /// 一路找到 scripts 根都没有 → 归属为空。
        /// </summary>
        /// <param name="filePath">源文件的完整路径。</param>
        /// <param name="scriptsRoot">业务代码根目录的完整路径。</param>
        private static string ReadOwningAssembly(string filePath, string scriptsRoot)
        {
            var directory = Path.GetDirectoryName(filePath);
            while (directory != null)
            {
                var asmdef = Directory.EnumerateFiles(directory, "*.asmdef").FirstOrDefault();
                if (asmdef != null)
                {
                    return Path.GetFileNameWithoutExtension(asmdef);
                }

                var asmref = Directory.EnumerateFiles(directory, "*.asmref").FirstOrDefault();
                if (asmref != null)
                {
                    return ReadAsmrefReference(asmref);
                }

                if (string.Equals(Path.GetFullPath(directory), scriptsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        /// <summary>读 asmref 的 JSON，取 reference 字段的值当归属程序集名；读不出来返回空。</summary>
        /// <param name="asmrefPath">asmref 文件的完整路径。</param>
        private static string ReadAsmrefReference(string asmrefPath)
        {
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(asmrefPath)))
                {
                    if (document.RootElement.TryGetProperty("reference", out var reference))
                    {
                        return reference.GetString();
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                return null;
            }

            return null;
        }

        /// <summary>相对 scripts 根的路径里出现跳过段就整条跳过，段名忽略大小写。</summary>
        /// <param name="relativePath">相对 scripts 根的路径（正斜杠）。</param>
        private static bool HasSkipSegment(string relativePath)
        {
            return relativePath.Split('/').Any(segment => SkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 把 glob 转成正则：<c>**/</c> → <c>(?:.*/)?</c>，<c>**</c> → <c>.*</c>，<c>*</c> → <c>[^/]*</c>，
        /// 其余字符逐个转义，整体加锚。逐字符单遍扫描，避免多轮替换把已产出的模式改坏。
        /// </summary>
        /// <param name="glob">glob 表达式。</param>
        private static string GlobToRegex(string glob)
        {
            var builder = new StringBuilder("^");
            for (var index = 0; index < glob.Length;)
            {
                var current = glob[index];
                if (current == '*')
                {
                    var isDoubleStar = index + 1 < glob.Length && glob[index + 1] == '*';
                    if (isDoubleStar && index + 2 < glob.Length && glob[index + 2] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        index += 3;
                    }
                    else if (isDoubleStar)
                    {
                        builder.Append(".*");
                        index += 2;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                        index += 1;
                    }
                }
                else
                {
                    builder.Append(Regex.Escape(current.ToString()));
                    index += 1;
                }
            }

            builder.Append('$');
            return builder.ToString();
        }

        /// <summary>一条 Compile 的 Include 与它绑定的 Exclude，各自编译成正则待用。</summary>
        private sealed class CompileGlob
        {
            /// <summary>
            /// 构造一条 Compile 链接规则。
            /// </summary>
            /// <param name="includeGlob">Include 里 Scripts 标记之后的 glob。</param>
            /// <param name="excludeGlobs">Exclude 拆出的每条 Scripts 标记之后的 glob。</param>
            public CompileGlob(string includeGlob, IReadOnlyList<string> excludeGlobs)
            {
                IncludeRegex = new Regex(GlobToRegex(includeGlob), RegexOptions.Compiled);
                ExcludeRegexes = excludeGlobs
                    .Select(glob => new Regex(GlobToRegex(glob), RegexOptions.Compiled))
                    .ToArray();
            }

            /// <summary>Include 编译出的正则。</summary>
            public Regex IncludeRegex { get; }

            /// <summary>Exclude 拆条后编译出的正则。</summary>
            public IReadOnlyList<Regex> ExcludeRegexes { get; }
        }
    }
}
