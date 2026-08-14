using System.Collections.Generic;
using HSGhost.Save;
using Xunit;

namespace HSGhost.Save.Tests
{
    /// <summary>存档 JSON 往返与版本迁移链的测试。</summary>
    public class SaveMigrationTests
    {
        [Fact]
        public void SaveDocumentRoundTripsThroughJson()
        {
            var document = new SaveDocument { Version = 1 };
            document.Sections["背包"] = "{\"金币\": 100}";
            document.Sections["任务"] = "{\"主线\": \"第一章\"}";

            var json = SaveSerializer.ToJson(document);
            var roundTripped = SaveSerializer.FromJson(json);

            Assert.Equal(1, roundTripped.Version);
            Assert.Equal("{\"金币\": 100}", roundTripped.Sections["背包"]);
            Assert.Equal("{\"主线\": \"第一章\"}", roundTripped.Sections["任务"]);
        }

        [Fact]
        public void MigrateV1ToV3AppliesBothStepsInOrder()
        {
            var document = new SaveDocument { Version = 1 };
            var migrations = new List<ISaveMigration>
            {
                new StepMigration(1, "背包", "{\"金币\": 100}"),
                new StepMigration(2, "任务", "{\"主线\": \"第一章\"}"),
            };
            var migrator = new SaveMigrator(3, migrations);

            var result = migrator.Migrate(document);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "1 → 2", "2 → 3" }, result.AppliedSteps);
            Assert.Equal(3, document.Version);
            Assert.Equal("{\"金币\": 100}", document.Sections["背包"]);
            Assert.Equal("{\"主线\": \"第一章\"}", document.Sections["任务"]);
        }

        [Fact]
        public void MigrateNewerVersionIsRejected()
        {
            var document = new SaveDocument { Version = 9 };
            var migrator = new SaveMigrator(3, new List<ISaveMigration>());

            var result = migrator.Migrate(document);

            Assert.False(result.IsSuccess);
            Assert.Contains("拒绝", result.Message);
        }

        [Fact]
        public void MigrateMissingIntermediateStepFailsWithMissingLevel()
        {
            var document = new SaveDocument { Version = 1 };
            var migrations = new List<ISaveMigration>
            {
                new StepMigration(1, "背包", "{\"金币\": 100}"),
            };
            var migrator = new SaveMigrator(3, migrations);

            var result = migrator.Migrate(document);

            Assert.False(result.IsSuccess);
            Assert.Contains("版本 2", result.Message);
        }

        [Fact]
        public void MigrateSameVersionPassesThroughWithoutSteps()
        {
            var document = new SaveDocument { Version = 3 };
            document.Sections["背包"] = "{\"金币\": 100}";
            var migrator = new SaveMigrator(3, new List<ISaveMigration>());

            var result = migrator.Migrate(document);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.AppliedSteps);
            Assert.Equal(3, document.Version);
        }

        private sealed class StepMigration : ISaveMigration
        {
            private readonly string _sectionKey;
            private readonly string _sectionValue;

            public int FromVersion { get; }

            public int ToVersion { get; }

            public StepMigration(int fromVersion, string sectionKey, string sectionValue)
            {
                FromVersion = fromVersion;
                ToVersion = fromVersion + 1;
                _sectionKey = sectionKey;
                _sectionValue = sectionValue;
            }

            public void Apply(SaveDocument document) => document.Sections[_sectionKey] = _sectionValue;
        }
    }
}
