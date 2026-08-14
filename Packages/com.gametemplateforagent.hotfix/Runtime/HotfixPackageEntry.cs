namespace GameTemplateForAgent.Hotfix
{
    /// <summary>热更清单里的一个包条目：文件名与内容校验信息，供下载后核对完整性。</summary>
    public sealed class HotfixPackageEntry
    {
        /// <summary>包在 CDN 上的逻辑名。</summary>
        public string PackageName { get; }

        /// <summary>包对应的文件名。</summary>
        public string FileName { get; }

        /// <summary>内容 SHA256 哈希，十六进制小写，下载后据此校验完整性。</summary>
        public string ContentHash { get; }

        /// <summary>包字节大小。</summary>
        public long ByteSize { get; }

        /// <summary>以包名、文件名、内容哈希与字节大小构造。</summary>
        public HotfixPackageEntry(string packageName, string fileName, string contentHash, long byteSize)
        {
            PackageName = packageName;
            FileName = fileName;
            ContentHash = contentHash;
            ByteSize = byteSize;
        }
    }
}
