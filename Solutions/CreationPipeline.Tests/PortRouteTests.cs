using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 开关路由的解析测试：域路由的两种写法、策略语义，以及非法写法一律判坏整份表。
    ///
    /// 为什么非法写法要判坏整份表而不是跳过那一条：路由表是调用链的分岔口，
    /// 一条写歪的候选清单会让调用悄悄走到别的下游去——那比当场报错难查得多。
    /// </summary>
    public class PortRouteTests
    {
        /// <summary>老写法（字符串）继续认：等价于候选只有一个、策略首选固定。</summary>
        [Fact]
        public void StringShapeIsSingleCandidateFixedPreferred()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": \"甲驱动\""));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.Loaded, table.LoadFailureReason);
            Assert.True(table.TryResolveRoute("某域", out var route, out var reason), reason);
            Assert.Equal(new[] { "甲驱动" }, route.Candidates);
            Assert.Equal(PortRoute.FixedPreferredStrategy, route.Strategy);
            Assert.False(route.AllowsFailover);
        }

        /// <summary>对象写法：候选按写的顺序，策略照写的取。</summary>
        [Fact]
        public void ObjectShapeKeepsCandidateOrderAndStrategy()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\", \"乙驱动\"], \"策略\": \"失败转移\" }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.Loaded, table.LoadFailureReason);
            Assert.True(table.TryResolveRoute("某域", out var route, out var reason), reason);
            Assert.Equal(new[] { "甲驱动", "乙驱动" }, route.Candidates);
            Assert.Equal(PortRoute.FailoverStrategy, route.Strategy);
            Assert.True(route.AllowsFailover);
        }

        /// <summary>缺「策略」时按首选固定——默认不换人，这是省钱的那一边。</summary>
        [Fact]
        public void MissingStrategyDefaultsToFixedPreferred()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\", \"乙驱动\"] }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolveRoute("某域", out var route, out _));
            Assert.Equal(PortRoute.FixedPreferredStrategy, route.Strategy);
            Assert.False(route.AllowsFailover);
        }

        /// <summary>只有一个候选时，写了失败转移也换不了人——没有下一个可换。</summary>
        [Fact]
        public void SingleCandidateNeverAllowsFailover()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\"], \"策略\": \"失败转移\" }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolveRoute("某域", out var route, out _));
            Assert.Equal(PortRoute.FailoverStrategy, route.Strategy);
            Assert.False(route.AllowsFailover);
        }

        /// <summary>TryResolvePort 拿的是首选，老调用点不用改就继续对。</summary>
        [Fact]
        public void ResolvePortReturnsPreferredCandidate()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\", \"乙驱动\"], \"策略\": \"失败转移\" }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolvePort("某域", out var driverName, out var reason), reason);
            Assert.Equal("甲驱动", driverName);
        }

        /// <summary>下划线开头的是说明字段，不是路由项，不许被当成一个域。</summary>
        [Fact]
        public void UnderscoreKeysAreNotRoutes()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"_说明\": \"这是给人看的\", \"某域\": \"甲驱动\""));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.True(table.Loaded, table.LoadFailureReason);
            Assert.Single(table.PortRoutes);
            Assert.False(table.TryResolveRoute("_说明", out _, out _));
        }

        /// <summary>不认的策略名 → 整份表判坏，原因里点名那个策略。</summary>
        [Fact]
        public void UnknownStrategyBreaksWholeTable()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\"], \"策略\": \"随便挑一个\" }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("随便挑一个", table.LoadFailureReason);
        }

        /// <summary>空候选数组 → 判坏。这个域等于没有下游，静默放过等于埋一个只在调用时才炸的雷。</summary>
        [Fact]
        public void EmptyCandidateArrayBreaksWholeTable()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [] }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("某域", table.LoadFailureReason);
        }

        /// <summary>同一个候选写两遍 → 判坏：失败转移会把它试两次，白等一轮超时。</summary>
        [Fact]
        public void DuplicateCandidateBreaksWholeTable()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\", \"甲驱动\"] }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("甲驱动", table.LoadFailureReason);
        }

        /// <summary>候选里混进非字符串 → 判坏，不静默丢掉那一项。</summary>
        [Fact]
        public void NonStringCandidateBreaksWholeTable()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": { \"候选\": [\"甲驱动\", 7] }"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("某域", table.LoadFailureReason);
        }

        /// <summary>既不是字符串也不是对象（比如写了个数组）→ 判坏。</summary>
        [Fact]
        public void UnsupportedShapeBreaksWholeTable()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, PortSection("\"某域\": [\"甲驱动\"]"));

            var table = BridgeRouteTable.Load(workspace.Root);

            Assert.False(table.Loaded);
            Assert.Contains("某域", table.LoadFailureReason);
        }

        /// <summary>拿域路由那一节的内容拼一份完整的路由表 JSON（实现表给空对象，这些用例不碰它）。</summary>
        private static string PortSection(string portEntries)
        {
            return "{\"契约版本\":\"1.0.0\",\"域路由\":{" + portEntries + "},\"实现\":{}}";
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
                Root = Path.Combine(Path.GetTempPath(), "开关路由测试-" + Guid.NewGuid().ToString("N"));
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
