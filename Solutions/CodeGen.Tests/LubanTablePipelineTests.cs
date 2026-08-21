using System;
using System.IO;
using System.Linq;
using Template.Toolkit.CodeGen;
using Template.Toolkit.ConfigBridge;
using Xunit;

namespace Template.Toolkit.CodeGen.Tests
{
    /// <summary>配置表管线 Luban 实现的测试：defines 投影、类型映射、多主键、真实跑 Luban 与失败消息。</summary>
    public class LubanTablePipelineTests
    {
        [Fact]
        public void PipelineNameIsLuban()
        {
            ITablePipeline pipeline = new LubanTablePipeline(FindTemplateRoot());

            Assert.Equal("Luban", pipeline.PipelineName);
        }

        [Fact]
        public void WriteDeclaresBeanAndTableForAllThreeTables()
        {
            var defines = ReadDefines(LubanDefinitionWriter.Write(FindTemplateRoot(), NewWorkingDirectory()));

            Assert.Contains("<bean name=\"Bag\">", defines);
            Assert.Contains("<bean name=\"Skill\">", defines);
            Assert.Contains("<bean name=\"Monster\">", defines);
            Assert.Contains("<table name=\"TbBag\"", defines);
            Assert.Contains("<table name=\"TbSkill\"", defines);
            Assert.Contains("<table name=\"TbMonster\"", defines);
        }

        [Fact]
        public void WriteMapsAllFiveFieldTypes()
        {
            var defines = ReadDefines(LubanDefinitionWriter.Write(FindTemplateRoot(), NewWorkingDirectory()));

            Assert.Contains("type=\"int\"", defines);
            Assert.Contains("type=\"long\"", defines);
            Assert.Contains("type=\"float\"", defines);
            Assert.Contains("type=\"bool\"", defines);
            Assert.Contains("type=\"string\"", defines);
        }

        [Fact]
        public void WriteUsesUnionIndexForCompositePrimaryKey()
        {
            var defines = ReadDefines(LubanDefinitionWriter.Write(FindTemplateRoot(), NewWorkingDirectory()));

            // 怪物表双主键，合起来才唯一：用 '+' 连接成联合索引，而不是 ',' 的独立索引。
            Assert.Contains("mode=\"list\"", defines);
            Assert.Contains("index=\"LevelId+MonsterId\"", defines);
        }

        [Fact]
        public void SyncApplyAndValidateDelegateLikeScribanPipeline()
        {
            var scriban = new ScribanTablePipeline(FindTemplateRoot());
            var luban = new LubanTablePipeline(FindTemplateRoot());

            // Validate 只读，直接比较完整结果。
            Assert.Equal(scriban.Validate("Bag").Message, luban.Validate("Bag").Message);

            // Sync / Apply 会落盘（镜像 / xlsx），这里用一个不存在的表名触发失败分支，
            // 只比较两边失败消息一致，不碰真实 xlsx。
            Assert.Equal(scriban.SyncFromWorkbook("并不存在的表").Message, luban.SyncFromWorkbook("并不存在的表").Message);
            Assert.Equal(scriban.ApplyToWorkbook("并不存在的表").Message, luban.ApplyToWorkbook("并不存在的表").Message);
        }

        [Fact]
        [Trait("Category", "Luban")]
        public void GenerateAccessCodeRunsLubanAndProducesCsFiles()
        {
            ITablePipeline pipeline = new LubanTablePipeline(FindTemplateRoot());

            var writtenPaths = pipeline.GenerateAccessCode("Bag");

            Assert.NotEmpty(writtenPaths);
            foreach (var path in writtenPaths)
            {
                Assert.True(File.Exists(path), $"访问代码文件不存在：{path}");
                Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"访问代码文件为空：{path}");
            }
        }

        [Fact]
        [Trait("Category", "Luban")]
        public void ExportRuntimeDataRunsLubanAndProducesDataFile()
        {
            ITablePipeline pipeline = new LubanTablePipeline(FindTemplateRoot());

            var exported = pipeline.ExportRuntimeData("Bag");

            Assert.Single(exported);
            Assert.True(File.Exists(exported[0]), $"运行时数据文件不存在：{exported[0]}");
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(exported[0])), $"运行时数据文件为空：{exported[0]}");
        }

        [Fact]
        [Trait("Category", "Luban")]
        public void GeneratedAccessCodeContainsTableNameAndPrimaryKeyField()
        {
            ITablePipeline pipeline = new LubanTablePipeline(FindTemplateRoot());

            var writtenPaths = pipeline.GenerateAccessCode("Bag");

            var tablePath = writtenPaths.Single(path => path.EndsWith("TbBag.cs", StringComparison.Ordinal));
            var code = File.ReadAllText(tablePath);

            Assert.Contains("TbBag", code);
            Assert.Contains("ItemId", code);
        }

        [Fact]
        public void WriteReturnsEmptyListWhenSchemaDirectoryIsEmpty()
        {
            var templateRoot = NewTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(templateRoot, "Config", "Schema"));

                var written = LubanDefinitionWriter.Write(templateRoot, Path.Combine(templateRoot, "Tools", "Luban", "Config"));

                Assert.Empty(written);
            }
            finally
            {
                DeleteDirectory(templateRoot);
            }
        }

        [Fact]
        public void GenerateAccessCodeReportsMissingLubanWithFourElements()
        {
            var templateRoot = NewTempDirectory();
            try
            {
                CopyRealSchemas(templateRoot);

                var pipeline = new LubanTablePipeline(templateRoot);

                var exception = Assert.Throws<InvalidOperationException>(() => pipeline.GenerateAccessCode("Bag"));

                Assert.Contains("位置：", exception.Message);
                Assert.Contains("原因：", exception.Message);
                Assert.Contains("修复：", exception.Message);
                Assert.Contains("参考：", exception.Message);
            }
            finally
            {
                DeleteDirectory(templateRoot);
            }
        }

        private static string ReadDefines(System.Collections.Generic.IReadOnlyList<string> writtenPaths)
        {
            var definesPath = writtenPaths.Single(path => path.EndsWith("defines.xml", StringComparison.Ordinal));
            Assert.True(File.Exists(definesPath), $"defines.xml 未写出：{definesPath}");
            return File.ReadAllText(definesPath);
        }

        private static string NewWorkingDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LubanDefinitionWriterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LubanTablePipelineTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void CopyRealSchemas(string templateRoot)
        {
            var schemaDirectory = Path.Combine(templateRoot, "Config", "Schema");
            Directory.CreateDirectory(schemaDirectory);

            foreach (var source in Directory.GetFiles(Path.Combine(FindTemplateRoot(), "Config", "Schema"), "*.schema.json"))
            {
                File.Copy(source, Path.Combine(schemaDirectory, Path.GetFileName(source)));
            }
        }

        private static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // 从程序集目录逐级向上找带 Tools/Gates/Config 的那一级作为模板根——
        // 模板被复制成别的项目名之后，这个标记仍然成立，而目录名 "Template" 不再成立。
        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
            {
                directory = directory.Parent;
            }

            Assert.True(directory != null, $"从 {AppContext.BaseDirectory} 向上找不到模板根");
            return directory.FullName;
        }
    }
}
