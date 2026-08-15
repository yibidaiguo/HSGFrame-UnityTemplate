using System;
using System.Collections.Generic;
using System.IO;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>流水线执行器与定义读取测试。</summary>
    public class PipelineRunnerTests
    {
        /// <summary>单步骤流水线跑成功，CompletedStepCount 是 1。</summary>
        [Fact]
        public void SingleStepPipelineSucceedsAndCountsOne()
        {
            var pipeline = SingleStep("版本", "dotnet", new[] { "--version" });

            var result = new PipelineRunner(Environment.CurrentDirectory, null).Run(pipeline, false);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.CompletedStepCount);
            Assert.Equal(string.Empty, result.FailedStepName);
        }

        /// <summary>逐行回调真的被调用过（收到的行数大于 0）。</summary>
        [Fact]
        public void LineCallbackIsInvoked()
        {
            var (lines, callback) = CollectLines();
            var pipeline = SingleStep("版本", "dotnet", new[] { "--version" });

            var result = new PipelineRunner(Environment.CurrentDirectory, callback).Run(pipeline, false);

            Assert.True(result.IsSuccess);
            Assert.NotEmpty(lines);
        }

        /// <summary>步骤开始与结束各广播一行，内容里有步骤名。</summary>
        [Fact]
        public void StartAndEndLinesCarryStepName()
        {
            var (lines, callback) = CollectLines();
            var pipeline = SingleStep("版本", "dotnet", new[] { "--version" });

            new PipelineRunner(Environment.CurrentDirectory, callback).Run(pipeline, false);

            Assert.Contains(lines, line => line.Contains("开始") && line.Contains("版本"));
            Assert.Contains(lines, line => line.Contains("结束") && line.Contains("版本"));
        }

        /// <summary>第一步失败时第二步不执行。</summary>
        [Fact]
        public void FirstStepFailureStopsSecondStep()
        {
            var pipeline = new PipelineDefinition
            {
                Name = "测试流水线",
                Steps = new List<PipelineStep>
                {
                    new PipelineStep { Name = "必败", FileName = "dotnet", Arguments = new[] { "--definitely-not-a-real-option" } },
                    new PipelineStep { Name = "成功", FileName = "dotnet", Arguments = new[] { "--version" } }
                }
            };
            var (lines, callback) = CollectLines();

            var result = new PipelineRunner(Environment.CurrentDirectory, callback).Run(pipeline, false);

            Assert.False(result.IsSuccess);
            Assert.Equal("必败", result.FailedStepName);
            Assert.Equal(0, result.CompletedStepCount);
            Assert.DoesNotContain(lines, line => line.Contains("步骤\":\"成功"));
        }

        /// <summary>失败时 FailedStepName 是那一步的名称。</summary>
        [Fact]
        public void FailureSetsFailedStepName()
        {
            var pipeline = SingleStep("必败", "dotnet", new[] { "--definitely-not-a-real-option" });

            var result = new PipelineRunner(Environment.CurrentDirectory, null).Run(pipeline, false);

            Assert.False(result.IsSuccess);
            Assert.Equal("必败", result.FailedStepName);
            Assert.Equal(0, result.CompletedStepCount);
        }

        /// <summary>skipStepsRequiringUnity 为 true 时，需要 Unity 的步骤被跳过且整条流水线仍成功。</summary>
        [Fact]
        public void SkipUnityStepsSkipsAndSucceeds()
        {
            var pipeline = new PipelineDefinition
            {
                Name = "测试流水线",
                Steps = new List<PipelineStep>
                {
                    new PipelineStep { Name = "需要Unity", FileName = "this-program-does-not-exist", Arguments = new string[0], RequiresUnity = true },
                    new PipelineStep { Name = "版本", FileName = "dotnet", Arguments = new[] { "--version" } }
                }
            };
            var (lines, callback) = CollectLines();

            var result = new PipelineRunner(Environment.CurrentDirectory, callback).Run(pipeline, skipStepsRequiringUnity: true);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.CompletedStepCount);
            Assert.Contains(lines, line => line.Contains("已跳过") && line.Contains("需要Unity"));
        }

        /// <summary>skipStepsRequiringUnity 为 false 时，需要 Unity 的步骤会被执行。</summary>
        [Fact]
        public void SkipUnityFalseExecutesUnityStep()
        {
            var pipeline = SingleStep("需要Unity", "this-program-does-not-exist", new string[0], requiresUnity: true);

            var result = new PipelineRunner(Environment.CurrentDirectory, null).Run(pipeline, skipStepsRequiringUnity: false);

            Assert.False(result.IsSuccess);
            Assert.Equal("需要Unity", result.FailedStepName);
        }

        /// <summary>空步骤清单 → 成功且 CompletedStepCount 是 0。</summary>
        [Fact]
        public void EmptyStepsSucceedsWithZero()
        {
            var pipeline = new PipelineDefinition { Name = "空流水线", Steps = new List<PipelineStep>() };

            var result = new PipelineRunner(Environment.CurrentDirectory, null).Run(pipeline, false);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.CompletedStepCount);
        }

        /// <summary>程序名不存在时返回失败而不是抛异常穿出去。</summary>
        [Fact]
        public void MissingProgramReturnsFailure()
        {
            var pipeline = SingleStep("不存在", "this-program-does-not-exist", new string[0]);
            var runner = new PipelineRunner(Environment.CurrentDirectory, null);

            PipelineRunResult result = null;
            var exception = Record.Exception(() => result = runner.Run(pipeline, false));

            Assert.Null(exception);
            Assert.False(result.IsSuccess);
            Assert.Equal("不存在", result.FailedStepName);
        }

        /// <summary>回调为 null 时不抛异常（内部当成空操作）。</summary>
        [Fact]
        public void NullCallbackDoesNotThrow()
        {
            var pipeline = SingleStep("版本", "dotnet", new[] { "--version" });

            var exception = Record.Exception(() => new PipelineRunner(Environment.CurrentDirectory, null).Run(pipeline, false));

            Assert.Null(exception);
        }

        /// <summary>LoadFromFile 读仓库里那份真定义文件，四条流水线都在。</summary>
        [Fact]
        public void CatalogLoadsRealDefinitionWithFourPipelines()
        {
            var definitionPath = Path.Combine(FindTemplateRoot(), "Pipelines", "流水线定义.json");

            var catalog = PipelineCatalog.LoadFromFile(definitionPath);

            Assert.NotNull(catalog.Pipelines);
            Assert.Equal(4, catalog.Pipelines.Count);
        }

        /// <summary>Find 对不存在的名字返回 null。</summary>
        [Fact]
        public void FindReturnsNullForUnknownName()
        {
            var definitionPath = Path.Combine(FindTemplateRoot(), "Pipelines", "流水线定义.json");
            var catalog = PipelineCatalog.LoadFromFile(definitionPath);

            Assert.Null(catalog.Find("不存在的流水线"));
            Assert.NotNull(catalog.Find("秒级门禁"));
        }

        private static PipelineDefinition SingleStep(string name, string fileName, IReadOnlyList<string> arguments, bool requiresUnity = false)
        {
            return new PipelineDefinition
            {
                Name = "测试流水线",
                Steps = new List<PipelineStep>
                {
                    new PipelineStep { Name = name, FileName = fileName, Arguments = arguments, RequiresUnity = requiresUnity }
                }
            };
        }

        private static (List<string> Lines, Action<string> Callback) CollectLines()
        {
            var lines = new List<string>();
            var gate = new object();
            return (lines, line => { lock (gate) { lines.Add(line); } });
        }

        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Pipelines", "流水线定义.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到模板根：Pipelines/流水线定义.json 不存在");
        }
    }
}
