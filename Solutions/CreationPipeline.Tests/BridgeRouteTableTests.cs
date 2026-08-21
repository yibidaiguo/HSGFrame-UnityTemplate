using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>下游路由表（Tools/CreationPipeline/Config/downstream.json）的解析测试：两支语义、port/实现解析、路径展开。</summary>
    public class BridgeRouteTableTests
    {
        private const string RouteTableJson = """
            {
              "契约版本": "1.0.0",
              "域路由": { "模型加工": "blender" },
              "实现": {
                "bridge-blender": {
                  "可执行": "dotnet",
                  "参数": ["run", "--project", "Bridges/blender/src/BridgeBlender", "--"]
                }
              }
            }
            """;

        /// <summary>文件不存在与文件坏掉必须是两支：不存在 → Loaded=true、表空、reason 写「下游路由表不存在」。</summary>
        [Fact]
        public void MissingFileIsLoadedWithEmptyTable()
        {
            using var workspace = new Workspace();

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.Loaded);
            Assert.Empty(table.PortRoutes);
            Assert.Empty(table.Implementations);
            Assert.False(table.TryResolvePort("模型加工", out _, out var portReason));
            Assert.Equal("下游路由表不存在", portReason);
            Assert.False(table.TryResolveImplementation("bridge-blender", out _, out _, out var implementationReason));
            Assert.Equal("下游路由表不存在", implementationReason);
        }

        /// <summary>文件坏掉 → Loaded=false、reason 写清坏在哪；这时整份表不可信，解析一律失败。</summary>
        [Fact]
        public void BrokenJsonIsNotLoaded()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "{ 这不是 JSON");

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("不是合法 JSON", table.LoadFailureReason);
            Assert.False(table.TryResolvePort("模型加工", out _, out var portReason));
            Assert.Equal(table.LoadFailureReason, portReason);
        }

        /// <summary>缺「域路由」或「实现」都是坏文件 → Loaded=false。</summary>
        [Fact]
        public void MissingRequiredSectionIsNotLoaded()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "{\"契约版本\":\"1.0.0\",\"实现\":{}}");

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("域路由", table.LoadFailureReason);
        }

        /// <summary>port 解析：域路由把「模型加工」指到 driver 名。</summary>
        [Fact]
        public void ResolvePortFindsDriver()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, RouteTableJson);

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.Loaded);
            Assert.True(table.TryResolvePort("模型加工", out var driverName, out var reason), reason);
            Assert.Equal("blender", driverName);
        }

        /// <summary>port 解析：查不到的 port 给出明确原因，不跟「表不存在」混在一起。</summary>
        [Fact]
        public void ResolvePortUnknownGivesDistinctReason()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, RouteTableJson);

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.TryResolvePort("生图", out _, out var reason));
            Assert.Contains("生图", reason);
            Assert.DoesNotContain("下游路由表不存在", reason);
        }

        /// <summary>实现解析：可执行与参数原样带回，命令词不展开。</summary>
        [Fact]
        public void ResolveImplementationKeepsCommandWords()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, RouteTableJson);

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolveImplementation("bridge-blender", out var executable, out var arguments, out var reason), reason);
            Assert.Equal("dotnet", executable);
            Assert.Equal("run", arguments[0]);
            Assert.Equal("--project", arguments[1]);
            Assert.Equal("--", arguments[3]);
        }

        /// <summary>实现解析：参数里的仓库相对路径按仓库根展开成绝对路径（决策 85）。</summary>
        [Fact]
        public void ResolveImplementationExpandsRepositoryRelativePaths()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, RouteTableJson);

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolveImplementation("bridge-blender", out _, out var arguments, out _));
            var expected = Path.GetFullPath(Path.Combine(workspace.Root, "Bridges", "blender", "src", "BridgeBlender"));
            Assert.Equal(expected, arguments[2]);
        }

        /// <summary>实现解析：查不到的实现给出明确原因。</summary>
        [Fact]
        public void ResolveImplementationUnknownGivesReason()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, RouteTableJson);

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.TryResolveImplementation("bridge-comfyui", out _, out _, out var reason));
            Assert.Contains("bridge-comfyui", reason);
        }

        private static void WriteRouteTable(string root, string json)
        {
            var path = Path.Combine(root, "Tools", "CreationPipeline", "Config", "downstream.json");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "路由表测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
