using System;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 助手读代码那条边界的测试。
    ///
    /// **这一层是安全边界**，不是功能：读到的东西会进提示词、发给下游模型，
    /// 也就是离开这台机器。所以这里盯的全是「什么读不到」，
    /// 而每一条都对应一种真会发生的读法：相对路径往上爬、拿工作流那棵树、
    /// 拿一个不该读的扩展名。
    /// </summary>
    public class ProjectCodeReaderTests : IDisposable
    {
        private readonly string _root;

        public ProjectCodeReaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "code-reader-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响判定。
            }
        }

        private string Write(string relativePath, string content)
        {
            var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        private bool Resolves(string requested, out string reason)
        {
            return ProjectCodeReader.TryResolve(Path.GetFullPath(_root), requested, out _, out _, out reason);
        }

        /// <summary>Unity 工程的代码读得到。</summary>
        [Fact]
        public void ReadsUnityGameCode()
        {
            Write("UnityProject/Assets/Game/Scripts/Modules/Inventory/InventoryService.cs", "class A {}");

            Assert.True(
                Resolves("UnityProject/Assets/Game/Scripts/Modules/Inventory/InventoryService.cs", out var reason),
                reason);
        }

        /// <summary>池子、规范、配置表结构也读得到——光有代码读不准。</summary>
        [Theory]
        [InlineData("Pools/Designs/Interfaces/UI-0001.json")]
        [InlineData("Specifications/Baseline/asset-spec.baseline.json")]
        [InlineData("Config/Schema/Bag.schema.json")]
        public void ReadsPoolsSpecsAndConfigSchema(string relativePath)
        {
            Write(relativePath, "{}");

            Assert.True(Resolves(relativePath, out var reason), reason);
        }

        /// <summary>
        /// **工作流那棵树读不到。** 密钥住在 Tools/CreationPipeline/Config/local.json，
        /// 而这里挡的不是那一个文件，是整棵 Tools/——挡文件要穷举，漏一个就是漏一个。
        /// </summary>
        [Theory]
        [InlineData("Tools/CreationPipeline/Config/local.json")]
        [InlineData("Tools/CreationPipeline/Config/downstream-objects.json")]
        [InlineData("Tools/Cli/CommandHost/Commands/AssistantCommands.cs")]
        public void RefusesTheWorkflowTree(string relativePath)
        {
            Write(relativePath, "{ \"飞书应用密钥\": \"绝密\" }");

            Assert.False(Resolves(relativePath, out var reason));
            Assert.Contains("不在允许读的目录", reason);
        }

        /// <summary>
        /// 相对路径往上爬读不到。
        ///
        /// **判据的顺序是这条能挡住的原因**：先把路径解析成绝对路径再比前缀。
        /// 反过来的话，这个字符串以白名单前缀开头，比前缀能过，而它实际指到仓库外面。
        /// </summary>
        [Fact]
        public void RefusesPathsThatClimbOutOfTheRepository()
        {
            Assert.False(
                Resolves("UnityProject/Assets/Game/Scripts/../../../../../secret.cs", out var reason));
            Assert.Contains("仓库外面", reason);
        }

        /// <summary>绕一圈回到 Tools/ 也不行——爬出去再爬回来同样过不了前缀那一关。</summary>
        [Fact]
        public void RefusesPathsThatClimbBackIntoTheWorkflowTree()
        {
            Write("Tools/CreationPipeline/Config/local.json", "{}");

            Assert.False(
                Resolves("UnityProject/Assets/Game/../../../Tools/CreationPipeline/Config/local.json", out var reason));
            Assert.Contains("不在允许读的目录", reason);
        }

        /// <summary>框架包底下只读 Runtime/：编辑器脚本与测试不是模块行为的一部分。</summary>
        [Fact]
        public void ReadsOnlyTheRuntimeSegmentOfFrameworkPackages()
        {
            Write("Packages/com.hsgframe.event/Runtime/EventBus.cs", "class A {}");
            Write("Packages/com.hsgframe.event/Editor/EventBusEditor.cs", "class B {}");

            Assert.True(Resolves("Packages/com.hsgframe.event/Runtime/EventBus.cs", out var ok), ok);
            Assert.False(Resolves("Packages/com.hsgframe.event/Editor/EventBusEditor.cs", out var reason));
            Assert.Contains("只读 Runtime/", reason);
        }

        /// <summary>不认的扩展名不读——二进制读了没意义，还撑爆提示词。</summary>
        [Fact]
        public void RefusesExtensionsItDoesNotUnderstand()
        {
            Write("UnityProject/Assets/Game/Art/T_Bag.png", "not really a png");

            Assert.False(Resolves("UnityProject/Assets/Game/Art/T_Bag.png", out var reason));
            Assert.Contains("扩展名", reason);
        }

        /// <summary>文件不在时如实说，不静默跳过。</summary>
        [Fact]
        public void SaysSoWhenTheFileIsNotThere()
        {
            Assert.False(Resolves("UnityProject/Assets/Game/Scripts/Nope.cs", out var reason));
            Assert.Contains("不在", reason);
        }

        /// <summary>一次最多读几个文件是有上限的，超了如实说而不是闷头全读。</summary>
        [Fact]
        public void StopsAtTheFileCountLimit()
        {
            var requested = new string[ProjectCodeReader.MaximumFileCount + 3];
            for (var index = 0; index < requested.Length; index++)
            {
                var path = $"UnityProject/Assets/Game/Scripts/File{index}.cs";
                Write(path, "class A" + index + " {}");
                requested[index] = path;
            }

            var result = ProjectCodeReader.Read(_root, requested);

            Assert.Equal(ProjectCodeReader.MaximumFileCount, result.ReadPaths.Count);
            Assert.Contains(result.Notes, note => note.Contains("最多读"));
        }

        /// <summary>拒了谁要留一句话：静默少读一个文件，模型会照着不完整的材料下结论。</summary>
        [Fact]
        public void EveryRefusalLeavesANote()
        {
            Write("UnityProject/Assets/Game/Scripts/Good.cs", "class A {}");

            var result = ProjectCodeReader.Read(_root, new[]
            {
                "UnityProject/Assets/Game/Scripts/Good.cs",
                "Tools/CreationPipeline/Config/local.json"
            });

            Assert.Single(result.ReadPaths);
            Assert.Contains(result.Notes, note => note.Contains("local.json"));
        }

        /// <summary>文件清单与读取判据共用同一份前缀表——列出来的每个路径都真读得了。</summary>
        [Fact]
        public void EveryListedFileIsActuallyReadable()
        {
            Write("UnityProject/Assets/Game/Scripts/Modules/Inventory/InventoryService.cs", "class A {}");
            Write("Tools/CreationPipeline/Config/local.json", "{}");

            var listed = ReadableFileIndex.Collect(_root);

            Assert.NotEmpty(listed);
            Assert.All(listed, path => Assert.True(Resolves(path, out _), path));
            Assert.DoesNotContain(listed, path => path.StartsWith("Tools/", StringComparison.Ordinal));
        }
    }
}
