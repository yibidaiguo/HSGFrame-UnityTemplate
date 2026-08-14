namespace Template.Toolkit.Indexing
{
    /// <summary>一条索引记录：命中文件的位置、guid 与内容哈希。</summary>
    public sealed class IndexEntry
    {
        /// <summary>仓库相对路径，统一用正斜杠。</summary>
        public string RelativePath { get; set; }

        /// <summary>文件名，不含目录。</summary>
        public string FileName { get; set; }

        /// <summary>同名 .meta 文件里的 guid，没有 .meta 时为空字符串。</summary>
        public string AssetGuid { get; set; }

        /// <summary>文件内容的 SHA256，小写十六进制。</summary>
        public string FileHash { get; set; }
    }
}
