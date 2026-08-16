using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 可选功能引用范围检查器：按配置里的每条规则，确认那批程序集只被该功能包目录内的 asmdef 引用。
    /// 没有这一道，「删一个包目录就等于删掉这个功能」这件事只靠人记着，而这种耦合不盯着就会自己长回来。
    /// </summary>
    public static class OptionalFeatureScopeChecker
    {
        /// <summary>与扫描根无关的目录段：拿相对模板根的路径比，不拿绝对路径比（绝对路径里的 Temp 段会误杀整棵树）。</summary>
        private static readonly string[] SkipSegments =
        {
            "bin", "obj", ".git", "Library", "Temp", "PackageCache", "HybridCLRData",
        };

        private const string ReferenceDocumentPath = "规范/结构规范-代码.md 第三节";

        private const string FixActionText =
            "把这段代码搬进该功能的包，或者让它别引这个程序集——引用范围就是可选功能的定义";

        /// <summary>
        /// 扫模板根下全部 asmdef，逐条规则比对引用范围，返回越界的引用清单。
        /// 入参为空、目录不存在、没有规则时返回空清单，不抛异常；asmdef 解析不了时跳过那一个文件。
        /// </summary>
        /// <remarks>
        /// 已知盲区：asmdef 的 references 允许写成 <c>GUID:xxx</c> 形式，那种按名字判不了，一律跳过。
        /// 本仓库的 asmdef 全部写程序集名，所以这条盲区当前不影响判定。
        /// </remarks>
        /// <param name="templateRoot">模板根目录。</param>
        /// <param name="configuration">门禁配置，规则取自 <see cref="GateConfiguration.OptionalFeatureScopes"/>。</param>
        public static IReadOnlyList<GateFinding> Check(string templateRoot, GateConfiguration configuration)
        {
            var scopes = configuration?.OptionalFeatureScopes;
            if (string.IsNullOrWhiteSpace(templateRoot) || !Directory.Exists(templateRoot) || scopes == null || scopes.Count == 0)
            {
                return Array.Empty<GateFinding>();
            }

            var rootFullPath = Path.GetFullPath(templateRoot);
            var findings = new List<GateFinding>();

            foreach (var filePath in Directory.EnumerateFiles(rootFullPath, "*.asmdef", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(rootFullPath, filePath).Replace('\\', '/');
                if (HasSkipSegment(relativePath))
                {
                    continue;
                }

                foreach (var reference in ReadReferences(filePath))
                {
                    foreach (var scope in scopes)
                    {
                        if (!Matches(reference, scope) || IsInsidePackage(relativePath, scope.PackageDirectory))
                        {
                            continue;
                        }

                        findings.Add(new GateFinding(
                            relativePath,
                            $"它引用了可选功能「{scope.FeatureName}」的程序集 {reference}，但它不在 {scope.PackageDirectory} 之内",
                            FixActionText,
                            ReferenceDocumentPath));
                    }
                }
            }

            findings.Sort((left, right) => string.CompareOrdinal(left.Location, right.Location));
            return findings;
        }

        /// <summary>引用名等于前缀、或以「前缀.」开头即命中；GUID 形式的引用一律跳过。</summary>
        private static bool Matches(string reference, OptionalFeatureScope scope)
        {
            if (string.IsNullOrWhiteSpace(reference)
                || reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)
                || scope?.ReferencePrefixes == null)
            {
                return false;
            }

            return scope.ReferencePrefixes.Any(prefix =>
                !string.IsNullOrWhiteSpace(prefix)
                && (string.Equals(reference, prefix, StringComparison.Ordinal)
                    || reference.StartsWith(prefix + ".", StringComparison.Ordinal)));
        }

        /// <summary>判断这个 asmdef 是否落在该功能的包目录之内。</summary>
        private static bool IsInsidePackage(string relativePath, string packageDirectory)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                return false;
            }

            var prefix = packageDirectory.Replace('\\', '/').TrimEnd('/') + "/";
            return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>读 asmdef 的 references 数组；读不出来返回空，不抛。</summary>
        private static IReadOnlyList<string> ReadReferences(string filePath)
        {
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    if (!document.RootElement.TryGetProperty("references", out var references)
                        || references.ValueKind != JsonValueKind.Array)
                    {
                        return Array.Empty<string>();
                    }

                    return references.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.String)
                        .Select(element => element.GetString())
                        .ToArray();
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>相对模板根的路径里出现跳过段就整条跳过，段名忽略大小写。</summary>
        private static bool HasSkipSegment(string relativePath)
        {
            return relativePath.Split('/').Any(segment => SkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }
    }
}
