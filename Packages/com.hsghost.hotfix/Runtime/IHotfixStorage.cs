using System.Collections.Generic;

namespace HSGhost.Hotfix
{
    /// <summary>热更本地存储接口：读写已装版本号、校验本地包、维护历史版本。真实实现走文件系统，本轮只提供接口。</summary>
    public interface IHotfixStorage
    {
        /// <summary>读取当前已装版本号文本，首次安装返回空字符串。</summary>
        string ReadInstalledVersionText();

        /// <summary>写入当前已装版本号文本。</summary>
        void WriteInstalledVersionText(string versionText);

        /// <summary>判断指定版本下是否已存在名为 fileName 的包文件。</summary>
        bool HasPackage(string versionText, string fileName);

        /// <summary>计算指定版本下名为 fileName 的包的 SHA256 十六进制小写哈希。</summary>
        string ComputePackageHash(string versionText, string fileName);

        /// <summary>列出所有已安装的历史版本号文本。</summary>
        IReadOnlyList<string> ListInstalledVersions();

        /// <summary>移除指定版本的本地文件。</summary>
        void RemoveVersion(string versionText);
    }
}
