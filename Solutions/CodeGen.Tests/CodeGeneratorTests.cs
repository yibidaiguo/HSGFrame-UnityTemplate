using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.CodeGen;
using Xunit;

namespace Template.Toolkit.CodeGen.Tests
{
    /// <summary>配置表访问代码生成的内容、幂等与校验模式测试。</summary>
    public class CodeGeneratorTests
    {
        private static CodeGenerationTarget BagTarget()
        {
            return LoadConfiguration().Targets.Single(target => target.TargetName == "背包表访问代码");
        }

        [Fact]
        public void RenderProducesStronglyTypedAccessCodeWithChineseComments()
        {
            var rendered = CodeGenerator.Render(FindTemplateRoot(), BagTarget());

            Assert.Contains("class BagTable", rendered);
            Assert.Contains("public int ItemId { get; set; }", rendered);
            Assert.Contains("编号", rendered);
        }

        [Fact]
        public void RenderIsIdempotent()
        {
            var templateRoot = FindTemplateRoot();
            var target = BagTarget();

            Assert.Equal(CodeGenerator.Render(templateRoot, target), CodeGenerator.Render(templateRoot, target));
        }

        [Fact]
        public void WriteIfChangedReturnsFalseOnSecondRun()
        {
            var templateRoot = FindTemplateRoot();
            var target = BagTarget();

            CodeGenerator.WriteIfChanged(templateRoot, target);

            Assert.False(CodeGenerator.WriteIfChanged(templateRoot, target));
        }

        [Fact]
        public void VerifyReportsDriftAfterProductIsEdited()
        {
            var templateRoot = FindTemplateRoot();
            var target = BagTarget();
            var configuration = BagConfiguration();
            var outputPath = Path.Combine(templateRoot, target.OutputPath);

            CodeGenerator.WriteIfChanged(templateRoot, target);
            var originalText = File.ReadAllText(outputPath);
            try
            {
                File.WriteAllText(outputPath, originalText + "// 人为改动\n");
                var problems = CodeGenerator.Verify(templateRoot, configuration);

                Assert.Single(problems);
                Assert.Contains("BagTable.cs", problems[0]);
            }
            finally
            {
                File.WriteAllText(outputPath, originalText);
            }
        }

        [Fact]
        public void VerifyPassesForFreshlyGeneratedProduct()
        {
            var templateRoot = FindTemplateRoot();
            var configuration = BagConfiguration();

            CodeGenerator.WriteIfChanged(templateRoot, configuration.Targets.Single());

            Assert.Empty(CodeGenerator.Verify(templateRoot, configuration));
        }

        private static CodeGenerationConfiguration LoadConfiguration()
        {
            return CodeGenerationConfiguration.LoadFromFile(
                Path.Combine(FindTemplateRoot(), "Tools", "CodeGen", "Config", "codegen-config.json"));
        }

        // 生成清单现在有三条 target，但这两条「手改一个产物 / 刚生成即一致」的用例只关心背包这一张表，
        // 所以用单 target 清单隔离，避免 Verify 顺带把尚未生成的技能表、怪物表也报成缺失。
        private static CodeGenerationConfiguration BagConfiguration()
        {
            return new CodeGenerationConfiguration
            {
                Targets = new List<CodeGenerationTarget> { BagTarget() }
            };
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
