using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.Scaffold;
using Xunit;

namespace Template.Toolkit.Scaffold.Tests
{
    /// <summary>
    /// 摘除可选功能的测试。全部在临时目录造的假树上跑——在真仓库上跑一次就把热更真删了。
    /// </summary>
    public class FeatureRemoverTests
    {
        private const string BeginMarker = "<!-- feature:hotfix 开始 -->";
        private const string EndMarker = "<!-- feature:hotfix 结束 -->";

        /// <summary>目录与文件（连同它的 .meta）都该被删掉。</summary>
        [Fact]
        public void DirectoriesAndFilesAreDeleted()
        {
            var root = CreateTree();

            var result = FeatureRemover.Remove(root, "hotfix");

            Assert.True(result.IsSuccess, result.Message);
            Assert.False(Directory.Exists(Path.Combine(root, "Packages", "com.hsgframe.hotfix")));
            Assert.False(Directory.Exists(Path.Combine(root, "Tools", "Hotfix")));
            Assert.False(Directory.Exists(Path.Combine(root, "Solutions", "Hotfix.Tests")));
            Assert.False(File.Exists(Path.Combine(root, "UnityProject", "ProjectSettings", "HybridCLRSettings.asset")));
            Assert.False(File.Exists(Path.Combine(root, "UnityProject", "ProjectSettings", "HybridCLRSettings.asset.meta")));
        }

        /// <summary>进仓库的生成物目录连同它的目录 .meta 一起删；本地那 800 MB 不归这条命令管。</summary>
        [Fact]
        public void TrackedGeneratedDirectoryIsDeletedWithItsFolderMeta()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            Assert.False(Directory.Exists(Path.Combine(root, "UnityProject", "Assets", "HybridCLRGenerate")));
            Assert.False(File.Exists(Path.Combine(root, "UnityProject", "Assets", "HybridCLRGenerate.meta")));
        }

