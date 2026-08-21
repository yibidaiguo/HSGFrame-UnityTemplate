using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Template.Toolkit.CodeGen;
using Xunit;

namespace Template.Toolkit.CodeGen.Tests
{
    /// <summary>多表生成：一次跑全部目标、幂等、按产物校验与双主键处理。</summary>
    public class MultiTableGenerationTests
    {
        [Fact]
        public void RunProducesThreeFiles()
        {
            using var fixture = new MultiTableFixture();

            Run(fixture);

            Assert.True(File.Exists(fixture.OutputPath("BagTable.cs")));
            Assert.True(File.Exists(fixture.OutputPath("SkillTable.cs")));
            Assert.True(File.Exists(fixture.OutputPath("MonsterTable.cs")));
        }

        [Fact]
        public void ProductsDifferByClassName()
        {
            using var fixture = new MultiTableFixture();

            Run(fixture);

            var bag = File.ReadAllText(fixture.OutputPath("BagTable.cs"));
            var skill = File.ReadAllText(fixture.OutputPath("SkillTable.cs"));
            var monster = File.ReadAllText(fixture.OutputPath("MonsterTable.cs"));

            Assert.Contains("class BagTable", bag);
            Assert.Contains("class SkillTable", skill);
            Assert.Contains("class MonsterTable", monster);
            Assert.NotEqual(bag, skill);
            Assert.NotEqual(bag, monster);
            Assert.NotEqual(skill, monster);
        }

        [Fact]
        public void RunIsIdempotent()
        {
            using var fixture = new MultiTableFixture();

            Run(fixture);
            var bagPath = fixture.OutputPath("BagTable.cs");
            var firstContent = File.ReadAllText(bagPath);
            var firstWriteTime = File.GetLastWriteTimeUtc(bagPath);

            // 留出足以区分的时间，确保「内容没变就没重写」这一层也成立。
            Thread.Sleep(200);

            Run(fixture);
            var secondContent = File.ReadAllText(bagPath);
            var secondWriteTime = File.GetLastWriteTimeUtc(bagPath);

            Assert.Equal(firstContent, secondContent);
            Assert.Equal(firstWriteTime, secondWriteTime);
        }

        [Fact]
        public void VerifyReportsOnlyEditedProduct()
        {
            using var fixture = new MultiTableFixture();

            Run(fixture);
            File.AppendAllText(fixture.OutputPath("SkillTable.cs"), "// 人为改动\n");

            var problems = CodeGenerator.Verify(fixture.TemplateRoot, fixture.Configuration);

            Assert.Single(problems);
            Assert.Contains("SkillTable.cs", problems[0]);
        }

        [Fact]
        public void MissingSchemaReportsTargetNameAndPath()
        {
            using var fixture = new MultiTableFixture();
            var target = new CodeGenerationTarget
            {
                TargetName = "不存在表访问代码",
                TargetKind = "TableAccess",
                InputPath = "Config/Schema/不存在.schema.json",
                OutputPath = "Generated/Nothing.cs",
            };

            var exception = Assert.Throws<InvalidOperationException>(() => CodeGenerator.Render(fixture.TemplateRoot, target));

            Assert.Contains("不存在表访问代码", exception.Message);
            Assert.Contains("Config/Schema/不存在.schema.json", exception.Message);
        }

        [Fact]
        public void DoublePrimaryKeyGeneratesCompositeKey()
        {
            using var fixture = new MultiTableFixture();
            var target = fixture.Configuration.Targets.Single(item => item.TargetName == "怪物表访问代码");

            var rendered = CodeGenerator.Render(fixture.TemplateRoot, target);

            Assert.Contains("ByCompositeKey", rendered);
            Assert.Contains("Dictionary<string, MonsterRow>", rendered);
            Assert.Contains("row.LevelId + \"|\" + row.MonsterId", rendered);
        }

        private static void Run(MultiTableFixture fixture)
        {
            foreach (var target in fixture.Configuration.Targets)
            {
                CodeGenerator.WriteIfChanged(fixture.TemplateRoot, target);
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

        private sealed class MultiTableFixture : IDisposable
        {
            public MultiTableFixture()
            {
                TemplateRoot = Path.Combine(Path.GetTempPath(), "CodeGenMultiTableTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(TemplateRoot);

                // 复制真实模板，schema 与清单用测试自己摆的，互不依赖仓库现状。
                var templatesDirectory = Path.Combine(TemplateRoot, "Tools", "CodeGen", "Templates");
                Directory.CreateDirectory(templatesDirectory);
                File.Copy(
                    Path.Combine(FindTemplateRoot(), "Tools", "CodeGen", "Templates", "TableAccess.scriban"),
                    Path.Combine(templatesDirectory, "TableAccess.scriban"));

                var schemaDirectory = Path.Combine(TemplateRoot, "Config", "Schema");
                Directory.CreateDirectory(schemaDirectory);
                File.WriteAllText(Path.Combine(schemaDirectory, "Bag.schema.json"), BagSchemaJson);
                File.WriteAllText(Path.Combine(schemaDirectory, "Skill.schema.json"), SkillSchemaJson);
                File.WriteAllText(Path.Combine(schemaDirectory, "Monster.schema.json"), MonsterSchemaJson);

                Configuration = new CodeGenerationConfiguration
                {
                    Targets = new List<CodeGenerationTarget>
                    {
                        new CodeGenerationTarget { TargetName = "背包表访问代码", TargetKind = "TableAccess", InputPath = "Config/Schema/Bag.schema.json", OutputPath = "Generated/BagTable.cs" },
                        new CodeGenerationTarget { TargetName = "技能表访问代码", TargetKind = "TableAccess", InputPath = "Config/Schema/Skill.schema.json", OutputPath = "Generated/SkillTable.cs" },
                        new CodeGenerationTarget { TargetName = "怪物表访问代码", TargetKind = "TableAccess", InputPath = "Config/Schema/Monster.schema.json", OutputPath = "Generated/MonsterTable.cs" },
                    },
                };
            }

            public string TemplateRoot { get; }

            public CodeGenerationConfiguration Configuration { get; }

            public string OutputPath(string fileName)
            {
                return Path.Combine(TemplateRoot, "Generated", fileName);
            }

            public void Dispose()
            {
                if (Directory.Exists(TemplateRoot))
                {
                    Directory.Delete(TemplateRoot, recursive: true);
                }
            }
        }

        private const string BagSchemaJson = @"
{
  ""tableName"": ""背包"",
  ""tableIdentifierName"": ""Bag"",
  ""sheetName"": ""道具"",
  ""fields"": [
    { ""displayName"": ""编号"", ""identifierName"": ""ItemId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""名称"", ""identifierName"": ""ItemName"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";

        private const string SkillSchemaJson = @"
{
  ""tableName"": ""技能"",
  ""tableIdentifierName"": ""Skill"",
  ""sheetName"": ""技能"",
  ""fields"": [
    { ""displayName"": ""编号"", ""identifierName"": ""SkillId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""技能名称"", ""identifierName"": ""SkillName"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";

        private const string MonsterSchemaJson = @"
{
  ""tableName"": ""怪物"",
  ""tableIdentifierName"": ""Monster"",
  ""sheetName"": ""怪物"",
  ""fields"": [
    { ""displayName"": ""关卡编号"", ""identifierName"": ""LevelId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""怪物编号"", ""identifierName"": ""MonsterId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""怪物名称"", ""identifierName"": ""MonsterName"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";
    }
}
