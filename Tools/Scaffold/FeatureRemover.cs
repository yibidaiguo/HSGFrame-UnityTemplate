using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.Scaffold
{
    /// <summary>
    /// 把一个可选功能整块摘掉：删它的目录与文件，摘 manifest 的依赖、解决方案的工程条目、
    /// 门禁配置里为它开的口子，再摘掉文档里带标记的段落。
    /// 每一处都落文件系统，跑完 <c>git diff</c> 看得见，不留「还要手动改三处」的尾巴。
    /// </summary>
    public static class FeatureRemover
    {
        private const string ManifestRelativePath = "UnityProject/Packages/manifest.json";
        private const string SolutionRelativePath = "Solutions/Template.sln";
        private const string TestBaselineRelativePath = "Tools/Gates/Config/test-baseline.json";
        private const string GateConfigRelativePath = "Tools/Gates/Config/gate-config.json";

        /// <summary>扫文档时要跳过的目录段，大小写不敏感。</summary>
        private static readonly string[] SkipSegments =
        {
            "bin", "obj", ".git", "Library", "Temp", "PackageCache", "HybridCLRData",
        };

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>
        /// 摘掉一个可选功能。功能名不认识、模板根不存在时失败且一个字节都不改。
        /// </summary>
        /// <param name="templateRoot">模板根目录。</param>
        /// <param name="featureName">要摘掉的功能名，见 <see cref="FeatureRemovalPlan.ListKnown"/>。</param>
        public static FeatureRemovalResult Remove(string templateRoot, string featureName)
        {
            if (string.IsNullOrWhiteSpace(templateRoot) || !Directory.Exists(templateRoot))
            {
                return FeatureRemovalResult.Failure(ComposeError(
                    templateRoot, "模板根目录不存在", "把 TemplateRoot 指向模板根", "Tools/Gates/Config/gate-config.json 所在的那一级"));
            }

            var knownPlans = FeatureRemovalPlan.ListKnown();
            var plan = knownPlans.FirstOrDefault(candidate =>
                string.Equals(candidate.FeatureName, featureName, StringComparison.OrdinalIgnoreCase));
            if (plan == null)
            {
                return FeatureRemovalResult.Failure(ComposeError(
                    featureName ?? "（空）", "没有这个可选功能", "换成下面列出的功能名之一",
                    string.Join("、", knownPlans.Select(candidate => candidate.FeatureName))));
            }

            var root = Path.GetFullPath(templateRoot);

            // 文档先做一次预检：只有半边标记时整条命令失败，且此时还什么都没删。
            // 静默删到文件末尾比报错危险得多，所以宁可在动手之前就停下。
            var documentPaths = ListDocuments(root).ToList();
            var markerError = FindUnpairedMarker(documentPaths, root, plan.DocumentMarkerName);
            if (markerError != null)
            {
                return FeatureRemovalResult.Failure(markerError);
            }

            var changed = new List<string>();

            RemoveDirectories(root, plan, changed);
            RemoveFiles(root, plan, changed);
            RemoveManifestKeys(root, plan, changed);
            RemoveSolutionProjects(root, plan, changed);
            RemoveTestBaselineEntries(root, plan, changed);
            RemoveGateConfigEntries(root, plan, changed);
            RemoveDocumentSections(documentPaths, root, plan, changed);

            return FeatureRemovalResult.Success(
                $"已摘除可选功能「{plan.FeatureName}」：改动 {changed.Count} 处", changed);
        }

        private static void RemoveDirectories(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            foreach (var relativePath in plan.Directories ?? Array.Empty<string>())
            {
                var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(fullPath))
                {
                    changed.Add($"跳过（已不在）：{relativePath}");
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
                changed.Add($"删除目录：{relativePath}");

                // Assets/ 下的目录各自带一个同名 .meta，留着它 Unity 下次打开会报孤儿元文件。
                var directoryMetaPath = fullPath + ".meta";
                if (File.Exists(directoryMetaPath))
                {
                    File.Delete(directoryMetaPath);
                    changed.Add($"删除文件：{relativePath}.meta");
                }

                // 父目录因此空掉时一并收走：Tools/SourceGenerators/ 这类只为一个功能存在的中间层，
                // 留一个空壳在树里只会让人以为还有别的东西。
                var parent = Path.GetDirectoryName(fullPath);
                if (parent != null
                    && !string.Equals(Path.GetFullPath(parent), root, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(parent)
                    && !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                    changed.Add($"删除空目录：{ToRelative(root, parent)}");
                }
            }
        }

        private static void RemoveFiles(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            foreach (var relativePath in plan.Files ?? Array.Empty<string>())
            {
                foreach (var candidate in new[] { relativePath, relativePath + ".meta" })
                {
                    var fullPath = Path.Combine(root, candidate.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                    {
                        if (candidate == relativePath)
                        {
                            changed.Add($"跳过（已不在）：{candidate}");
                        }

                        continue;
                    }

                    File.Delete(fullPath);
                    changed.Add($"删除文件：{candidate}");
                }
            }
        }

        // manifest 是人天天读的文件：整份反序列化再写回会把键序与缩进洗掉，所以按行删。
        private static void RemoveManifestKeys(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            var fullPath = Path.Combine(root, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || plan.UnityPackageKeys == null)
            {
                return;
            }

            var lines = ReadLines(fullPath, out var hasBom);
            var removed = new List<string>();
            foreach (var key in plan.UnityPackageKeys)
            {
                var index = lines.FindIndex(line => line.TrimStart().StartsWith("\"" + key + "\":", StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }

                var wasLastEntry = !lines[index].TrimEnd().EndsWith(",", StringComparison.Ordinal);
                lines.RemoveAt(index);
                removed.Add(key);

                // 删掉的是最后一项时，前一项的行尾逗号得跟着去掉，否则 JSON 就废了。
                if (wasLastEntry && index > 0)
                {
                    lines[index - 1] = lines[index - 1].TrimEnd().TrimEnd(',');
                }
            }

            if (removed.Count == 0)
            {
                return;
            }

            WriteLines(fullPath, lines, hasBom);
            changed.Add($"{ManifestRelativePath}：摘掉依赖 {string.Join("、", removed)}");
        }

        private static void RemoveSolutionProjects(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            var fullPath = Path.Combine(root, SolutionRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || plan.SolutionProjectNames == null)
            {
                return;
            }

            var lines = ReadLines(fullPath, out var hasBom);
            var removed = new List<string>();

            foreach (var projectName in plan.SolutionProjectNames)
            {
                // 整名匹配：'"Hotfix", ' 里的逗号与空格是关键，否则 "Hotfix" 会吃掉 "Hotfix.Tests"。
                var marker = "= \"" + projectName + "\", ";
                var startIndex = lines.FindIndex(line =>
                    line.StartsWith("Project(", StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal));
                if (startIndex < 0)
                {
                    continue;
                }

                var projectGuid = ExtractLastGuid(lines[startIndex]);
                var endIndex = lines.FindIndex(startIndex, line => line.Trim() == "EndProject");
                if (endIndex < 0)
                {
                    continue;
                }

                lines.RemoveRange(startIndex, endIndex - startIndex + 1);

                if (projectGuid != null)
                {
                    lines.RemoveAll(line => line.Contains(projectGuid, StringComparison.OrdinalIgnoreCase));
                }

                removed.Add(projectName);
            }

            if (removed.Count == 0)
            {
                return;
            }

            WriteLines(fullPath, lines, hasBom);
            changed.Add($"{SolutionRelativePath}：摘掉工程 {string.Join("、", removed)}");
        }

        private static void RemoveTestBaselineEntries(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            var fullPath = Path.Combine(root, TestBaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || plan.TestBaselinePathPrefixes == null)
            {
                return;
            }

            var lines = ReadLines(fullPath, out var hasBom);
            var removedCount = lines.RemoveAll(line => plan.TestBaselinePathPrefixes.Any(prefix =>
                line.TrimStart().StartsWith("\"" + prefix, StringComparison.Ordinal)));
            if (removedCount == 0)
            {
                return;
            }

            FixTrailingCommaBeforeCloseBrace(lines);
            WriteLines(fullPath, lines, hasBom);
            changed.Add($"{TestBaselineRelativePath}：摘掉 {removedCount} 条测试哈希");
        }

        // 门禁配置里满是 _xxx说明 注释键，整份反序列化再写回会全丢，所以同样按行删。
        private static void RemoveGateConfigEntries(string root, FeatureRemovalPlan plan, List<string> changed)
        {
            var fullPath = Path.Combine(root, GateConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                return;
            }

            var lines = ReadLines(fullPath, out var hasBom);
            var notes = new List<string>();

            var removedSegments = new List<string>();
            foreach (var segment in plan.SourceScanSkipSegments ?? Array.Empty<string>())
            {
                var index = lines.FindIndex(line => line.Trim().TrimEnd(',') == "\"" + segment + "\"");
                if (index < 0)
                {
                    continue;
                }

                var wasLastEntry = !lines[index].TrimEnd().EndsWith(",", StringComparison.Ordinal);
                lines.RemoveAt(index);
                if (wasLastEntry && index > 0)
                {
                    lines[index - 1] = lines[index - 1].TrimEnd().TrimEnd(',');
                }

                removedSegments.Add(segment);
            }

            if (removedSegments.Count > 0)
            {
                notes.Add($"摘掉扫描跳过项 {string.Join("、", removedSegments)}");
            }

            if (RemoveOptionalFeatureScope(lines, plan.FeatureName))
            {
                notes.Add($"摘掉引用范围规则「{plan.FeatureName}」");
            }

            if (notes.Count == 0)
            {
                return;
            }

            WriteLines(fullPath, lines, hasBom);
            changed.Add($"{GateConfigRelativePath}：{string.Join("；", notes)}");
        }

        // 规则是 optionalFeatureScopes 数组里的一个对象字面量，按大括号配对切，不解析整份 JSON。
        private static bool RemoveOptionalFeatureScope(List<string> lines, string featureName)
        {
            var nameIndex = lines.FindIndex(line =>
                line.Contains("\"featureName\"", StringComparison.Ordinal)
                && line.Contains("\"" + featureName + "\"", StringComparison.OrdinalIgnoreCase));
            if (nameIndex < 0)
            {
                return false;
            }

            var startIndex = nameIndex;
            while (startIndex > 0 && !lines[startIndex].TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                startIndex--;
            }

            var endIndex = nameIndex;
            while (endIndex < lines.Count - 1 && !lines[endIndex].TrimStart().StartsWith("}", StringComparison.Ordinal))
            {
                endIndex++;
            }

            lines.RemoveRange(startIndex, endIndex - startIndex + 1);
            FixTrailingCommaBeforeCloseBracket(lines, startIndex);
            return true;
        }

        private static void RemoveDocumentSections(
            IReadOnlyList<string> documentPaths, string root, FeatureRemovalPlan plan, List<string> changed)
        {
            var beginMarker = BeginMarker(plan.DocumentMarkerName);
            var endMarker = EndMarker(plan.DocumentMarkerName);

            foreach (var documentPath in documentPaths)
            {
                // 清单是删目录之前列的（预检必须先于任何删除），那之后这份文档可能已经随目录一起走了。
                if (!File.Exists(documentPath))
                {
                    continue;
                }

                var lines = ReadLines(documentPath, out var hasBom);
                var kept = new List<string>();
                var removedSections = 0;
                var inside = false;

                foreach (var line in lines)
                {
                    if (!inside && line.Contains(beginMarker, StringComparison.Ordinal))
                    {
                        inside = true;
                        removedSections++;
                        continue;
                    }

                    if (inside)
                    {
                        if (line.Contains(endMarker, StringComparison.Ordinal))
                        {
                            inside = false;
                        }

                        continue;
                    }

                    kept.Add(line);
                }

                if (removedSections == 0)
                {
                    continue;
                }

                WriteLines(documentPath, kept, hasBom);
                changed.Add($"{ToRelative(root, documentPath)}：摘掉 {removedSections} 段带标记的内容");
            }
        }

        /// <summary>预检：任何一份文档里标记不成对就返回一条四要素消息，成对则返回 null。</summary>
        private static string FindUnpairedMarker(IReadOnlyList<string> documentPaths, string root, string markerName)
        {
            var beginMarker = BeginMarker(markerName);
            var endMarker = EndMarker(markerName);

            foreach (var documentPath in documentPaths)
            {
                var lines = ReadLines(documentPath, out _);
                var openedAtLine = 0;

                for (var index = 0; index < lines.Count; index++)
                {
                    if (lines[index].Contains(beginMarker, StringComparison.Ordinal))
                    {
                        if (openedAtLine > 0)
                        {
                            return ComposeError(
                                $"{ToRelative(root, documentPath)} 第 {index + 1} 行", "标记嵌套了，上一段还没结束",
                                "把上一段的结束标记补上", endMarker);
                        }

                        openedAtLine = index + 1;
                        continue;
                    }

                    if (lines[index].Contains(endMarker, StringComparison.Ordinal))
                    {
                        if (openedAtLine == 0)
                        {
                            return ComposeError(
                                $"{ToRelative(root, documentPath)} 第 {index + 1} 行", "结束标记没有配对的开始标记",
                                "补上开始标记，或删掉这一行", beginMarker);
                        }

                        openedAtLine = 0;
                    }
                }

                if (openedAtLine > 0)
                {
                    return ComposeError(
                        $"{ToRelative(root, documentPath)} 第 {openedAtLine} 行", "开始标记没有配对的结束标记",
                        "补上结束标记——少了它就会一路删到文件末尾", endMarker);
                }
            }

            return null;
        }

        private static IEnumerable<string> ListDocuments(string root)
        {
            return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                .Where(filePath => !HasSkipSegment(ToRelative(root, filePath)));
        }

        private static bool HasSkipSegment(string relativePath)
        {
            return relativePath.Split('/').Any(segment => SkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        private static string BeginMarker(string markerName)
        {
            return "<!-- feature:" + markerName + " 开始 -->";
        }

        private static string EndMarker(string markerName)
        {
            return "<!-- feature:" + markerName + " 结束 -->";
        }

        private static string ToRelative(string root, string fullPath)
        {
            return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        }

        /// <summary>删掉数组末项之后，补掉遗留在闭合中括号前的那个逗号。</summary>
        private static void FixTrailingCommaBeforeCloseBracket(List<string> lines, int fromIndex)
        {
            for (var index = fromIndex; index < lines.Count; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("]", StringComparison.Ordinal) && index > 0)
                {
                    lines[index - 1] = lines[index - 1].TrimEnd().TrimEnd(',');
                    return;
                }

                if (trimmed.Length > 0)
                {
                    return;
                }
            }
        }

        /// <summary>删掉对象末项之后，补掉遗留在闭合大括号前的那个逗号。</summary>
        private static void FixTrailingCommaBeforeCloseBrace(List<string> lines)
        {
            for (var index = lines.Count - 1; index > 0; index--)
            {
                var trimmed = lines[index].TrimStart();
                if (!trimmed.StartsWith("}", StringComparison.Ordinal))
                {
                    continue;
                }

                var previous = lines[index - 1].TrimEnd();
                if (previous.EndsWith(",", StringComparison.Ordinal))
                {
                    lines[index - 1] = previous.TrimEnd(',');
                }

                return;
            }
        }

        private static string ExtractLastGuid(string line)
        {
            var closeIndex = line.LastIndexOf('}');
            if (closeIndex < 0)
            {
                return null;
            }

            var openIndex = line.LastIndexOf('{', closeIndex);
            return openIndex < 0 ? null : line.Substring(openIndex, closeIndex - openIndex + 1);
        }

        private static List<string> ReadLines(string filePath, out bool hasBom)
        {
            var bytes = File.ReadAllBytes(filePath);
            hasBom = bytes.Length >= Utf8Bom.Length
                     && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);
            return text.Replace("\r\n", "\n").Split('\n').ToList();
        }

        private static void WriteLines(string filePath, IReadOnlyList<string> lines, bool hasBom)
        {
            File.WriteAllText(filePath, string.Join("\n", lines), new UTF8Encoding(hasBom));
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}
