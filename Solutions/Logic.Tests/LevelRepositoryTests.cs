using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Logic.Data.Level;
using Xunit;

namespace Template.Logic.Tests
{
    /// <summary>关卡仓库测试：按需加载、内存复用、按块卸载与四要素失败消息。</summary>
    public class LevelRepositoryTests
    {
        [Fact]
        public void VillageLevelManifestHasTwoChunks()
        {
            var repository = new LevelRepository(VillageDirectory());

            var level = repository.LoadLevel();

            Assert.Equal(2, level.ChunkNames.Count);
        }

        [Fact]
        public void AllChunksLoadToTwentyFourPlacements()
        {
            var repository = new LevelRepository(VillageDirectory());

            var chunks = repository.LoadAllChunks();
            var total = chunks.Values.Sum(chunk => chunk.Placements.Count);

            Assert.Equal(24, total);
        }

        [Fact]
        public void VillageLevelValidatesClean()
        {
            var repository = new LevelRepository(VillageDirectory());

            var errors = repository.Validate();

            Assert.Empty(errors);
        }

        [Fact]
        public void VillageGuardCarriesKindPositionAndFaction()
        {
            var repository = new LevelRepository(VillageDirectory());

            var chunk = repository.LoadChunk("区块_村口");
            var placement = chunk.Placements.Single(entity => entity.EntityId == "村口_守卫_01");

            Assert.Equal("NPC", placement.EntityKind);
            Assert.Equal(12.5, placement.Position.X, 3);
            Assert.Equal(0.0, placement.Position.Y, 3);
            Assert.Equal(-3.25, placement.Position.Z, 3);
            Assert.Equal("友方", placement.Parameters["阵营"]);
        }

        [Fact]
        public void PlazaQuestStoneCarriesKindAndQuestId()
        {
            var repository = new LevelRepository(VillageDirectory());

            var chunk = repository.LoadChunk("区块_广场");
            var placement = chunk.Placements.Single(entity => entity.EntityId == "广场_任务物件_石碑");

            Assert.Equal("任务物件", placement.EntityKind);
            Assert.Equal("2002", placement.Parameters["任务编号"]);
        }

        [Fact]
        public void FreshRepositoryHasNoLoadedChunks()
        {
            var repository = new LevelRepository(VillageDirectory());

            Assert.Empty(repository.LoadedChunkNames);
        }

        [Fact]
        public void LoadingOneChunkDoesNotPullInAnother()
        {
            var repository = new LevelRepository(VillageDirectory());

            repository.LoadChunk("区块_村口");

            Assert.Single(repository.LoadedChunkNames);
            Assert.Equal("区块_村口", repository.LoadedChunkNames[0]);
        }

        [Fact]
        public void LoadingSameChunkTwiceReusesMemoryInstance()
        {
            var repository = new LevelRepository(VillageDirectory());

            var first = repository.LoadChunk("区块_村口");
            var readCountAfterFirst = repository.FileReadCount;
            var second = repository.LoadChunk("区块_村口");

            Assert.Equal(readCountAfterFirst, repository.FileReadCount);
            Assert.Same(first, second);
        }

        [Fact]
        public void UnloadingLoadedChunkReturnsTrueAndDropsIt()
        {
            var repository = new LevelRepository(VillageDirectory());
            repository.LoadChunk("区块_村口");

            var removed = repository.UnloadChunk("区块_村口");

            Assert.True(removed);
            Assert.DoesNotContain("区块_村口", repository.LoadedChunkNames);
        }

        [Fact]
        public void UnloadingUnloadedChunkReturnsFalse()
        {
            var repository = new LevelRepository(VillageDirectory());

            var removed = repository.UnloadChunk("区块_广场");

            Assert.False(removed);
        }

        [Fact]
        public void ReloadingAfterUnloadReadsDiskAgain()
        {
            var repository = new LevelRepository(VillageDirectory());
            repository.LoadChunk("区块_村口");
            var readCountBeforeUnload = repository.FileReadCount;

            repository.UnloadChunk("区块_村口");
            repository.LoadChunk("区块_村口");

            Assert.Equal(readCountBeforeUnload + 1, repository.FileReadCount);
        }

        [Fact]
        public void MissingLevelDirectoryReportsFourElements()
        {
            var missingDirectory = Path.Combine(Path.GetTempPath(), "LevelRepositoryTests-" + Guid.NewGuid().ToString("N"));
            var repository = new LevelRepository(missingDirectory);

            var exception = Assert.Throws<LevelDataException>(() => repository.LoadLevel());

            AssertFourElements(exception.Message);
        }

        [Fact]
        public void MissingLevelDefinitionFileReportsFourElements()
        {
            var directory = CreateTempDirectory();
            try
            {
                var repository = new LevelRepository(directory);

                var exception = Assert.Throws<LevelDataException>(() => repository.LoadLevel());

                AssertFourElements(exception.Message);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void MalformedLevelJsonReportsFourElementsWithInnerException()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "关卡.json"), "{ 这不是合法 json");
                var repository = new LevelRepository(directory);

                var exception = Assert.Throws<LevelDataException>(() => repository.LoadLevel());

                AssertFourElements(exception.Message);
                Assert.NotNull(exception.InnerException);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void UnregisteredChunkNameReportsItsOwnName()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "关卡.json"),
                    "{\"关卡名\":\"测试关卡\",\"环境\":\"白天\",\"区块清单\":[]}");
                var repository = new LevelRepository(directory);

                var exception = Assert.Throws<LevelDataException>(() => repository.LoadChunk("区块_鬼"));

                AssertFourElements(exception.Message);
                Assert.Contains("区块_鬼", exception.Message);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void RegisteredChunkMissingItsFileReportsFourElements()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "关卡.json"),
                    "{\"关卡名\":\"测试关卡\",\"环境\":\"白天\",\"区块清单\":[\"区块_码头\"]}");
                var repository = new LevelRepository(directory);

                var exception = Assert.Throws<LevelDataException>(() => repository.LoadChunk("区块_码头"));

                AssertFourElements(exception.Message);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string VillageDirectory()
        {
            return Path.Combine(FindTemplateRoot(), "Levels", "村庄");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LevelRepositoryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void AssertFourElements(string message)
        {
            Assert.Contains("位置", message);
            Assert.Contains("原因", message);
            Assert.Contains("修复", message);
            Assert.Contains("参考", message);
        }

        // 测试工作目录不稳定，不能靠相对路径硬拼：从程序集目录逐级向上找带 Tools/Gates/Config 的那一级作为模板根——
        // 模板被复制成别的项目名之后，这个标记仍然成立，而目录名 "Template" 不再成立。
        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            var searched = new List<string>();
            while (directory != null)
            {
                searched.Add(directory.FullName);
                if (File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"未找到包含 Tools/Gates/Config 的模板根，已查找：{string.Join(Environment.NewLine, searched)}");
            return string.Empty;
        }
    }
}
