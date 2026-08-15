using System;
using System.IO;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>config.query 命令的测试：按主键从镜像里取出一行并输出该行 JSON。</summary>
    public class ConfigQueryCommandTests
    {
        private const string SingleKeySchema = @"
{
  ""tableName"": ""背包"",
  ""tableIdentifierName"": ""Bag"",
  ""sheetName"": ""道具"",
  ""fields"": [
    { ""displayName"": ""编号"", ""identifierName"": ""ItemId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""名称"", ""identifierName"": ""ItemName"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";

        private const string SingleKeyMirror = @"
{
  ""tableName"": ""背包"",
  ""rows"": [
    { ""ItemId"": 1001, ""ItemName"": ""治疗药水"" },
    { ""ItemId"": 1002, ""ItemName"": ""皮甲"" }
  ]
}";

        private const string CompositeKeySchema = @"
{
  ""tableName"": ""怪物"",
  ""tableIdentifierName"": ""Monster"",
  ""sheetName"": ""怪物"",
  ""fields"": [
    { ""displayName"": ""关卡编号"", ""identifierName"": ""LevelId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""怪物编号"", ""identifierName"": ""MonsterId"", ""typeName"": ""Int32"", ""isPrimaryKey"": true },
    { ""displayName"": ""怪物名称"", ""identifierName"": ""MonsterName"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";

        private const string CompositeKeyMirror = @"
{
  ""tableName"": ""怪物"",
  ""rows"": [
    { ""LevelId"": 3, ""MonsterId"": 5, ""MonsterName"": ""石像鬼"" },
    { ""LevelId"": 3, ""MonsterId"": 6, ""MonsterName"": ""毒蛛"" }
  ]
}";

        private const string StringKeySchema = @"
{
  ""tableName"": ""物品标签"",
  ""tableIdentifierName"": ""ItemTag"",
  ""sheetName"": ""标签"",
  ""fields"": [
    { ""displayName"": ""标签键"", ""identifierName"": ""TagKey"", ""typeName"": ""String"", ""isPrimaryKey"": true },
    { ""displayName"": ""说明"", ""identifierName"": ""Description"", ""typeName"": ""String"", ""isPrimaryKey"": false }
  ]
}";

        private const string StringKeyMirror = @"
{
  ""tableName"": ""物品标签"",
  ""rows"": [
    { ""TagKey"": ""weapon"", ""Description"": ""武器"" },
    { ""TagKey"": ""armor"", ""Description"": ""护甲"" }
  ]
}";

        [Fact]
        public void QueryHitsSinglePrimaryKeyAndReturnsRowJson()
        {
            var root = CreateConfigRoot("背包", SingleKeySchema, SingleKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("背包", "1001", root));

                Assert.True(result.IsSuccess);
                var rowJson = Assert.Single(result.OutputLines);
                Assert.Contains("治疗药水", rowJson);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryMissReportsRowCount()
        {
            var root = CreateConfigRoot("背包", SingleKeySchema, SingleKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("背包", "9999", root));

                Assert.False(result.IsSuccess);
                Assert.Contains("行数：2", result.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryHitsCompositePrimaryKey()
        {
            var root = CreateConfigRoot("怪物", CompositeKeySchema, CompositeKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("怪物", "3|5", root));

                Assert.True(result.IsSuccess);
                var rowJson = Assert.Single(result.OutputLines);
                Assert.Contains("石像鬼", rowJson);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryFailsWhenCompositeKeyHasTooFewSegments()
        {
            var root = CreateConfigRoot("怪物", CompositeKeySchema, CompositeKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("怪物", "3", root));

                Assert.False(result.IsSuccess);
                Assert.Contains("期望 2 段", result.Message);
                Assert.Contains("实际 1 段", result.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryFailsWhenCompositeKeyHasTooManySegments()
        {
            var root = CreateConfigRoot("怪物", CompositeKeySchema, CompositeKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("怪物", "3|5|7", root));

                Assert.False(result.IsSuccess);
                Assert.Contains("期望 2 段", result.Message);
                Assert.Contains("实际 3 段", result.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryHitsStringPrimaryKey()
        {
            var root = CreateConfigRoot("物品标签", StringKeySchema, StringKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("物品标签", "weapon", root));

                Assert.True(result.IsSuccess);
                var rowJson = Assert.Single(result.OutputLines);
                Assert.Contains("武器", rowJson);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryFailsWhenTableNameMissing()
        {
            var root = CreateConfigRoot("背包", SingleKeySchema, SingleKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments(string.Empty, "1001", root));

                Assert.False(result.IsSuccess);
                Assert.Contains("TableName", result.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void QueryFailsWhenPrimaryKeyMissing()
        {
            var root = CreateConfigRoot("背包", SingleKeySchema, SingleKeyMirror);
            try
            {
                var result = ConfigCommands.Query(Arguments("背包", string.Empty, root));

                Assert.False(result.IsSuccess);
                Assert.Contains("PrimaryKey", result.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static ConfigQueryArguments Arguments(string tableName, string primaryKey, string configRoot)
        {
            return new ConfigQueryArguments
            {
                TableName = tableName,
                PrimaryKey = primaryKey,
                ConfigRoot = configRoot
            };
        }

        private static string CreateConfigRoot(string tableName, string schemaJson, string mirrorJson)
        {
            var root = Path.Combine(Path.GetTempPath(), "config-query-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Schema"));
            Directory.CreateDirectory(Path.Combine(root, "Mirror"));
            File.WriteAllText(Path.Combine(root, "Schema", tableName + ".schema.json"), schemaJson);
            File.WriteAllText(Path.Combine(root, "Mirror", tableName + ".json"), mirrorJson);
            return root;
        }
    }
}
