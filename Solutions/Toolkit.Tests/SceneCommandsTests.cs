using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>关卡场景命令测试：全部走前置检查，在真正启动 Unity 之前就返回。</summary>
    public class SceneCommandsTests
    {
        [Fact]
        public void SceneBuildWithEmptyLevelNameFailsWithFourElements()
        {
            var result = SceneBuildCommand.Execute(new SceneBuildArguments { LevelName = null });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void SceneBuildWithWhitespaceLevelNameFails()
        {
            var result = SceneBuildCommand.Execute(new SceneBuildArguments { LevelName = "   " });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void SceneBuildWithUnknownLevelNameReportsNameAndManifest()
        {
            var templateRoot = CreateTempDirectory();
            try
            {
                var result = SceneBuildCommand.Execute(new SceneBuildArguments
                {
                    TemplateRoot = templateRoot,
                    LevelName = "不存在关卡",
                });

                AssertFailureWithFourElements(result);
                Assert.Contains("不存在关卡", result.Message);
                Assert.Contains("level.json", result.Message);
            }
            finally
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }

        [Fact]
        public void SceneBuildWithZeroTimeoutFails()
        {
            var templateRoot = CreateTempDirectory();
            try
            {
                CreateVillageLevel(templateRoot);

                var result = SceneBuildCommand.Execute(new SceneBuildArguments
                {
                    TemplateRoot = templateRoot,
                    LevelName = "测试关卡",
                    TimeoutMinutes = 0,
                });

                AssertFailureWithFourElements(result);
            }
            finally
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }

        [Fact]
        public void SceneBuildWithNegativeTimeoutFails()
        {
            var templateRoot = CreateTempDirectory();
            try
            {
                CreateVillageLevel(templateRoot);

                var result = SceneBuildCommand.Execute(new SceneBuildArguments
                {
                    TemplateRoot = templateRoot,
                    LevelName = "测试关卡",
                    TimeoutMinutes = -1,
                });

                AssertFailureWithFourElements(result);
            }
            finally
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }

        [Fact]
        public void SceneExportWithEmptyScenePathFails()
        {
            var result = SceneExportCommand.Execute(new SceneExportArguments
            {
                ScenePath = null,
                OutputDirectory = "Levels/Village",
            });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void SceneExportWithEmptyOutputDirectoryFails()
        {
            var result = SceneExportCommand.Execute(new SceneExportArguments
            {
                ScenePath = "Assets/Game/Scenes/World/村庄.unity",
                OutputDirectory = null,
            });

            AssertFailureWithFourElements(result);
        }

        [Fact]
        public void SceneExportWithMissingSceneFileReportsPath()
        {
            var templateRoot = CreateTempDirectory();
            try
            {
                var result = SceneExportCommand.Execute(new SceneExportArguments
                {
                    TemplateRoot = templateRoot,
                    ScenePath = "Assets/Game/Scenes/World/不存在.unity",
                    OutputDirectory = "Levels/Village",
                });

                AssertFailureWithFourElements(result);
                Assert.Contains("不存在.unity", result.Message);
            }
            finally
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }

        [Fact]
        public void SceneExportWithZeroTimeoutFails()
        {
            var templateRoot = CreateTempDirectory();
            try
            {
                CreateSceneFile(templateRoot, "Assets/Game/Scenes/World/测试.unity");

                var result = SceneExportCommand.Execute(new SceneExportArguments
                {
                    TemplateRoot = templateRoot,
                    ScenePath = "Assets/Game/Scenes/World/测试.unity",
                    OutputDirectory = "Levels/Village",
                    TimeoutMinutes = 0,
                });

                AssertFailureWithFourElements(result);
            }
            finally
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }

        [Fact]
        public void EverySceneArgumentPropertyCarriesSummary()
        {
            AssertAllPropertiesHaveSummary(typeof(SceneBuildArguments));
            AssertAllPropertiesHaveSummary(typeof(SceneExportArguments));
        }

        private static void CreateVillageLevel(string templateRoot)
        {
            var levelDirectory = Path.Combine(templateRoot, "Levels", "测试关卡");
            Directory.CreateDirectory(levelDirectory);
            File.WriteAllText(
                Path.Combine(levelDirectory, "level.json"),
                "{\"关卡名\":\"测试关卡\",\"环境\":\"白天\",\"区块清单\":[]}");
        }

        // 下面两条走的是命令框架推出来的 schema，而不是直接调 Execute。
        // 直接调 Execute 的那十条测试全绿时，scene.build 仍然一次都跑不起来：
        // ScenePath 说好可以留空，却因为缺 [DefaultValue] 被框架判成必填，参数校验先一步拦下了。
        [Fact]
        public void SceneBuildScenePathIsOptionalInSchema()
        {
            var command = CommandRegistry
                .ScanAssemblies(typeof(SceneBuildCommand).Assembly)
                .Single(candidate => candidate.CommandName == "scene.build");

            var scenePath = command.ParameterSchemas.Single(parameter => parameter.ParameterName == "ScenePath");

            Assert.False(scenePath.IsRequired);
        }

        [Fact]
        public void SceneBuildOnlyRequiresLevelName()
        {
            var command = CommandRegistry
                .ScanAssemblies(typeof(SceneBuildCommand).Assembly)
                .Single(candidate => candidate.CommandName == "scene.build");

            var required = command.ParameterSchemas
                .Where(parameter => parameter.IsRequired)
                .Select(parameter => parameter.ParameterName)
                .ToList();

            Assert.Equal(new[] { "LevelName" }, required);
        }

        private static void CreateSceneFile(string templateRoot, string relativePath)
        {
            var scenePath = Path.Combine(templateRoot, "UnityProject", relativePath);
            var directory = Path.GetDirectoryName(scenePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(scenePath, "{}");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "SceneCommandsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void AssertFailureWithFourElements(CommandResult result)
        {
            Assert.False(result.IsSuccess);
            Assert.Contains("位置", result.Message);
            Assert.Contains("原因", result.Message);
            Assert.Contains("修复", result.Message);
            Assert.Contains("参考", result.Message);
        }

        private static void AssertAllPropertiesHaveSummary(Type argumentsType)
        {
            foreach (var property in argumentsType.GetProperties())
            {
                Assert.NotNull(property.GetCustomAttribute<SummaryAttribute>());
            }
        }
    }
}
