using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 域路由写入器的测试：候选逐个校验、读改写不重建、只改策略时沿用现有候选。
    ///
    /// 最要紧的一条是**候选校验**：只查「这个名字的目录在不在」是不够的，
    /// 还得查那份自述里真的声明了这个 port——名字对、目录也在、但它这辈子不响应那个动作，
    /// 这种错要等到真调用才炸，而那时报的是「驱动自述缺失」之类指不到路由表的话。
    /// </summary>
    public class RouteTableWriterTests
    {
        /// <summary>换首选：候选按给的顺序写回，实现表与说明字段原样保留。</summary>
        [Fact]
        public void SetPortRouteRewritesCandidatesAndKeepsEverythingElse()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteDriver(workspace.Root, "乙驱动", "某域");
            WriteRouteTable(workspace.Root);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "乙驱动", "甲驱动" }, PortRoute.FailoverStrategy);

            Assert.True(outcome.Succeeded, outcome.Message);

            var table = BridgeRouteTable.Load(workspace.Root);
            Assert.True(table.Loaded, table.LoadFailureReason);
            Assert.True(table.TryResolveRoute("某域", out var route, out _));
            Assert.Equal(new[] { "乙驱动", "甲驱动" }, route.Candidates);
            Assert.Equal(PortRoute.FailoverStrategy, route.Strategy);

            // 读改写不重建：实现表与顶层说明必须还在。
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(RouteTablePath(workspace.Root)));
            Assert.NotNull(root["实现"]);
            Assert.NotNull(root["_说明"]);
        }

        /// <summary>只给策略、不给候选时，沿用现有候选——「这个域挂了要不要自动换人」是最常见的动作。</summary>
        [Fact]
        public void StrategyOnlyUpdateKeepsExistingCandidates()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteDriver(workspace.Root, "乙驱动", "某域");
            WriteRouteTable(workspace.Root);
            RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动", "乙驱动" }, PortRoute.FixedPreferredStrategy);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", Array.Empty<string>(), PortRoute.FailoverStrategy);

            Assert.True(outcome.Succeeded, outcome.Message);
            var table = BridgeRouteTable.Load(workspace.Root);
            Assert.True(table.TryResolveRoute("某域", out var route, out _));
            Assert.Equal(new[] { "甲驱动", "乙驱动" }, route.Candidates);
            Assert.Equal(PortRoute.FailoverStrategy, route.Strategy);
        }

        /// <summary>候选是个不存在的 driver → 拒绝写，原因里点名它。</summary>
        [Fact]
        public void UnknownDriverIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteRouteTable(workspace.Root);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动", "查无此人" }, "");

            Assert.False(outcome.Succeeded);
            Assert.Contains("查无此人", outcome.Message);
        }

        /// <summary>候选存在，但它没声明这个 port → 拒绝写，并把它到底声明了什么摆出来。</summary>
        [Fact]
        public void DriverWithoutThatPortIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteDriver(workspace.Root, "丙驱动", "另一个域");
            WriteRouteTable(workspace.Root);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动", "丙驱动" }, "");

            Assert.False(outcome.Succeeded);
            Assert.Contains("丙驱动", outcome.Message);
            Assert.Contains("另一个域", outcome.Message);
        }

        /// <summary>不认的策略 → 拒绝写。写进去会让整份路由表判坏，连带别的域一起不能用。</summary>
        [Fact]
        public void UnknownStrategyIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteRouteTable(workspace.Root);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动" }, "随便挑一个");

            Assert.False(outcome.Succeeded);
            Assert.Contains("随便挑一个", outcome.Message);
        }

        /// <summary>同一个候选写两遍 → 拒绝：失败转移会把它试两次。</summary>
        [Fact]
        public void DuplicateCandidateIsRejected()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            WriteRouteTable(workspace.Root);

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动", "甲驱动" }, "");

            Assert.False(outcome.Succeeded);
            Assert.Contains("甲驱动", outcome.Message);
        }

        /// <summary>路由表 JSON 坏掉时拒绝写，绝不用一份干净骨架把人写了一半的文件盖掉。</summary>
        [Fact]
        public void BrokenRouteTableIsNotOverwritten()
        {
            using var workspace = new Workspace();
            WriteDriver(workspace.Root, "甲驱动", "某域");
            var path = RouteTablePath(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ 这不是 JSON", new UTF8Encoding(false));

            var outcome = RouteTableWriter.SetPortRoute(workspace.Root, "某域", new[] { "甲驱动" }, "");

            Assert.False(outcome.Succeeded);
            Assert.Equal("{ 这不是 JSON", File.ReadAllText(path));
        }

        private static string RouteTablePath(string root)
        {
            return Path.Combine(root, "Tools", "CreationPipeline", "Config", "downstream.json");
        }

        private static void WriteRouteTable(string root)
        {
            var path = RouteTablePath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "{\"_说明\":\"给人看的\",\"契约版本\":\"1.0.0\",\"域路由\":{\"某域\":\"甲驱动\"},\"实现\":{}}",
                new UTF8Encoding(false));
        }

        private static void WriteDriver(string root, string driverName, string portName)
        {
            var directory = Path.Combine(root, "Bridges", driverName);
            Directory.CreateDirectory(directory);
            var json = "{\"名称\":\"" + driverName + "\",\"port\":[\"" + portName + "\"],\"形态\":\"线上\","
                + "\"契约版本\":\">=1.0 <2.0\",\"配置schema\":{},\"密钥字段\":[],\"试跑\":\"\",\"实现\":\"impl\","
                + "\"字段类型映射\":{},\"表单分组字段\":\"\"}";
            File.WriteAllText(Path.Combine(directory, "driver.json"), json, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "路由写入测试-" + Guid.NewGuid().ToString("N"));
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
