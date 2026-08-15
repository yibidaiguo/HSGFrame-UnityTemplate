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
        private const string NewPrefix = "com.example.";
        private const string TemplatePrefix = "com.gametemplateforagent.";

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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.True(result.IsSuccess, result.Message);
                Assert.Equal(9, result.CreatedFileCount);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>com.gametemplateforagent.demo 目录在目标里变成新前缀加 demo。</summary>
        [Fact]
        public void CreateRenamesComHsghostDirectoryToPackagePrefix()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.True(result.IsSuccess, result.Message);
                Assert.True(Directory.Exists(Path.Combine(result.TargetPath, "com.example.demo")));
                Assert.False(Directory.Exists(Path.Combine(result.TargetPath, "com.gametemplateforagent.demo")));
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>文本文件里的 com.gametemplateforagent. 全部换成新前缀。</summary>
        [Fact]
        public void CreateRewritesPackagePrefixInsideTextFiles()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                var infoPath = Path.Combine(result.TargetPath, "com.example.demo", "info.json");
                var content = File.ReadAllText(infoPath);

                Assert.Contains("com.example.", content);
                Assert.DoesNotContain(TemplatePrefix, content);
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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.False(File.Exists(Path.Combine(result.TargetPath, "com.example.demo", "bin", "build.dll")));
                Assert.False(File.Exists(Path.Combine(result.TargetPath, "obj", "temp.o")));
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>CLAUDE.md 追加了模板生成说明，占位符替换成项目名与包前缀。</summary>
        [Fact]
        public void CreateAppendsTemplateNoticeToClaudeFile()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                var claudePath = Path.Combine(result.TargetPath, "CLAUDE.md");
                var content = File.ReadAllText(claudePath);

                Assert.Contains("本项目由通用 Unity 模板生成", content);
                Assert.Contains("项目名：" + ProjectName, content);
                Assert.Contains("UPM 包前缀：" + NewPrefix, content);
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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

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

                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

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
                var result = RunGenerator(templateRoot, targetDirectory, "My Project", NewPrefix);

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
                var result = RunGenerator(templateRoot, targetDirectory, "我的项目", NewPrefix);

                Assert.False(result.IsSuccess);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>包前缀不以点结尾时返回失败。</summary>
        [Fact]
        public void CreateFailsWhenPackagePrefixHasNoTrailingDot()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, "com.example");

                Assert.False(result.IsSuccess);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }


        /// <summary>模板自身的标识名在生成时换成新项目名，新项目不再顶着模板的名字。</summary>
        [Fact]
        public void CreateReplacesTemplateIdentifierNameWithProjectName()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(templateRoot, "标识名样本.cs"),
                    "namespace GameTemplateForAgent.Save { }");

                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.True(result.IsSuccess, result.Message);

                var generated = File.ReadAllText(Path.Combine(targetDirectory, ProjectName, "标识名样本.cs"));
                Assert.Contains("namespace " + ProjectName + ".Save", generated);
                Assert.DoesNotContain("GameTemplateForAgent", generated);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>根命名空间 Template. 换成新项目名，Scriban 的 Template.Parse 不被误伤。</summary>
        [Fact]
        public void CreateReplacesRootNamespaceButKeepsScribanTemplateApi()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.True(result.IsSuccess, result.Message);
                var content = File.ReadAllText(Path.Combine(result.TargetPath, "命名空间样本.cs"));

                Assert.Contains("namespace " + ProjectName + ".Toolkit.Demo", content);
                Assert.Contains("Scriban.Template.Parse", content);
                Assert.DoesNotContain("Template.Toolkit", content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>Template.sln 连同引用它的文本一起改成新项目名。</summary>
        [Fact]
        public void CreateRenamesSolutionFileToProjectName()
        {
            var templateRoot = CreateTemplateTree();
            var targetDirectory = CreateTargetDirectory();
            try
            {
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                Assert.True(File.Exists(Path.Combine(result.TargetPath, "Solutions", ProjectName + ".sln")));
                Assert.False(File.Exists(Path.Combine(result.TargetPath, "Solutions", "Template.sln")));

                var content = File.ReadAllText(Path.Combine(result.TargetPath, "命名空间样本.cs"));
                Assert.Contains(ProjectName + ".sln", content);
                Assert.DoesNotContain("Template.sln", content);
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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

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
                var result = RunGenerator(templateRoot, targetDirectory, ProjectName, NewPrefix);

                var content = File.ReadAllText(Path.Combine(result.TargetPath, "Pipelines", "Jenkinsfile.秒级门禁"));

                Assert.Contains("dotnet test Solutions/" + ProjectName + ".sln", content);
                Assert.Contains(ProjectName + ".Toolkit.Editor.CompileCheckEntry.Run", content);
                Assert.DoesNotContain("Template.sln", content);
            }
            finally
            {
                Directory.Delete(templateRoot, true);
                Directory.Delete(targetDirectory, true);
            }
        }

        private static ProjectCreationResult RunGenerator(string templateRoot, string targetDirectory, string projectName, string packagePrefix)
        {
            return ProjectGenerator.Create(new ProjectCreationOptions
            {
                TemplateRoot = templateRoot,
                TargetDirectory = targetDirectory,
                ProjectName = projectName,
                PackagePrefix = packagePrefix
            });
        }

        private static string CreateTemplateTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "ScaffoldTemplate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            File.WriteAllText(Path.Combine(root, "README.md"), "前缀 " + TemplatePrefix + " 的说明");
            File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# 原有路标内容\n");

            var demoDirectory = Path.Combine(root, "com.gametemplateforagent.demo");
            Directory.CreateDirectory(demoDirectory);
            File.WriteAllText(Path.Combine(demoDirectory, "info.json"), "{ \"name\": \"com.gametemplateforagent.demo\" }");

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
                "## 本项目由通用 Unity 模板生成\n\n- 项目名：{{项目名}}\n- UPM 包前缀：{{包前缀}}\n");

            var pipelinesDirectory = Path.Combine(root, "Pipelines");
            Directory.CreateDirectory(pipelinesDirectory);
            File.WriteAllText(Path.Combine(pipelinesDirectory, "Jenkinsfile.秒级门禁"),
                "bat 'dotnet test Solutions/Template.sln --nologo'\n"
                + "bat 'unity-cmd.ps1 -ExecuteMethod Template.Toolkit.Editor.CompileCheckEntry.Run'\n");

            File.WriteAllText(
                Path.Combine(root, "命名空间样本.cs"),
                "using Scriban;\nnamespace Template.Toolkit.Demo\n{\n"
                + "    // 解决方案是 Template.sln\n"
                + "    public static class Sample { public static void Run() { Scriban.Template.Parse(\"x\"); } }\n"
                + "}\n");

            var solutionsDirectory = Path.Combine(root, "Solutions");
            Directory.CreateDirectory(solutionsDirectory);
            File.WriteAllText(Path.Combine(solutionsDirectory, "Template.sln"), "Project \"Template.Toolkit\"\n");

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
