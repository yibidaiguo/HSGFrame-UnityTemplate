using System;
using System.IO;
using Template.Toolkit.Scaffold;
using Xunit;

namespace Template.Toolkit.ScaffoldTests
{
    /// <summary>模板生成器测试：用一棵临时小模板树验证复制、改名与改写。</summary>
    public class ProjectGeneratorTests
    {
        private const string ProjectName = "NewGame";

        // HSGFrame 是框架自己的名字，与 Unity.Mathematics 地位相同，不跟宿主项目改名。
        // 生成器因此不再有「包前缀」这个参数，这个常量只是临时树里的样本值。
        private const string FrameworkPrefix = "com.hsgframe.";

        // 下面这些是**测试数据**——它们代表「模板的根命名空间」这个被测概念，
        // 不是本文件自己的命名空间。一律从生成器的公开常量取，不写死字面量：
        // 写死的话，用本模板生成新项目时生成器会把这些期望值连同真命名空间一起替换掉，
        // 于是新项目里这几条测试自我矛盾（喂进去的样本已是新名字，断言却还要求它变成新名字）。
        private const string TemplateRootToken = ProjectGenerator.TemplateRootNamespace;
        private static readonly string TemplateNamespaceSample = TemplateRootToken + ".Toolkit";
        private static readonly string TemplateSolutionName = ProjectGenerator.TemplateSolutionFileName;

        private const string GateConfigJson = @"{
  ""changedPathWhitelist"": [
    ""Template/"",
    ""Doc/README.md""
  ]
}";

        /// <summary>生成成功后目标目录文件数与源一致（跳过项除外）。</summary>
        [Fact]
        public void CreateCopiesTemplateTreeWithExpectedFileCount()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(result.IsSuccess, result.Message);
                Assert.Equal(9, result.CreatedFileCount);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>bin / obj 目录里的文件没有被复制过去。</summary>
        [Fact]
        public void CreateSkipsBinAndObjDirectories()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.False(File.Exists(Path.Combine(result.TargetPath, FrameworkPrefix + "demo", "bin", "build.dll")));
                Assert.False(File.Exists(Path.Combine(result.TargetPath, "obj", "temp.o")));
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>CLAUDE.md 追加了模板生成说明，占位符替换成项目名。</summary>
        [Fact]
        public void CreateAppendsTemplateNoticeToClaudeFile()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                var claudePath = Path.Combine(result.TargetPath, "CLAUDE.md");
                var content = File.ReadAllText(claudePath);

                Assert.Contains("本项目由通用 Unity 模板生成", content);
                Assert.Contains("项目名：" + ProjectName, content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>gate-config.json 的 changedPathWhitelist 第一项改成项目名。</summary>
        [Fact]
        public void CreateRewritesGateWhitelistFirstEntry()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                var configPath = Path.Combine(result.TargetPath, "Tools", "Gates", "Config", "gate-config.json");
                var content = File.ReadAllText(configPath);

                Assert.Contains("\"" + ProjectName + "/\"", content);
                Assert.DoesNotContain("\"Template/\"", content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>目标目录已存在且非空时返回失败，消息含「已有内容」。</summary>
        [Fact]
        public void CreateFailsWhenTargetDirectoryAlreadyHasContent()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var existingProject = Path.Combine(targetDirectory, ProjectName);
                Directory.CreateDirectory(existingProject);
                File.WriteAllText(Path.Combine(existingProject, "occupied.txt"), "occupied");

                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.False(result.IsSuccess);
                Assert.Contains("已有内容", result.Message);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>项目名含空格时返回失败。</summary>
        [Fact]
        public void CreateFailsWhenProjectNameContainsSpace()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, "My Project");

                Assert.False(result.IsSuccess);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>项目名含中文时返回失败。</summary>
        [Fact]
        public void CreateFailsWhenProjectNameContainsChinese()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, "我的项目");

                Assert.False(result.IsSuccess);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>根命名空间换成新项目名，Scriban 那个同名 API 不被误伤。</summary>
        [Fact]
        public void CreateReplacesRootNamespaceButKeepsScribanTemplateApi()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(result.IsSuccess, result.Message);
                var content = File.ReadAllText(Path.Combine(result.TargetPath, "命名空间样本.cs"));

