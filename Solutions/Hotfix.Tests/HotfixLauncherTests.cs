using System.Collections.Generic;
using HSGhost.Hotfix;
using Xunit;

namespace HSGhost.Hotfix.Tests
{
    /// <summary>热更启动器状态机与失败回滚的测试。</summary>
    public class HotfixLauncherTests
    {
        [Fact]
        public void Launch_LocalVersionEqualsRemote_NeedsNoUpdate()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.2.3" };
            var launcher = new HotfixLauncher(storage);
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>());

            var result = launcher.Launch(manifest);

            Assert.True(result.IsSuccess);
            Assert.False(result.NeedsUpdate);
            Assert.Contains("最新", result.Message);
        }

        [Fact]
        public void Launch_LocalVersionLower_WithAllPackagesValid_SucceedsAndWritesRemoteVersion()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.2.3" };
            storage.RegisterPackage("1.3.0", "dll.bytes", "abc123");
            var launcher = new HotfixLauncher(storage);
            var manifest = new HotfixManifest("1.3.0", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("hotfix-dll", "dll.bytes", "abc123", 1024),
            });

            var result = launcher.Launch(manifest);

            Assert.True(result.IsSuccess);
            Assert.True(result.NeedsUpdate);
            Assert.Equal("1.3.0", storage.InstalledVersionText);
        }

        [Fact]
        public void Launch_PackageHashMismatch_FailsAndKeepsInstalledVersion()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.2.3" };
            storage.RegisterPackage("1.3.0", "dll.bytes", "wrong-hash");
            var launcher = new HotfixLauncher(storage);
            var manifest = new HotfixManifest("1.3.0", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("hotfix-dll", "dll.bytes", "abc123", 1024),
            });

            var result = launcher.Launch(manifest);

            Assert.False(result.IsSuccess);
            Assert.Equal("1.2.3", storage.InstalledVersionText);
            Assert.Contains("dll.bytes", result.Message);
        }

        [Fact]
        public void Launch_PackageMissing_FailsAndNamesPackage()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.2.3" };
            var launcher = new HotfixLauncher(storage);
            var manifest = new HotfixManifest("1.3.0", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("hotfix-dll", "dll.bytes", "abc123", 1024),
            });

            var result = launcher.Launch(manifest);

            Assert.False(result.IsSuccess);
            Assert.Contains("dll.bytes", result.Message);
        }

        [Fact]
        public void Rollback_WithHistory_RestoresHighestLowerVersionAndRemovesCurrent()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.3.0" };
            storage.InstalledVersions.Add("1.0.0");
            storage.InstalledVersions.Add("1.2.0");
            storage.InstalledVersions.Add("1.3.0");
            var launcher = new HotfixLauncher(storage);

            var result = launcher.Rollback();

            Assert.True(result.IsSuccess);
            Assert.Equal("1.2.0", storage.InstalledVersionText);
            Assert.Equal("1.2.0", result.RolledBackTo);
            Assert.DoesNotContain("1.3.0", storage.InstalledVersions);
        }

        [Fact]
        public void Rollback_NoHistory_FailsWithFullUpdateMessage()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.3.0" };
            storage.InstalledVersions.Add("1.3.0");
            var launcher = new HotfixLauncher(storage);

            var result = launcher.Rollback();

            Assert.False(result.IsSuccess);
            Assert.Contains("整包更新", result.Message);
        }

        [Fact]
        public void Launch_RemoteVersionUnparseable_Fails()
        {
            var storage = new InMemoryStorage { InstalledVersionText = "1.2.3" };
            var launcher = new HotfixLauncher(storage);
            var manifest = new HotfixManifest("not-a-version", new List<HotfixPackageEntry>());

            var result = launcher.Launch(manifest);

            Assert.False(result.IsSuccess);
            Assert.Contains("远端清单版本号无法解析", result.Message);
        }

        private sealed class InMemoryStorage : IHotfixStorage
        {
            private readonly Dictionary<string, HashSet<string>> _packageFiles = new Dictionary<string, HashSet<string>>();
            private readonly Dictionary<string, Dictionary<string, string>> _packageHashes = new Dictionary<string, Dictionary<string, string>>();

            /// <summary>当前已装版本号，测试用属性。</summary>
            public string InstalledVersionText { get; set; } = string.Empty;

            /// <summary>已安装的历史版本列表，测试用属性。</summary>
            public List<string> InstalledVersions { get; } = new List<string>();

            /// <summary>读取当前已装版本号文本。</summary>
            public string ReadInstalledVersionText() => InstalledVersionText;

            /// <summary>写入当前已装版本号文本。</summary>
            public void WriteInstalledVersionText(string versionText) => InstalledVersionText = versionText;

            /// <summary>判断指定版本下是否已存在名为 fileName 的包文件。</summary>
            public bool HasPackage(string versionText, string fileName)
                => _packageFiles.TryGetValue(versionText, out var files) && files.Contains(fileName);

            /// <summary>返回注册时给的包内容哈希。</summary>
            public string ComputePackageHash(string versionText, string fileName)
                => _packageHashes.TryGetValue(versionText, out var hashes) && hashes.TryGetValue(fileName, out var hash) ? hash : string.Empty;

            /// <summary>列出所有已安装的历史版本号文本。</summary>
            public IReadOnlyList<string> ListInstalledVersions() => InstalledVersions;

            /// <summary>移除指定版本的本地文件。</summary>
            public void RemoveVersion(string versionText) => InstalledVersions.Remove(versionText);

            /// <summary>注册某个版本下的一个包及其内容哈希，模拟已下载落盘的包。</summary>
            public void RegisterPackage(string versionText, string fileName, string contentHash)
            {
                if (!_packageFiles.TryGetValue(versionText, out var files))
                {
                    files = new HashSet<string>();
                    _packageFiles[versionText] = files;
                }

                files.Add(fileName);

                if (!_packageHashes.TryGetValue(versionText, out var hashes))
                {
                    hashes = new Dictionary<string, string>();
                    _packageHashes[versionText] = hashes;
                }

                hashes[fileName] = contentHash;
            }
        }
    }
}
