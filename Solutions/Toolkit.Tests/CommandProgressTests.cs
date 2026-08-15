using System;
using System.IO;
using Template.Toolkit.CommandFramework;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>命令断点续跑的边界测试。</summary>
    public class CommandProgressTests
    {
        [Fact]
        public void FreshRunExecutesAllSteps()
        {
            var root = NewTempRoot();
            try
            {
                var progress = CommandProgress.Load(root, "test.command", "{\"A\":1}", resume: false);

                Assert.True(progress.RunStep("FirstStep", () => { }));
                Assert.True(progress.RunStep("SecondStep", () => { }));
                Assert.True(progress.RunStep("ThirdStep", () => { }));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void CompleteRemovesCheckpointFile()
        {
            var root = NewTempRoot();
            try
            {
                var progress = CommandProgress.Load(root, "test.command", "{\"A\":1}", resume: false);
                progress.RunStep("FirstStep", () => { });
                progress.Complete();

                Assert.False(File.Exists(progress.ProgressFilePath));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void SecondStepExceptionPropagatesAndKeepsOnlyFirstStep()
        {
            var root = NewTempRoot();
            try
            {
                var arguments = "{\"A\":1}";
                var progress = CommandProgress.Load(root, "test.command", arguments, resume: false);

                Assert.True(progress.RunStep("FirstStep", () => { }));
                Assert.Throws<InvalidOperationException>(() =>
                {
                    progress.RunStep("SecondStep", () => throw new InvalidOperationException("boom"));
                });

                Assert.True(File.Exists(progress.ProgressFilePath));

                var reloaded = CommandProgress.Load(root, "test.command", arguments, resume: true);
                Assert.Contains("FirstStep", reloaded.CompletedStepNames);
                Assert.DoesNotContain("SecondStep", reloaded.CompletedStepNames);
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void ResumeTrueSkipsCompletedStep()
        {
            var root = NewTempRoot();
            try
            {
                var arguments = "{\"A\":1}";
                var first = CommandProgress.Load(root, "test.command", arguments, resume: false);
                first.RunStep("FirstStep", () => { });

                var resumed = CommandProgress.Load(root, "test.command", arguments, resume: true);
                Assert.False(resumed.RunStep("FirstStep", () => throw new InvalidOperationException("不应执行")));
                Assert.True(resumed.RunStep("SecondStep", () => { }));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void ResumeFalseRunsFromBeginning()
        {
            var root = NewTempRoot();
            try
            {
                var arguments = "{\"A\":1}";
                var first = CommandProgress.Load(root, "test.command", arguments, resume: false);
                first.RunStep("FirstStep", () => { });

                var fresh = CommandProgress.Load(root, "test.command", arguments, resume: false);
                Assert.True(fresh.RunStep("FirstStep", () => { }));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void ChangedArgumentsInvalidateCheckpointOnResume()
        {
            var root = NewTempRoot();
            try
            {
                var first = CommandProgress.Load(root, "test.command", "{\"A\":1}", resume: false);
                first.RunStep("FirstStep", () => { });

                var changed = CommandProgress.Load(root, "test.command", "{\"A\":2}", resume: true);
                Assert.Empty(changed.CompletedStepNames);
                Assert.True(changed.RunStep("FirstStep", () => { }));
                Assert.True(File.Exists(changed.ProgressFilePath));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void ComputeInputHashIgnoresJsonWhitespace()
        {
            var compact = CommandProgress.ComputeInputHash("{\"A\":1,\"B\":\"x\"}");
            var indented = CommandProgress.ComputeInputHash("{\n  \"A\": 1,\n  \"B\": \"x\"\n}");

            Assert.Equal(compact, indented);
        }

        [Fact]
        public void ComputeInputHashTreatsNullAndEmptyIdentically()
        {
            var nullHash = CommandProgress.ComputeInputHash(null);
            var emptyHash = CommandProgress.ComputeInputHash(string.Empty);

            Assert.Equal(nullHash, emptyHash);
            Assert.Equal(16, nullHash.Length);
        }

        /// <summary>Resume 自身不算输入：算进哈希的话续跑那一次永远匹配不上断点。</summary>
        [Fact]
        public void ComputeInputHashExcludesResumeFlag()
        {
            var withoutResume = CommandProgress.ComputeInputHash("{\"TemplateRoot\":\"D:/x\"}");
            var resumeFalse = CommandProgress.ComputeInputHash("{\"TemplateRoot\":\"D:/x\",\"Resume\":false}");
            var resumeTrue = CommandProgress.ComputeInputHash("{\"TemplateRoot\":\"D:/x\",\"Resume\":true}");

            Assert.Equal(withoutResume, resumeFalse);
            Assert.Equal(withoutResume, resumeTrue);
        }

        /// <summary>参数换个书写顺序应当落在同一个断点上。</summary>
        [Fact]
        public void ComputeInputHashIgnoresPropertyOrder()
        {
            var first = CommandProgress.ComputeInputHash("{\"A\":1,\"B\":\"x\"}");
            var second = CommandProgress.ComputeInputHash("{\"B\":\"x\",\"A\":1}");

            Assert.Equal(first, second);
        }

        /// <summary>真正的输入变了，断点仍然要失效。</summary>
        [Fact]
        public void ComputeInputHashChangesWhenRealArgumentChanges()
        {
            var first = CommandProgress.ComputeInputHash("{\"TemplateRoot\":\"D:/x\",\"Resume\":true}");
            var second = CommandProgress.ComputeInputHash("{\"TemplateRoot\":\"D:/y\",\"Resume\":true}");

            Assert.NotEqual(first, second);
        }

        /// <summary>端到端语义：先失败留断点，再带 Resume 续跑时第一步应当跳过。</summary>
        [Fact]
        public void ResumeSkipsCompletedStepEvenWhenResumeFlagIsInArgumentsJson()
        {
            var root = NewTempRoot();
            try
            {
                var firstPassJson = "{\"TemplateRoot\":\"D:/x\",\"Resume\":false}";
                var firstPass = CommandProgress.Load(root, "test.command", firstPassJson, resume: false);
                firstPass.RunStep("FirstStep", () => { });
                Assert.Throws<InvalidOperationException>(() =>
                {
                    firstPass.RunStep("SecondStep", () => { throw new InvalidOperationException("中途挂了"); });
                });

                var secondPassJson = "{\"TemplateRoot\":\"D:/x\",\"Resume\":true}";
                var secondPass = CommandProgress.Load(root, "test.command", secondPassJson, resume: true);

                Assert.False(secondPass.RunStep("FirstStep", () => { }));
                Assert.True(secondPass.RunStep("SecondStep", () => { }));
            }
            finally
            {
                DeleteIfExists(root);
            }
        }

        [Fact]
        public void LoadCreatesMissingProgressRootDirectory()
        {
            var baseRoot = NewTempRoot();
            var root = Path.Combine(baseRoot, "nested", "deeper");
            try
            {
                var progress = CommandProgress.Load(root, "test.command", "{}", resume: false);

                Assert.NotNull(progress);
                Assert.True(Directory.Exists(root));
            }
            finally
            {
                DeleteIfExists(baseRoot);
            }
        }

        private static string NewTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "toolkit-progress-tests", Guid.NewGuid().ToString("N"));
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
