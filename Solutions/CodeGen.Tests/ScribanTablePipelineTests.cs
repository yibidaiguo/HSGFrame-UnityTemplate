using System;
using System.IO;
using System.Linq;
using Template.Toolkit.CodeGen;
using Template.Toolkit.ConfigBridge;
using Xunit;

namespace Template.Toolkit.CodeGen.Tests
{
    /// <summary>配置表管线可替换面的实现测试：转发、生成访问代码与运行时数据。</summary>
    public class ScribanTablePipelineTests
    {
        [Fact]
        public void PipelineIsUsableThroughTheInterface()
        {
            ITablePipeline pipeline = new ScribanTablePipeline(FindTemplateRoot());

            Assert.Contains("Scriban", pipeline.PipelineName);
        }

        [Fact]
        public void ValidateForwardsToConfigBridge()
        {
            ITablePipeline pipeline = new ScribanTablePipeline(FindTemplateRoot());

            var result = pipeline.Validate("背包");

            Assert.True(result.IsSuccess, result.Message);
        }

        [Fact]
        public void GenerateAccessCodeWritesTheTargetForThatTable()
        {
            ITablePipeline pipeline = new ScribanTablePipeline(FindTemplateRoot());

            var writtenPaths = pipeline.GenerateAccessCode("背包");

            Assert.Single(writtenPaths);
            Assert.Contains("BagTable.cs", writtenPaths[0]);
        }

        [Fact]
        public void GenerateAccessCodeReturnsNothingForUnknownTable()
        {
            ITablePipeline pipeline = new ScribanTablePipeline(FindTemplateRoot());

            Assert.Empty(pipeline.GenerateAccessCode("并不存在的表"));
        }

        [Fact]
        public void ExportRuntimeDataPointsAtTheMirrorFile()
        {
            ITablePipeline pipeline = new ScribanTablePipeline(FindTemplateRoot());

            var exported = pipeline.ExportRuntimeData("背包");

            Assert.Single(exported);
            Assert.True(File.Exists(exported[0]), $"运行时数据文件不存在：{exported[0]}");
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
