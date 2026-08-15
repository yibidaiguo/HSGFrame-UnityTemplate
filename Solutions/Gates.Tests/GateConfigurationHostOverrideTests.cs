using System;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>
    /// 宿主专属配置（gate-config.host.json）与通用配置的合并规则。
    /// 分成两个文件是为了让 template.sync 整份同步时，来源仓库的目录前缀不会被带到去向仓库。
    /// </summary>
    public class GateConfigurationHostOverrideTests
    {
        [Fact]
        public void HostFileOverridesWhitelistAndEditorOwnedPrefixes()
        {
            var directory = NewTempDirectory();
            try
            {
                var configPath = WriteConfiguration(directory, "gate-config.json",
                    "{\"documentLineLimit\": 200, \"changedPathWhitelist\": [\"Template/\"], \"editorOwnedPathPrefixes\": [\"Old/\"]}");
                WriteConfiguration(directory, GateConfiguration.HostConfigurationFileName,
                    "{\"changedPathWhitelist\": [\"HostRoot/\"], \"editorOwnedPathPrefixes\": [\"HostEditor/\"]}");

                var configuration = GateConfiguration.LoadFromFile(configPath);

                Assert.Equal(new[] { "HostRoot/" }, configuration.ChangedPathWhitelist);
                Assert.Equal(new[] { "HostEditor/" }, configuration.EditorOwnedPathPrefixes);

                // 通用项不受宿主文件影响。
                Assert.Equal(200, configuration.DocumentLineLimit);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void MissingHostFileLeavesGenericValuesInPlace()
        {
            var directory = NewTempDirectory();
            try
            {
                var configPath = WriteConfiguration(directory, "gate-config.json",
                    "{\"changedPathWhitelist\": [\"Template/\"]}");

                var configuration = GateConfiguration.LoadFromFile(configPath);

                Assert.Equal(new[] { "Template/" }, configuration.ChangedPathWhitelist);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void HostFileWithOnlyOneKeyLeavesTheOtherKeyAlone()
        {
            var directory = NewTempDirectory();
            try
            {
                var configPath = WriteConfiguration(directory, "gate-config.json",
                    "{\"changedPathWhitelist\": [\"Template/\"], \"editorOwnedPathPrefixes\": [\"Old/\"]}");
                WriteConfiguration(directory, GateConfiguration.HostConfigurationFileName,
                    "{\"changedPathWhitelist\": [\"HostRoot/\"]}");

                var configuration = GateConfiguration.LoadFromFile(configPath);

                Assert.Equal(new[] { "HostRoot/" }, configuration.ChangedPathWhitelist);
                Assert.Equal(new[] { "Old/" }, configuration.EditorOwnedPathPrefixes);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void HostConfigurationPathSitsBesideTheGenericOne()
        {
            var directory = NewTempDirectory();
            try
            {
                var configPath = Path.Combine(directory, "gate-config.json");

                var hostPath = GateConfiguration.ResolveHostConfigurationPath(configPath);

                Assert.Equal(Path.Combine(directory, GateConfiguration.HostConfigurationFileName), hostPath);
                Assert.Null(GateConfiguration.ResolveHostConfigurationPath(null));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>宿主配置里的 genericNameBlacklist 覆盖通用配置里的同名项。</summary>
        [Fact]
        public void HostConfigurationOverridesGenericNameBlacklist()
        {
            var directory = NewTempDirectory();
            try
            {
                var configPath = WriteConfiguration(directory, "gate-config.json",
                    "{\"genericNameBlacklist\": [\"Generic\"]}");
                WriteConfiguration(directory, GateConfiguration.HostConfigurationFileName,
                    "{\"genericNameBlacklist\": [\"HostOnly\"]}");

                var configuration = GateConfiguration.LoadFromFile(configPath);

                Assert.Equal(new[] { "HostOnly" }, configuration.GenericNameBlacklist);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string WriteConfiguration(string directory, string fileName, string json)
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, json);
            return path;
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gate-host-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
