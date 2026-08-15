namespace GameTemplateForAgent.Hotfix
{
    /// <summary>热更包写入接口：把下载回来的包字节落到某个版本目录下。</summary>
    public interface IHotfixPackageWriter
    {
        /// <summary>把一个包的字节写进指定版本的目录，目录不存在时先建。</summary>
        void WritePackage(string versionText, string fileName, byte[] content);
    }
}