                Assert.Contains("namespace " + ProjectName + ".Toolkit.Demo", content);
                Assert.Contains("Scriban." + TemplateRootToken + ".Parse", content);
                Assert.DoesNotContain(TemplateNamespaceSample, content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>解决方案文件连同引用它的文本一起改成新项目名。</summary>
        [Fact]
        public void CreateRenamesSolutionFileToProjectName()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(File.Exists(Path.Combine(result.TargetPath, "Solutions", ProjectName + ".sln")));
                Assert.False(File.Exists(Path.Combine(result.TargetPath, "Solutions", TemplateSolutionName)));

                var content = File.ReadAllText(Path.Combine(result.TargetPath, "命名空间样本.cs"));
                Assert.Contains(ProjectName + ".sln", content);
                Assert.DoesNotContain(TemplateSolutionName, content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>新项目的宿主门禁配置整份重写：白名单换成项目名，另外两项清空。</summary>
        [Fact]
        public void CreateWritesCleanHostGateConfiguration()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                var hostPath = Path.Combine(result.TargetPath, "Tools", "Gates", "Config", "gate-config.host.json");
                var content = File.ReadAllText(hostPath);

                Assert.Contains("\"" + ProjectName + "/\"", content);
                Assert.Contains("\"editorOwnedPathPrefixes\": []", content);
                Assert.Contains("\"genericNameBlacklist\": []", content);
                Assert.DoesNotContain("RPG_Unity", content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>新项目自己的名字不在自己的通用性黑名单里，否则第八道门禁必红。</summary>
        [Fact]
        public void CreateDoesNotPutProjectNameIntoItsOwnBlacklist()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                var hostPath = Path.Combine(result.TargetPath, "Tools", "Gates", "Config", "gate-config.host.json");
                var content = File.ReadAllText(hostPath);

                Assert.DoesNotContain(ProjectName + "\"", content.Substring(content.IndexOf("genericNameBlacklist", StringComparison.Ordinal)));
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>扩展名认不出来的 Jenkinsfile 也按文本改写，里面的解决方案名跟着换。</summary>
        [Fact]
        public void CreateRewritesJenkinsfileWithoutRecognizableExtension()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                var content = File.ReadAllText(Path.Combine(result.TargetPath, "Pipelines", "Jenkinsfile.秒级门禁"));

                Assert.Contains("dotnet test Solutions/" + ProjectName + ".sln", content);
                Assert.Contains(ProjectName + ".Toolkit.Editor.CompileCheckEntry.Run", content);
                Assert.DoesNotContain(TemplateSolutionName, content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>框架包的目录名与包名原样带进新项目，不跟着项目名改。</summary>
        [Fact]
        public void CreateKeepsFrameworkPackagePrefixUnchanged()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(Directory.Exists(Path.Combine(result.TargetPath, FrameworkPrefix + "demo")));

                var content = File.ReadAllText(Path.Combine(result.TargetPath, FrameworkPrefix + "demo", "info.json"));
                Assert.Contains(FrameworkPrefix + "demo", content);
                Assert.DoesNotContain(ProjectName, content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>新项目自带试验区：_Scratch/说明.md 从模板里那份说明原样落地。</summary>
        [Fact]
        public void CreateWritesScratchAreaNotice()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                const string noticeText = "# _Scratch · 模型试验区\n\n只进不出。\n";
                File.WriteAllText(
                    Path.Combine(templateRoot, "Tools", "Scaffold", "Templates",
                        ProjectGenerator.ScratchNoticeTemplateName),
                    noticeText);

                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(result.IsSuccess, result.Message);
                var noticePath = Path.Combine(
                    result.TargetPath, ProjectGenerator.ScratchDirectoryName, "说明.md");
                Assert.True(File.Exists(noticePath), "新项目里没有铺出试验区说明");
                Assert.Equal(noticeText, File.ReadAllText(noticePath));
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>模板里没有那份说明时不建空的试验区目录——空夹留不住，建了也是噪音。</summary>
        [Fact]
        public void CreateSkipsScratchAreaWhenTemplateNoticeMissing()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(result.IsSuccess, result.Message);
                Assert.False(
                    Directory.Exists(Path.Combine(result.TargetPath, ProjectGenerator.ScratchDirectoryName)),
                    "模板里没有说明文件却还是建了试验区目录");
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>Agent 入口镜像在追加模板说明之后重出，内容与新的 CLAUDE.md 逐字对得上。</summary>
        [Fact]
        public void CreateRegeneratesAgentEntryMirror()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var agentSyncDirectory = Path.Combine(templateRoot, "Tools", "AgentSync");
                Directory.CreateDirectory(agentSyncDirectory);
                File.WriteAllText(Path.Combine(agentSyncDirectory, "agent-sync.ps1"),
                    "param([switch]$Verify)\n$mirrorNames = @('AGENTS.md')\n"
                    + "$mirrorHeader = \"<!-- 镜像文件 -->\"\n");
                File.WriteAllText(Path.Combine(templateRoot, "AGENTS.md"), "过期的镜像内容\n");

                var result = RunGenerator(templateRoot, targetDirectory, ProjectName);

                Assert.True(result.IsSuccess, result.Message);
                var claudeText = File.ReadAllText(Path.Combine(result.TargetPath, "CLAUDE.md"));
                var mirrorText = File.ReadAllText(Path.Combine(result.TargetPath, "AGENTS.md"));
                Assert.Equal("<!-- 镜像文件 -->\n\n" + claudeText, mirrorText);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        private static ProjectCreationResult RunGenerator(string templateRoot, string targetDirectory, string projectName)
        {
            return ProjectGenerator.Create(new ProjectCreationOptions
            {
                TemplateRoot = templateRoot,
                TargetDirectory = targetDirectory,
                ProjectName = projectName
            });
        }

        private static string CreateTemplateTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "ScaffoldTemplate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            File.WriteAllText(Path.Combine(root, "README.md"), "框架包前缀 " + FrameworkPrefix + " 的说明");
            File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# 原有路标内容\n");

            var demoDirectory = Path.Combine(root, FrameworkPrefix + "demo");
            Directory.CreateDirectory(demoDirectory);
            File.WriteAllText(Path.Combine(demoDirectory, "info.json"), "{ \"name\": \"" + FrameworkPrefix + "demo\" }");

            var binDirectory = Path.Combine(demoDirectory, "bin");
            Directory.CreateDirectory(binDirectory);
            File.WriteAllText(Path.Combine(binDirectory, "build.dll"), "fake binary");

            var objDirectory = Path.Combine(root, "obj");
            Directory.CreateDirectory(objDirectory);
            File.WriteAllText(Path.Combine(objDirectory, "temp.o"), "fake object");

            var gatesConfigDirectory = Path.Combine(root, "Tools", "Gates", "Config");
            Directory.CreateDirectory(gatesConfigDirectory);
            File.WriteAllText(Path.Combine(gatesConfigDirectory, "gate-config.json"), GateConfigJson);

            var scaffoldTemplatesDirectory = Path.Combine(root, "Tools", "Scaffold", "Templates");
            Directory.CreateDirectory(scaffoldTemplatesDirectory);
            File.WriteAllText(Path.Combine(scaffoldTemplatesDirectory, "新项目说明.md"),
                "## 本项目由通用 Unity 模板生成\n\n- 项目名：{{项目名}}\n");

            var pipelinesDirectory = Path.Combine(root, "Pipelines");
            Directory.CreateDirectory(pipelinesDirectory);
            File.WriteAllText(Path.Combine(pipelinesDirectory, "Jenkinsfile.秒级门禁"),
                "bat 'dotnet test Solutions/" + TemplateSolutionName + " --nologo'\n"
                + "bat 'unity-cmd.ps1 -ExecuteMethod " + TemplateNamespaceSample + ".Editor.CompileCheckEntry.Run'\n");

            File.WriteAllText(
                Path.Combine(root, "命名空间样本.cs"),
                "using Scriban;\nnamespace " + TemplateNamespaceSample + ".Demo\n{\n"
                + "    // 解决方案是 " + TemplateSolutionName + "\n"
                + "    public static class Sample { public static void Run() { Scriban."
                + TemplateRootToken + ".Parse(\"x\"); } }\n"
                + "}\n");

            var solutionsDirectory = Path.Combine(root, "Solutions");
            Directory.CreateDirectory(solutionsDirectory);
            File.WriteAllText(Path.Combine(solutionsDirectory, TemplateSolutionName),
                "Project \"" + TemplateNamespaceSample + "\"\n");

            File.WriteAllText(
                Path.Combine(gatesConfigDirectory, "gate-config.host.json"),
                "{\n  \"changedPathWhitelist\": [ \"RebuiltRPG/\" ],\n"
                + "  \"editorOwnedPathPrefixes\": [ \"RPG_Unity/\" ],\n"
                + "  \"genericNameBlacklist\": [ \"RPG\", \"RebuiltRPG\" ]\n}\n");

            return root;
        }

        private static string CreateTargetDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "ScaffoldTarget_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
