namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条重命名计划：原文件路径、规范后的新文件名与改动原因。</summary>
    public sealed class AssetRenamePlan
    {
        /// <summary>
        /// 构造一条重命名计划。
        /// </summary>
        /// <param name="originalPath">原文件完整路径。</param>
        /// <param name="normalizedFileName">规范后的新文件名（不含目录）。</param>
        /// <param name="reason">为什么要重命名。</param>
        public AssetRenamePlan(string originalPath, string normalizedFileName, string reason)
        {
            OriginalPath = originalPath;
            NormalizedFileName = normalizedFileName;
            Reason = reason;
        }

        /// <summary>原文件完整路径。</summary>
        public string OriginalPath { get; }

        /// <summary>规范后的新文件名（不含目录）。</summary>
        public string NormalizedFileName { get; }

        /// <summary>为什么要重命名。</summary>
        public string Reason { get; }
    }
}