        /// <summary>只为这个功能存在的中间目录空掉之后一并收走。</summary>
        [Fact]
        public void EmptyParentDirectoryIsAlsoRemoved()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            Assert.False(Directory.Exists(Path.Combine(root, "Tools", "SourceGenerators")));
        }

        /// <summary>manifest 摘掉两个键之后仍是合法 JSON，其余键一个不少、顺序不变。</summary>
        [Fact]
        public void ManifestKeepsEveryOtherKeyInOrder()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "UnityProject", "Packages", "manifest.json"));
            using var document = JsonDocument.Parse(text);
            var keys = document.RootElement.GetProperty("dependencies").EnumerateObject()
                .Select(property => property.Name).ToArray();

            Assert.Equal(new[] { "com.hsgframe.audio", "com.tuyoogame.yooasset", "com.unity.ugui" }, keys);
        }

        /// <summary>摘掉的正好是最后一项时，前一项的行尾逗号要跟着去掉，否则 JSON 就废了。</summary>
        [Fact]
        public void ManifestStaysValidWhenTheRemovedKeyIsTheLastOne()
        {
            var root = CreateTree();
            WriteText(root, "UnityProject/Packages/manifest.json", @"{
  ""dependencies"": {
    ""com.hsgframe.audio"": ""file:../../Packages/com.hsgframe.audio"",
    ""com.hsgframe.hotfix"": ""file:../../Packages/com.hsgframe.hotfix""
  }
}");

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "UnityProject", "Packages", "manifest.json"));
            using var document = JsonDocument.Parse(text);
            Assert.Equal(
                new[] { "com.hsgframe.audio" },
                document.RootElement.GetProperty("dependencies").EnumerateObject().Select(property => property.Name));
        }

        /// <summary>三个工程条目与它们的平台配置行全没了，别的工程一条不少。</summary>
        [Fact]
        public void SolutionLosesOnlyTheFeatureProjects()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "Solutions", "Template.sln"));
            Assert.DoesNotContain("\"Hotfix\"", text);
            Assert.DoesNotContain("\"Hotfix.Tests\"", text);
            Assert.DoesNotContain("\"HotfixProbeGenerator\"", text);
            Assert.DoesNotContain("{07F399AC-C375-4279-9D57-5D06E6CF5335}", text);
            Assert.DoesNotContain("{90AE16EE-6F2C-44BA-9D77-FC0561E54236}", text);
            Assert.Contains("\"Logic.Tests\"", text);
            Assert.Contains("{829E5702-6D3A-4E72-9FD2-013CFAE5ED1B}", text);
        }

        /// <summary>测试基线只掉指定前缀的条目，剩下的仍是合法 JSON。</summary>
        [Fact]
        public void TestBaselineLosesOnlyThePrefixedEntries()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "Tools", "Gates", "Config", "test-baseline.json"));
            using var document = JsonDocument.Parse(text);
            var keys = document.RootElement.GetProperty("files").EnumerateObject()
                .Select(property => property.Name).ToArray();

            Assert.Equal(new[] { "Solutions/Logic.Tests/HealthTests.cs" }, keys);
        }

        /// <summary>门禁配置掉的是那两个段名与那条规则，_xxx说明 注释键必须还在。</summary>
        [Fact]
        public void GateConfigLosesTheFeatureEntriesButKeepsNoteKeys()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "Tools", "Gates", "Config", "gate-config.json"));
            Assert.DoesNotContain("HybridCLRGenerate", text);
            // HybridCLRData 那条刻意留着：本地那 800 MB 不归这条命令删，跳过项摘了门禁就会去扫它。
            Assert.Contains("HybridCLRData", text);
            Assert.DoesNotContain("featureName", text);
            Assert.Contains("_optionalFeatureScopes说明", text);
            Assert.Contains("PackageCache", text);
            using var document = JsonDocument.Parse(text);
            Assert.Equal(
                new[] { "HybridCLRData", "PackageCache" },
                document.RootElement.GetProperty("sourceScanSkipSegments").EnumerateArray().Select(element => element.GetString()));
        }

        /// <summary>被删目录里的文档不该把整条命令带崩：清单是删目录之前列的，那之后它已经不在了。</summary>
        [Fact]
        public void DocumentInsideARemovedDirectoryDoesNotBreakTheRun()
        {
            var root = CreateTree();
            WriteText(
                root,
                "Tools/SourceGenerators/HotfixProbe/SOURCE.md",
                string.Join("\n", "抬头", BeginMarker, "这一段随目录一起走", EndMarker, string.Empty));

            var result = FeatureRemover.Remove(root, "hotfix");

            Assert.True(result.IsSuccess, result.Message);
            Assert.False(Directory.Exists(Path.Combine(root, "Tools", "SourceGenerators")));
        }

        /// <summary>标记之间的内容连同标记行一起没了，标记之外的一字不动。</summary>
        [Fact]
        public void MarkedDocumentSectionsAreRemoved()
        {
            var root = CreateTree();

            FeatureRemover.Remove(root, "hotfix");

            var text = File.ReadAllText(Path.Combine(root, "getting-started.md"));
            Assert.DoesNotContain("feature:hotfix", text);
            Assert.DoesNotContain("热更专属的一段", text);
            Assert.Contains("标记之前的一段", text);
            Assert.Contains("标记之后的一段", text);
        }

        /// <summary>只有开始标记时整条命令失败，且那份文档一个字节都没被改。</summary>
        [Fact]
        public void UnpairedMarkerFailsWithoutTouchingAnything()
        {
            var root = CreateTree();
            var documentPath = Path.Combine(root, "getting-started.md");
            WriteText(root, "getting-started.md", "抬头\n" + BeginMarker + "\n热更专属的一段\n");
            var before = File.ReadAllText(documentPath);

            var result = FeatureRemover.Remove(root, "hotfix");

            Assert.False(result.IsSuccess);
            Assert.Contains("结束标记", result.Message);
            Assert.Equal(before, File.ReadAllText(documentPath));
            // 预检在动手之前跑，所以包目录也该原封不动。
            Assert.True(Directory.Exists(Path.Combine(root, "Packages", "com.hsgframe.hotfix")));
        }

        /// <summary>功能名不认识时按四要素报错。</summary>
        [Fact]
        public void UnknownFeatureNameFailsWithFourElements()
        {
            var root = CreateTree();

            var result = FeatureRemover.Remove(root, "没有这个功能");

            Assert.False(result.IsSuccess);
            Assert.Contains("位置：", result.Message);
            Assert.Contains("原因：", result.Message);
            Assert.Contains("修复：", result.Message);
            Assert.Contains("参考：", result.Message);
            Assert.Contains("hotfix", result.Message);
        }

        /// <summary>目录本来就不在时记一行跳过，不抛也不判失败。</summary>
        [Fact]
        public void MissingDirectoryIsReportedAsSkippedInsteadOfThrowing()
        {
            var root = CreateTree();
            Directory.Delete(Path.Combine(root, "Tools", "Hotfix"), recursive: true);

            var result = FeatureRemover.Remove(root, "hotfix");

            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.ChangedPaths, line => line.Contains("跳过（已不在）：Tools/Hotfix"));
        }

        private static string CreateTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "feature-remover-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);

            WriteText(root, "Packages/com.hsgframe.hotfix/package.json", "{}");
            WriteText(root, "Tools/Hotfix/Hotfix.csproj", "<Project />");
            WriteText(root, "Tools/SourceGenerators/HotfixProbe/HotfixProbeGenerator.csproj", "<Project />");
            WriteText(root, "Solutions/Hotfix.Tests/Hotfix.Tests.csproj", "<Project />");
            WriteText(root, "UnityProject/Assets/HybridCLRGenerate/AOTGenericReferences.cs", "public class AOTGenericReferences {}");
            WriteText(root, "UnityProject/Assets/HybridCLRGenerate.meta", "folderAsset: yes");
            WriteText(root, "UnityProject/ProjectSettings/HybridCLRSettings.asset", "enable: 1");
            WriteText(root, "UnityProject/ProjectSettings/HybridCLRSettings.asset.meta", "fileFormatVersion: 2");

            WriteText(root, "UnityProject/Packages/manifest.json", @"{
  ""dependencies"": {
    ""com.code-philosophy.hybridclr"": ""https://example.invalid/hybridclr.git#v8.13.0"",
    ""com.hsgframe.audio"": ""file:../../Packages/com.hsgframe.audio"",
    ""com.hsgframe.hotfix"": ""file:../../Packages/com.hsgframe.hotfix"",
    ""com.tuyoogame.yooasset"": ""https://example.invalid/yooasset.git#3.0.5"",
    ""com.unity.ugui"": ""2.0.0""
  }
}");

            WriteText(root, "Solutions/Template.sln", @"Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Logic.Tests"", ""Logic.Tests\Logic.Tests.csproj"", ""{829E5702-6D3A-4E72-9FD2-013CFAE5ED1B}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Hotfix.Tests"", ""Hotfix.Tests\Hotfix.Tests.csproj"", ""{90AE16EE-6F2C-44BA-9D77-FC0561E54236}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Hotfix"", ""..\Tools\Hotfix\Hotfix.csproj"", ""{07F399AC-C375-4279-9D57-5D06E6CF5335}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""HotfixProbeGenerator"", ""..\Tools\SourceGenerators\HotfixProbe\HotfixProbeGenerator.csproj"", ""{5391A74E-47D8-46B4-8D12-949EABDAFDE6}""
EndProject
Global
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{829E5702-6D3A-4E72-9FD2-013CFAE5ED1B}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{90AE16EE-6F2C-44BA-9D77-FC0561E54236}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{07F399AC-C375-4279-9D57-5D06E6CF5335}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{5391A74E-47D8-46B4-8D12-949EABDAFDE6}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
	EndGlobalSection
EndGlobal");

            WriteText(root, "Tools/Gates/Config/test-baseline.json", @"{
  ""files"": {
    ""Solutions/Hotfix.Tests/HotfixLauncherTests.cs"": ""aaaa"",
    ""Solutions/Logic.Tests/HealthTests.cs"": ""bbbb""
  }
}");

            WriteText(root, "Tools/Gates/Config/gate-config.json", @"{
  ""_sourceScanSkipSegments说明"": ""扫描跳过项"",
  ""sourceScanSkipSegments"": [
    ""HybridCLRData"",
    ""HybridCLRGenerate"",
    ""PackageCache""
  ],
  ""_optionalFeatureScopes说明"": ""可选功能的引用范围"",
  ""optionalFeatureScopes"": [
    {
      ""featureName"": ""hotfix"",
      ""packageDirectory"": ""Packages/com.hsgframe.hotfix"",
      ""referencePrefixes"": [ ""HSGFrame.Hotfix"", ""HybridCLR"" ]
    }
  ],
  ""documentLineLimit"": 200
}");

            WriteText(root, "getting-started.md",
                "标记之前的一段\n" + BeginMarker + "\n热更专属的一段\n" + EndMarker + "\n标记之后的一段\n");
            WriteText(root, "Library/缓存说明.md", BeginMarker + "\n这份在跳过目录里，不该被碰\n");

            return root;
        }

        private static void WriteText(string root, string relativePath, string content)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }
    }
}
