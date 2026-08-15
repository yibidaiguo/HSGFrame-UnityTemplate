using System;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>门禁配置加载在缺失文件、残缺 JSON 与大小写上的边界行为。</summary>
    public class GateConfigurationBoundaryTests
    {
        [Fact]
        public void LoadFromFileThrowsFileNotFoundExceptionWhenMissing()
        {
            var directory = NewTempDirectory();
            try
            {
                var missing = Path.Combine(directory, "nope.json");

                Assert.Throws<FileNotFoundException>(() => GateConfiguration.LoadFromFile(missing));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadFromFileLeavesMissingFieldsNull()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "gate-config.json");
                File.WriteAllText(path, "{\"documentLineLimit\": 123}");

                var configuration = GateConfiguration.LoadFromFile(path);

                Assert.Equal(123, configuration.DocumentLineLimit);
                Assert.Null(configuration.AbbreviationBlacklist);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadFromFileEmptyObjectLeavesAllDefaults()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "gate-config.json");
                File.WriteAllText(path, "{}");

                var configuration = GateConfiguration.LoadFromFile(path);

                Assert.Equal(0, configuration.DocumentLineLimit);
                Assert.Null(configuration.AbbreviationBlacklist);
                Assert.Null(configuration.DirectoryNamePattern);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadFromFileReadsCaseInsensitiveFieldName()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "gate-config.json");
                File.WriteAllText(path, "{\"documentlinelimit\": 42}");

                var configuration = GateConfiguration.LoadFromFile(path);

                Assert.Equal(42, configuration.DocumentLineLimit);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
