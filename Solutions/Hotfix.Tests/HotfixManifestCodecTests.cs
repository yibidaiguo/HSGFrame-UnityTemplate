using System;
using System.Collections.Generic;
using GameTemplateForAgent.Hotfix;
using Xunit;

namespace GameTemplateForAgent.Hotfix.Tests
{
    /// <summary>热更清单 JSON 编解码的测试。</summary>
    public class HotfixManifestCodecTests
    {
        [Fact]
        public void RoundTrip_PreservesVersionAndPackages()
        {
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("Hotfix.Logic", "Hotfix.Logic.dll", "abc123", 1234),
                new HotfixPackageEntry("Hotfix.Data", "Hotfix.Data.dll", "def456", 5678),
            });

            var parsed = HotfixManifestCodec.FromJson(HotfixManifestCodec.ToJson(manifest));

            Assert.Equal("1.2.3", parsed.VersionText);
            Assert.Equal(2, parsed.Packages.Count);
            Assert.Equal("Hotfix.Logic", parsed.Packages[0].PackageName);
            Assert.Equal("Hotfix.Logic.dll", parsed.Packages[0].FileName);
            Assert.Equal("abc123", parsed.Packages[0].ContentHash);
            Assert.Equal(1234, parsed.Packages[0].ByteSize);
        }

        [Fact]
        public void Json_UsesPackHotfixKeyShape()
        {
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("Hotfix.Logic", "Hotfix.Logic.dll", "abc", 1234),
            });

            var json = HotfixManifestCodec.ToJson(manifest);

            Assert.Contains("\"versionText\"", json);
            Assert.Contains("\"packages\"", json);
            Assert.Contains("\"packageName\"", json);
            Assert.Contains("\"fileName\"", json);
            Assert.Contains("\"contentHash\"", json);
            Assert.Contains("\"byteSize\"", json);
            Assert.DoesNotContain("\"VersionText\"", json);

            var parsed = HotfixManifestCodec.FromJson(json);
            Assert.Equal("1.2.3", parsed.VersionText);
            Assert.Equal("Hotfix.Logic", parsed.Packages[0].PackageName);
        }

        [Fact]
        public void RoundTrip_EmptyPackageList_Works()
        {
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>());

            var parsed = HotfixManifestCodec.FromJson(HotfixManifestCodec.ToJson(manifest));

            Assert.Equal("1.2.3", parsed.VersionText);
            Assert.Empty(parsed.Packages);
        }

        [Fact]
        public void FromJson_MissingVersionText_ThrowsWithFourElements()
        {
            var json = "{ \"packages\": [] }";

            var exception = Assert.Throws<HotfixManifestException>(() => HotfixManifestCodec.FromJson(json));

            AssertContainsFourElements(exception.Message);
        }

        [Fact]
        public void FromJson_MissingFileName_ThrowsWithFourElements()
        {
            var json = "{ \"versionText\": \"1.2.3\", \"packages\": [ { \"packageName\": \"x\", \"contentHash\": \"abc\", \"byteSize\": 1 } ] }";

            var exception = Assert.Throws<HotfixManifestException>(() => HotfixManifestCodec.FromJson(json));

            AssertContainsFourElements(exception.Message);
        }

        [Fact]
        public void FromJson_BrokenJson_ThrowsWithFourElements()
        {
            var json = "{ not valid json";

            var exception = Assert.Throws<HotfixManifestException>(() => HotfixManifestCodec.FromJson(json));

            AssertContainsFourElements(exception.Message);
        }

        [Fact]
        public void RoundTrip_LargeByteSize_PreservesValue()
        {
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("big", "big.dll", "abc", long.MaxValue),
            });

            var parsed = HotfixManifestCodec.FromJson(HotfixManifestCodec.ToJson(manifest));

            Assert.Equal(long.MaxValue, parsed.Packages[0].ByteSize);
        }

        [Fact]
        public void ToJson_ContainsNoChineseCharacters()
        {
            var manifest = new HotfixManifest("1.2.3", new List<HotfixPackageEntry>
            {
                new HotfixPackageEntry("Hotfix.Logic", "Hotfix.Logic.dll", "abc", 1234),
            });

            var json = HotfixManifestCodec.ToJson(manifest);

            Assert.DoesNotContain(json, character => character >= 0x4e00 && character <= 0x9fff);
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
