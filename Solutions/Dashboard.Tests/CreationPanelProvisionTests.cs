using System;
using System.IO;
using System.Text;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板供给对账页读取器测试：全部用系统临时目录建仓库，跑完自删。</summary>
    public sealed class CreationPanelProvisionTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelProvisionTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板供给对账读取器测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>Bridges 目录不存在时返回空列表。</summary>
        [Fact]
        public void MissingBridgesDirectoryReturnsEmpty()
        {
            Assert.Empty(CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot));
        }

        /// <summary>自述合法但没有指纹的 driver：供给状态是未供给、对账状态是未跑（本页最要紧的一条断言）。</summary>
        [Fact]
        public void ValidDescriptorWithoutFingerprintIsUnprovisionedAndUntested()
        {
            WriteDriver("driver-alpha", """
                {
                  "名称": "driver-alpha",
                  "port": [ "http://127.0.0.1:8080" ],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "impl",
                  "字段类型映射": {}
                }
                """);

            var rows = CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("driver-alpha", row.DriverName);
            Assert.Equal("本地", row.Form);
            Assert.Equal("未供给", row.ProvisionState);
            Assert.Equal("未跑", row.ReconcileState);
        }

        /// <summary>自述是坏 JSON 时供给状态是自述损坏，不抛。</summary>
        [Fact]
        public void BrokenDescriptorIsMarkedBroken()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteDriver("driver-broken", """
                {
                  not valid json at all
                """);

            var rows = CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("driver-broken", row.DriverName);
            Assert.Equal("自述损坏", row.ProvisionState);
            Assert.Equal("", row.Form);
        }

        /// <summary>有依赖清单 / 没有，HasDependencyManifest 对应 true / false。</summary>
        [Fact]
        public void HasDependencyManifestReflectsManifestFileExistence()
        {
            WriteDriver("driver-with-manifest", """
                {
                  "名称": "driver-with-manifest",
                  "port": [],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "impl",
                  "字段类型映射": {}
                }
                """);
            WriteFile(Path.Combine(_repositoryRoot, "Bridges", "driver-with-manifest", "依赖清单.json"), """
                {
                  "契约版本": "1.0",
                  "依赖": []
                }
                """);
            WriteDriver("driver-without-manifest", """
                {
                  "名称": "driver-without-manifest",
                  "port": [],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "impl",
                  "字段类型映射": {}
                }
                """);

            var rows = CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot);

            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].HasDependencyManifest);
            Assert.False(rows[1].HasDependencyManifest);
        }

        /// <summary>配方目录下两个子目录，RecipeCount 是 2。</summary>
        [Fact]
        public void RecipeCountCountsRecipeDirectories()
        {
            WriteDriver("driver-recipes", """
                {
                  "名称": "driver-recipes",
                  "port": [],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "impl",
                  "字段类型映射": {}
                }
                """);
            Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Bridges", "driver-recipes", "配方", "配方一"));
            Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Bridges", "driver-recipes", "配方", "配方二"));

            var rows = CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal(2, row.RecipeCount);
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        /// <summary>对账整体没跑成时，有指纹的 driver 也必须记「未跑」，不许说成「一致」。</summary>
        [Fact]
        public void FingerprintPresentButReconcileFailedIsStillUntested()
        {
            // 池根故意不铺基线 schema，ProvisionReconciler.Reconcile 会抛。
            // 此时该 driver 有指纹、零 finding——若按「零 finding 即一致」判，
            // 就会把崩掉的对账说成对上了。这条锁住的正是那个假绿。
            WriteDriver("driver-gamma", """
                {
                  "名称": "driver-gamma",
                  "port": [ "http://127.0.0.1:8080" ],
                  "形态": "本地",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "impl",
                  "字段类型映射": {}
                }
                """);
            WriteFile(
                Path.Combine(_repositoryRoot, "_Generated", "Bridges", "driver-gamma", "指纹.json"),
                """
                { "自述哈希": "aaa", "产物哈希": "bbb" }
                """);

            var rows = CreationPanelReader.ReadProvision(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("已供给", row.ProvisionState);
            Assert.Equal("未跑", row.ReconcileState);
        }

        private void WriteDriver(string driverName, string json)
        {
            var directory = Path.Combine(_repositoryRoot, "Bridges", driverName);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "driver.json"), json);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
