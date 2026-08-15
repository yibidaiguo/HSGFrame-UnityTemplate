using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一次引用扫描的结果：没人引用的资产与指向不存在资产的悬空引用。</summary>
    public sealed class AssetReferenceReport
    {
        /// <summary>构造一次引用扫描结果。</summary>
        /// <param name="unreferencedAssetPaths">无人引用资产路径，相对 Assets 根。</param>
        /// <param name="danglingReferences">悬空引用：引用方相对路径 → 找不到的 guid 集合。</param>
        public AssetReferenceReport(
            IReadOnlyList<string> unreferencedAssetPaths,
            IReadOnlyDictionary<string, IReadOnlyList<string>> danglingReferences)
        {
            UnreferencedAssetPaths = unreferencedAssetPaths;
            DanglingReferences = danglingReferences;
        }

        /// <summary>没有被任何文本资产引用的资产路径，相对 Assets 根。</summary>
        public IReadOnlyList<string> UnreferencedAssetPaths { get; }

        /// <summary>悬空引用：键是引用方的相对路径，值是它引用却找不到的 guid 集合。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> DanglingReferences { get; }
    }

    /// <summary>按 .meta 里的 guid 扫描资产之间的引用关系。</summary>
    public static class AssetReferenceScanner
    {
        // .meta 里的 guid 行整行匹配，要求小写 32 位十六进制；大小写或格式不符的行直接忽略。
        private static readonly Regex MetaGuidPattern = new Regex("^guid: ([0-9a-f]{32})$", RegexOptions.Compiled);

        // 文本资产里的引用形如「guid: 0a1b…」，出现在任意位置。
        private static readonly Regex ReferenceGuidPattern = new Regex("guid: ([0-9a-f]{32})", RegexOptions.Compiled);

        // 这些东西不靠 guid 被引用：脚本与程序集定义靠编译进程序集，样式与模板靠代码里的名字加载，
        // 配置与文档根本不是内容资产。把它们算进「无人引用」只会淹掉真正的孤儿资产。
        private static readonly HashSet<string> NonContentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".json", ".md", ".txt", ".xml",
            ".uss", ".uxml", ".shader", ".hlsl", ".cginc", ".dll", ".rsp"
        };

        private static readonly string[] DefaultScannedExtensions =
        {
            ".unity", ".prefab", ".asset", ".mat", ".controller", ".playable"
        };

        // Unity 内置资源的 guid 形如 0000000000000000e000000000000000：16 个 0、一位十六进制、再 15 个 0。
        // 它们不对应工程里的任何文件，把它们算成悬空引用会让每个场景都无谓地报错。
        private static readonly Regex BuiltinGuidPattern = new Regex("^0{16}[0-9a-f]0{15}$", RegexOptions.Compiled);

        /// <summary>扫描 Assets 根下的引用关系。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="scannedExtensions">要当作引用方读取的文本资产扩展名，为空时用默认集合。</param>
        /// <param name="additionalGuidSourceDirectories">额外的 guid 来源目录（如 Packages），只用来认领 guid，其中的资产不参与「无人引用」判定。</param>
        public static AssetReferenceReport Scan(
            string assetsRootDirectory,
            IReadOnlyList<string> scannedExtensions = null,
            IReadOnlyList<string> additionalGuidSourceDirectories = null)
        {
            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            if (!Directory.Exists(assetsRoot))
            {
                return new AssetReferenceReport(Array.Empty<string>(), new Dictionary<string, IReadOnlyList<string>>());
            }

            // 先扫全部 .meta，建「guid → 资产相对路径」表。
            var guidToAssetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metaPath in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
            {
                var assetRelativePath = ToRelativePath(assetsRoot, metaPath);
                assetRelativePath = assetRelativePath.Substring(0, assetRelativePath.Length - ".meta".Length);

                var guid = ReadMetaGuid(metaPath);
                if (guid != null)
                {
                    guidToAssetPath[guid] = assetRelativePath;
                }
            }

            // 引用可以指向 Packages 里的资产（UPM 包的脚本与配置）。那些 guid 认领得到就不算悬空，
            // 但它们不属于本工程的资产，因此只进认领表、不进「无人引用」的候选集。
            var externalGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var externalRoot in additionalGuidSourceDirectories ?? Array.Empty<string>())
            {
                if (!Directory.Exists(externalRoot))
                {
                    continue;
                }

                foreach (var metaPath in Directory.EnumerateFiles(externalRoot, "*.meta", SearchOption.AllDirectories))
                {
                    var guid = ReadMetaGuid(metaPath);
                    if (guid != null)
                    {
                        externalGuids.Add(guid);
                    }
                }
            }

            var extensionSet = BuildExtensionSet(scannedExtensions);
            var referencedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var danglingByReferencer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            // 再扫引用方文本资产，找出它们引用的全部 guid。
            foreach (var assetPath in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(assetPath);
                if (!extensionSet.Contains(extension))
                {
                    continue;
                }

                var relativePath = ToRelativePath(assetsRoot, assetPath);

                IReadOnlyList<string> referenced;
                try
                {
                    referenced = ReadReferencedGuids(assetPath);
                }
                catch (IOException)
                {
                    // 文件被占用读不动时跳过它，别让整趟扫描失败。
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var guid in referenced)
                {
                    referencedGuids.Add(guid);
                    if (guidToAssetPath.ContainsKey(guid)
                        || externalGuids.Contains(guid)
                        || BuiltinGuidPattern.IsMatch(guid))
                    {
                        continue;
                    }

                    if (!danglingByReferencer.TryGetValue(relativePath, out var danglingSet))
                    {
                        danglingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        danglingByReferencer.Add(relativePath, danglingSet);
                    }

                    danglingSet.Add(guid);
                }
            }

            // 无人引用：guid 表里有、从没被引用方提到、且在磁盘上不是目录（目录的 .meta 不算资产）。
            var unreferenced = new List<string>();
            foreach (var pair in guidToAssetPath)
            {
                if (referencedGuids.Contains(pair.Key))
                {
                    continue;
                }

                var absolutePath = Path.Combine(assetsRoot, pair.Value.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(absolutePath))
                {
                    continue;
                }

                if (NonContentExtensions.Contains(Path.GetExtension(pair.Value)))
                {
                    continue;
                }

                unreferenced.Add(pair.Value);
            }

            unreferenced.Sort(string.CompareOrdinal);

            var danglingReferences = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var pair in danglingByReferencer)
            {
                var guidList = pair.Value.ToList();
                guidList.Sort(string.CompareOrdinal);
                danglingReferences.Add(pair.Key, guidList);
            }

            return new AssetReferenceReport(unreferenced, danglingReferences);
        }

        /// <summary>扫描引用边：键是引用方资产相对路径，值是被它引用的资产相对路径列表。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="scannedExtensions">要当作引用方读取的文本资产扩展名，为空时用默认集合。</param>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScanReferenceEdges(
            string assetsRootDirectory,
            IReadOnlyList<string> scannedExtensions = null)
        {
            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            if (!Directory.Exists(assetsRoot))
            {
                return new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            }

            // 先扫全部 .meta，建「guid → 资产相对路径」表。
            var guidToAssetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metaPath in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
            {
                var assetRelativePath = ToRelativePath(assetsRoot, metaPath);
                assetRelativePath = assetRelativePath.Substring(0, assetRelativePath.Length - ".meta".Length);

                var guid = ReadMetaGuid(metaPath);
                if (guid != null)
                {
                    guidToAssetPath[guid] = assetRelativePath;
                }
            }

            var extensionSet = BuildExtensionSet(scannedExtensions);
            var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            // 再扫引用方文本资产，读出它引用的全部 guid；能认领到相对路径的才成为一条边。
            foreach (var assetPath in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(assetPath);
                if (!extensionSet.Contains(extension))
                {
                    continue;
                }

                var relativePath = ToRelativePath(assetsRoot, assetPath);

                IReadOnlyList<string> referenced;
                try
                {
                    referenced = ReadReferencedGuids(assetPath);
                }
                catch (IOException)
                {
                    // 文件被占用读不动时跳过它，别让整趟扫描失败。
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var guid in referenced)
                {
                    // 查不到相对路径的 guid 是悬空引用，由 Scan 负责报，这里直接跳过。
                    if (!guidToAssetPath.TryGetValue(guid, out var referencedPath))
                    {
                        continue;
                    }

                    // 自己引用自己的边没有方向可言，不构成依赖。
                    if (string.Equals(referencedPath, relativePath, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!edges.TryGetValue(relativePath, out var referencedSet))
                    {
                        referencedSet = new HashSet<string>(StringComparer.Ordinal);
                        edges.Add(relativePath, referencedSet);
                    }

                    referencedSet.Add(referencedPath);
                }
            }

            var sortedEdges = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var pair in edges)
            {
                var pathList = pair.Value.ToList();
                pathList.Sort(string.CompareOrdinal);
                sortedEdges.Add(pair.Key, pathList);
            }

            return sortedEdges;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            try
            {
                foreach (var line in File.ReadLines(metaPath))
                {
                    var match = MetaGuidPattern.Match(line);
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }

                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static IReadOnlyList<string> ReadReferencedGuids(string assetPath)
        {
            var text = File.ReadAllText(assetPath);
            var guids = new List<string>();
            foreach (Match match in ReferenceGuidPattern.Matches(text))
            {
                guids.Add(match.Groups[1].Value);
            }

            return guids;
        }

        private static HashSet<string> BuildExtensionSet(IReadOnlyList<string> scannedExtensions)
        {
            var extensions = scannedExtensions == null || scannedExtensions.Count == 0
                ? DefaultScannedExtensions
                : scannedExtensions;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in extensions)
            {
                set.Add(extension.StartsWith(".", StringComparison.Ordinal)
                    ? extension
                    : "." + extension);
            }

            return set;
        }

        private static string ToRelativePath(string root, string fullPath)
        {
            return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        }
    }
}
