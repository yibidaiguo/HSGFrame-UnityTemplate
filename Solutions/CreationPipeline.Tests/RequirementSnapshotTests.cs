using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 需求快照存取测试：取当前版次、原文逐字节落盘、既有版本永不覆盖、目录不存在自建。
    /// </summary>
    public class RequirementSnapshotTests
    {
        /// <summary>空目录 → CurrentVersion 为 0。</summary>
        [Fact]
        public void EmptyDirectoryHasCurrentVersionZero()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.Equal(0, RequirementSnapshotStore.CurrentVersion(workspace.RepositoryRoot, "REQ-0001"));
        }

        /// <summary>Capture 一次 → 文件 00-需求.v1.json 存在，内容与传入的原文逐字节相等。</summary>
        [Fact]
        public void CaptureWritesOriginalBytesToVersionOne()
        {
            using var workspace = new PoolTestWorkspace();
            var original = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\",\"玩法\":\"翻滚\",\"目标\":\"一次三连\"}";

            var snapshot = RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", original);

            Assert.Equal(1, snapshot.Version);
            var filePath = PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1);
            Assert.Equal(filePath, snapshot.FilePath);
            Assert.True(File.Exists(filePath));
            // 逐字节原样：不重新序列化、不美化、不排键。
            Assert.Equal(original, File.ReadAllText(filePath));
        }

        /// <summary>再 Capture 一次 → 写出 v2，且 v1 的内容一字未变。</summary>
        [Fact]
        public void SecondCaptureWritesVersionTwoAndKeepsVersionOneUntouched()
        {
            using var workspace = new PoolTestWorkspace();
            var first = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\"}";
            var second = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋升级版\",\"玩法\":\"翻滚\"}";

            RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", first);
            RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", second);

            Assert.Equal(first, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1)));
            Assert.Equal(second, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 2)));
        }

        /// <summary>
        /// v3 已存在时 Capture（先手工造 v1、v3）→ CurrentVersion 是 3，新的写成 v4，v1、v3 一字未变。
        /// </summary>
        [Fact]
        public void CaptureWritesVersionFourWhenThreeExists()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = PipelinePaths.TaskDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var versionOneText = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\"}";
            var versionThreeText = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋 v3\"}";
            File.WriteAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1), versionOneText, new UTF8Encoding(false));
            File.WriteAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 3), versionThreeText, new UTF8Encoding(false));

            Assert.Equal(3, RequirementSnapshotStore.CurrentVersion(workspace.RepositoryRoot, "REQ-0001"));
            var fresh = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋 v4\"}";
            var snapshot = RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", fresh);

            Assert.Equal(4, snapshot.Version);
            Assert.Equal(versionOneText, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1)));
            Assert.Equal(versionThreeText, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 3)));
            Assert.Equal(fresh, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 4)));
        }

        /// <summary>
        /// 新版次文件已存在时，Capture 拒绝覆盖它并继续写下一个空版次——既有版本文件永不覆盖。
        /// 任务书设想的「新版次文件已存在即抛 InvalidOperationException」在规格实现下单线程不可达：
        /// 新版次 = 当前最大 N + 1，若 v(N+1) 已存在则 CurrentVersion 至少是 N+1，检查必然跳过该文件。
        /// 该抛错分支是并发窗口（另一进程抢先写入）的防御，单线程无法稳定构造，故用「直接先写文件」
        /// 构造等价场景：预置 v1、v3、v4，再 Capture 应写 v5 且 v4 一字未变（任务书允许的替代验证）。
        /// </summary>
        [Fact]
        public void CaptureNeverOverwritesExistingVersionFile()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = PipelinePaths.TaskDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var versionOneText = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\"}";
            var versionThreeText = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋 v3\"}";
            var versionFourText = "{\"id\":\"REQ-0001\",\"标题\":\"并发抢先写好的 v4\"}";
            File.WriteAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1), versionOneText, new UTF8Encoding(false));
            File.WriteAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 3), versionThreeText, new UTF8Encoding(false));
            File.WriteAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 4), versionFourText, new UTF8Encoding(false));

            var fresh = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋 v5\"}";
            var snapshot = RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", fresh);

            // v4 是已存在的版本文件，一字未变；新的写到 v5。
            Assert.Equal(5, snapshot.Version);
            Assert.Equal(versionFourText, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 4)));
            Assert.Equal(fresh, File.ReadAllText(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 5)));
        }

        /// <summary>任务目录不存在 → Capture 自己把目录建出来。</summary>
        [Fact]
        public void CaptureCreatesMissingTaskDirectory()
        {
            using var workspace = new PoolTestWorkspace();
            var taskDirectory = PipelinePaths.TaskDirectory(workspace.RepositoryRoot, "REQ-0001");
            Assert.False(Directory.Exists(taskDirectory));

            var snapshot = RequirementSnapshotStore.Capture(workspace.RepositoryRoot, "REQ-0001", "{}");

            Assert.Equal(1, snapshot.Version);
            Assert.True(Directory.Exists(taskDirectory));
            Assert.True(File.Exists(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1)));
        }
    }
}
