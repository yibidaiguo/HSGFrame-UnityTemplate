using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条资产校验发现：位置、原因与修复动作。</summary>
    public sealed class AssetFinding
    {
        /// <summary>
        /// 构造一条资产校验发现。
        /// </summary>
        /// <param name="location">发现所在的文件路径。</param>
        /// <param name="reason">违规原因。</param>
        /// <param name="fixAction">建议的修复动作。</param>
        public AssetFinding(string location, string reason, string fixAction)
        {
            Location = location;
            Reason = reason;
            FixAction = fixAction;
        }

        /// <summary>发现所在的文件路径。</summary>
        public string Location { get; }

        /// <summary>违规原因。</summary>
        public string Reason { get; }

        /// <summary>建议的修复动作。</summary>
        public string FixAction { get; }

        /// <summary>把三要素拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"位置：{Location}；原因：{Reason}；修复：{FixAction}";
        }
    }

    /// <summary>对单个目录跑四类资产校验：基础合规、引用完整性、冗余孤儿、依赖方向。</summary>
    public static class AssetValidator
    {
        // 正式目录前缀形如「T_」：以大写字母开头、后跟字母数字、再下划线。
        // 收件箱 _Inbox 里出现这种名字，说明该文件已经按正式目录命名却还没归档。
        private static readonly Regex FormalPrefixPattern = new Regex("^[A-Z][A-Za-z0-9]*_", RegexOptions.Compiled);

        /// <summary>校验目录下全部资产，返回按四类规则归纳的发现列表。</summary>
        /// <param name="directoryPath">被校验目录；相对路径须与 referencedRelativePaths 同基准。</param>
        /// <param name="rule">该目录的导入规则；为 null 时返回空列表。</param>
        /// <param name="referencedRelativePaths">被索引引用的相对路径集合；传空集合时跳过冗余孤儿校验。</param>
        public static IReadOnlyList<AssetFinding> Validate(
            string directoryPath,
            AssetImportRule rule,
            IReadOnlyCollection<string> referencedRelativePaths)
        {
            var findings = new List<AssetFinding>();
            if (rule == null || !Directory.Exists(directoryPath))
            {
                return findings;
            }

            var assetNames = new List<string>();
            var metaNames = new List<string>();

            foreach (var filePath in Directory.EnumerateFiles(directoryPath))
            {
                var fileName = Path.GetFileName(filePath);
                if (fileName.EndsWith(".meta", StringComparison.Ordinal))
                {
                    metaNames.Add(fileName.Substring(0, fileName.Length - ".meta".Length));
                }
                else if (!string.Equals(fileName, "导入规则.json", StringComparison.Ordinal))
                {
                    assetNames.Add(fileName);
                }
            }

            var referencedPaths = BuildReferenceSet(referencedRelativePaths);
            var isInboxDirectory = IsInboxDirectory(directoryPath);

            foreach (var fileName in assetNames)
            {
                var fullPath = Path.Combine(directoryPath, fileName);
                var location = NormalizePath(fullPath);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();

                if (!ContainsExtension(rule.AllowedExtensions, extension))
                {
                    findings.Add(new AssetFinding(
                        location,
                        $"扩展名「{extension}」不在允许集合内",
                        $"改用 {FormatExtensions(rule.AllowedExtensions)} 之一"));
                }

                var fileLength = new FileInfo(fullPath).Length;
                if (fileLength > rule.MaximumFileBytes)
                {
                    findings.Add(new AssetFinding(
                        location,
                        $"文件字节数 {fileLength} 超过上限 {rule.MaximumFileBytes}",
                        "压缩或缩小尺寸"));
                }

                var normalizedName = AssetNameNormalizer.Normalize(fileName, rule);
                if (!string.Equals(normalizedName, fileName, StringComparison.Ordinal))
                {
                    findings.Add(new AssetFinding(
                        location,
                        $"文件名不符合规范，应为「{normalizedName}」",
                        "运行 asset.import 自动重命名"));
                }

                if (!metaNames.Contains(fileName))
                {
                    findings.Add(new AssetFinding(
                        location,
                        "资产文件缺少 .meta 文件",
                        "让 Unity 重新导入以生成 .meta"));
                }

                if (referencedPaths.Count > 0 && !referencedPaths.Contains(location))
                {
                    findings.Add(new AssetFinding(
                        location,
                        "疑似无人引用",
                        "确认无引用后删除或归档"));
                }

                if (isInboxDirectory && FormalPrefixPattern.IsMatch(fileName))
                {
                    findings.Add(new AssetFinding(
                        location,
                        "文件已带正式前缀却仍停留在 _Inbox",
                        "归档到正式目录"));
                }
            }

            foreach (var metaName in metaNames)
            {
                if (!assetNames.Contains(metaName))
                {
                    var metaPath = Path.Combine(directoryPath, metaName + ".meta");
                    findings.Add(new AssetFinding(
                        NormalizePath(metaPath),
                        "存在孤儿 .meta，对应资产文件缺失",
                        "删除孤儿 .meta 或补回资产文件"));
                }
            }

            return findings;
        }

        private static HashSet<string> BuildReferenceSet(IReadOnlyCollection<string> referencedRelativePaths)
        {
            if (referencedRelativePaths == null || referencedRelativePaths.Count == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            return new HashSet<string>(referencedRelativePaths, StringComparer.Ordinal);
        }

        private static bool ContainsExtension(IReadOnlyList<string> allowedExtensions, string extension)
        {
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(allowedExtension, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatExtensions(IReadOnlyList<string> allowedExtensions)
        {
            return string.Join(" / ", allowedExtensions);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static bool IsInboxDirectory(string directoryPath)
        {
            return directoryPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "_Inbox", StringComparison.OrdinalIgnoreCase));
        }
    }
}
