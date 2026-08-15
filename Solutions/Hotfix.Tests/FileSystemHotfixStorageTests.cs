using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HSGFrame.Hotfix;
using Xunit;

namespace HSGFrame.Hotfix.Tests
{
    /// <summary>文件系统热更存储的测试：临时目录做夹具，跑完清理。</summary>
    public class FileSystemHotfixStorageTests
    {
        [Fact]
        public void ReadInstalledVersionText_OnFreshRoot_ReturnsEmpty()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);

                Assert.Equal(string.Empty, storage.ReadInstalledVersionText());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void WriteInstalledVersionText_ThenRead_ReturnsSameVersion()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);

                storage.WriteInstalledVersionText("1.2.3");

                Assert.Equal("1.2.3", storage.ReadInstalledVersionText());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ListInstalledVersions_WhenRootMissing_ReturnsEmpty()
        {
            var root = CreateTempRoot();
            try
            {
                var missingRoot = Path.Combine(root, "not-created");
                var storage = new FileSystemHotfixStorage(missingRoot);

                Assert.Empty(storage.ListInstalledVersions());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ListInstalledVersions_AfterWritingThreeVersions_ContainsAllThree()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                storage.WritePackage("1.0.0", "a.dll", new byte[] { 1 });
                storage.WritePackage("1.1.0", "a.dll", new byte[] { 2 });
                storage.WritePackage("1.2.0", "a.dll", new byte[] { 3 });

                var versions = storage.ListInstalledVersions();

                Assert.Equal(3, versions.Count);
                Assert.Contains("1.0.0", versions);
                Assert.Contains("1.1.0", versions);
                Assert.Contains("1.2.0", versions);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void HasPackage_ExistingAndMissing_ReturnsCorrectly()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                storage.WritePackage("1.0.0", "present.dll", new byte[] { 1 });

                Assert.True(storage.HasPackage("1.0.0", "present.dll"));
                Assert.False(storage.HasPackage("1.0.0", "missing.dll"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ComputePackageHash_Is64LowercaseHex()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                storage.WritePackage("1.0.0", "a.dll", new byte[] { 1, 2, 3 });

                var hash = storage.ComputePackageHash("1.0.0", "a.dll");

                Assert.Equal(64, hash.Length);
                Assert.Equal(hash.ToLowerInvariant(), hash);
                Assert.Matches("^[0-9a-f]{64}$", hash);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ComputePackageHash_SameContent_SameHash_DifferentByte_DifferentHash()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                storage.WritePackage("1.0.0", "same-a.dll", new byte[] { 10, 20, 30 });
                storage.WritePackage("1.0.0", "same-b.dll", new byte[] { 10, 20, 30 });
                storage.WritePackage("1.0.0", "diff.dll", new byte[] { 10, 20, 31 });

                var hashA = storage.ComputePackageHash("1.0.0", "same-a.dll");
                var hashB = storage.ComputePackageHash("1.0.0", "same-b.dll");
                var hashDiff = storage.ComputePackageHash("1.0.0", "diff.dll");

                Assert.Equal(hashA, hashB);
                Assert.NotEqual(hashA, hashDiff);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void WritePackage_ThenHasPackageTrueAndBytesMatch()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                var content = new byte[] { 5, 6, 7, 8 };

                storage.WritePackage("1.0.0", "a.dll", content);

                Assert.True(storage.HasPackage("1.0.0", "a.dll"));
                Assert.Equal(content, File.ReadAllBytes(Path.Combine(root, "1.0.0", "a.dll")));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void RemoveVersion_RemovesOnlyThatVersion()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                storage.WritePackage("1.0.0", "a.dll", new byte[] { 1 });
                storage.WritePackage("1.1.0", "a.dll", new byte[] { 2 });

                storage.RemoveVersion("1.0.0");

                Assert.False(Directory.Exists(Path.Combine(root, "1.0.0")));
                Assert.True(Directory.Exists(Path.Combine(root, "1.1.0")));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void RemoveVersion_MissingVersion_DoesNotThrow()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);

                storage.RemoveVersion("9.9.9");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void VersionText_WithDotDot_ThrowsArgumentWithFourElements()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);

                var exception = Assert.Throws<ArgumentException>(
                    () => storage.WritePackage("1..3", "a.dll", new byte[] { 1 }));

                AssertContainsFourElements(exception.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void VersionText_WithSeparator_ThrowsArgumentWithFourElements()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                var badVersion = "1" + Path.DirectorySeparatorChar + "2.3";

                var exception = Assert.Throws<ArgumentException>(
                    () => storage.WritePackage(badVersion, "a.dll", new byte[] { 1 }));

                AssertContainsFourElements(exception.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Launch_WithPreparedPackages_UpdatesInstalledVersion()
        {
            var root = CreateTempRoot();
            try
            {
                var storage = new FileSystemHotfixStorage(root);
                var content = Encoding.UTF8.GetBytes("hotfix-dll-content");
                storage.WritePackage("1.3.0", "dll.bytes", content);

                var manifest = new HotfixManifest("1.3.0", new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("hotfix-dll", "dll.bytes", ComputeSha256Hex(content), content.Length),
                });

                var launcher = new HotfixLauncher(storage);
                var result = launcher.Launch(manifest);

                Assert.True(result.IsSuccess);
                Assert.True(result.NeedsUpdate);
                Assert.Equal("1.3.0", storage.ReadInstalledVersionText());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "HotfixStorageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string ComputeSha256Hex(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static void AssertContainsFourElements(string message)
        {
            Assert.Contains("位置", message);
            Assert.Contains("原因", message);
            Assert.Contains("修复", message);
            Assert.Contains("参考", message);
        }
    }
}
