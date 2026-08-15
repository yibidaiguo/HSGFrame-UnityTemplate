using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HSGFrame.Hotfix
{
    /// <summary>热更本地存储的文件系统实现：一个版本一个子目录，已装版本号单独记一个文件。</summary>
    public sealed class FileSystemHotfixStorage : IHotfixStorage, IHotfixPackageWriter
    {
        private const string InstalledVersionFileName = "已装版本.txt";

        private readonly string _rootDirectory;

        /// <summary>以热更根目录构造，根目录下每个版本一个同名子目录。</summary>
        /// <param name="rootDirectory">热更根目录。</param>
        public FileSystemHotfixStorage(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
        }

        /// <summary>热更根目录。</summary>
        public string RootDirectory => _rootDirectory;

        /// <summary>读取当前已装版本号文本，文件不存在时返回空串。</summary>
        public string ReadInstalledVersionText()
        {
            var filePath = Path.Combine(_rootDirectory, InstalledVersionFileName);
            return File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        }

        /// <summary>写入当前已装版本号文本，根目录不存在时先建。</summary>
        public void WriteInstalledVersionText(string versionText)
        {
            Directory.CreateDirectory(_rootDirectory);
            File.WriteAllText(Path.Combine(_rootDirectory, InstalledVersionFileName), versionText);
        }

        /// <summary>判断指定版本下是否已存在名为 fileName 的包文件。</summary>
        public bool HasPackage(string versionText, string fileName)
        {
            ValidateVersionText(versionText);
            return File.Exists(GetPackagePath(versionText, fileName));
        }

        /// <summary>计算指定版本下名为 fileName 的包的 SHA256 十六进制小写哈希。</summary>
        public string ComputePackageHash(string versionText, string fileName)
        {
            ValidateVersionText(versionText);
            using var stream = File.OpenRead(GetPackagePath(versionText, fileName));
            using var sha256 = SHA256.Create();
            return ToLowerHex(sha256.ComputeHash(stream));
        }

        /// <summary>列出所有已安装的历史版本号文本，根目录不存在时返回空清单。</summary>
        public IReadOnlyList<string> ListInstalledVersions()
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(_rootDirectory).Select(Path.GetFileName).ToList();
        }

        /// <summary>移除指定版本的本地文件，版本目录不存在时安静返回。</summary>
        public void RemoveVersion(string versionText)
        {
            ValidateVersionText(versionText);
            var versionDirectory = GetVersionDirectory(versionText);
            if (Directory.Exists(versionDirectory))
            {
                Directory.Delete(versionDirectory, recursive: true);
            }
        }

        /// <summary>把一个包的字节写进指定版本的目录，目录不存在时先建。</summary>
        public void WritePackage(string versionText, string fileName, byte[] content)
        {
            ValidateVersionText(versionText);
            var versionDirectory = GetVersionDirectory(versionText);
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllBytes(Path.Combine(versionDirectory, fileName), content);
        }

        private string GetVersionDirectory(string versionText) => Path.Combine(_rootDirectory, versionText);

        private string GetPackagePath(string versionText, string fileName) => Path.Combine(GetVersionDirectory(versionText), fileName);

        private static void ValidateVersionText(string versionText)
        {
            // 版本号来自远端清单，直接当目录名拼进路径，远端能借此写到根目录之外，必须挡住。
            var hasSeparator = versionText.IndexOf(Path.DirectorySeparatorChar) >= 0
                || versionText.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
            if (hasSeparator || versionText.Contains(".."))
            {
                throw new ArgumentException(
                    $"位置：{versionText}；原因：版本号含路径分隔符或「..」，不能作为目录名；修复：改用纯数字点分的三段式版本号；参考：1.2.3");
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                // "x2" 输出两位十六进制小写，与 ContentHash 的约定一致。
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
