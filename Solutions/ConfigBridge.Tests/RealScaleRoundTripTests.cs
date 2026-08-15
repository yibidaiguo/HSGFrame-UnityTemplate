using System;
using ClosedXML.Excel;
using Template.Toolkit.ConfigBridge;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>真实规模表的往返回归：行数与仓库三张真表同构，验证大表 Sync / Apply 不吞行不多行。</summary>
    public class RealScaleRoundTripTests
    {
        private const string BackpackSchemaJson = """
        {
          "tableName": "背包",
          "tableIdentifierName": "Bag",
          "sheetName": "道具",
          "fields": [
            { "displayName": "编号",   "identifierName": "ItemId",     "typeName": "Int32",  "isPrimaryKey": true },
            { "displayName": "名称",   "identifierName": "ItemName",   "typeName": "String", "isPrimaryKey": false },
            { "displayName": "堆叠上限", "identifierName": "StackLimit", "typeName": "Int32",  "isPrimaryKey": false },
            { "displayName": "售价",   "identifierName": "SellPrice",  "typeName": "Int32",  "isPrimaryKey": false }
          ]
        }
        """;

        private const string SkillSchemaJson = """
        {
          "tableName": "技能",
          "tableIdentifierName": "Skill",
          "sheetName": "技能",
          "fields": [
            { "displayName": "编号",     "identifierName": "SkillId",        "typeName": "Int32",   "isPrimaryKey": true },
            { "displayName": "全局编号", "identifierName": "GlobalSkillId",  "typeName": "Int64",   "isPrimaryKey": false },
            { "displayName": "技能名称", "identifierName": "SkillName",      "typeName": "String",  "isPrimaryKey": false },
            { "displayName": "冷却秒数", "identifierName": "CooldownSeconds", "typeName": "Single", "isPrimaryKey": false },
            { "displayName": "是否被动", "identifierName": "IsPassive",      "typeName": "Boolean", "isPrimaryKey": false }
          ]
        }
        """;

        private const string MonsterSchemaJson = """
        {
          "tableName": "怪物",
          "tableIdentifierName": "Monster",
          "sheetName": "怪物",
          "fields": [
            { "displayName": "关卡编号", "identifierName": "LevelId",     "typeName": "Int32",   "isPrimaryKey": true },
            { "displayName": "怪物编号", "identifierName": "MonsterId",   "typeName": "Int32",   "isPrimaryKey": true },
            { "displayName": "怪物名称", "identifierName": "MonsterName", "typeName": "String",  "isPrimaryKey": false },
            { "displayName": "生命值",   "identifierName": "HealthPoint", "typeName": "Int32",   "isPrimaryKey": false },
            { "displayName": "是否精英", "identifierName": "IsElite",     "typeName": "Boolean", "isPrimaryKey": false }
          ]
        }
        """;

        [Fact]
        public void BackpackSyncYields100Rows()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(worksheet => WriteBackpackRows(worksheet, 100));

            var sync = builder.Service.Sync("背包");

            Assert.True(sync.IsSuccess, sync.Message);
            Assert.Equal(100, builder.LoadMirror().Rows.Count);
        }

        [Fact]
        public void SkillSyncYields120Rows()
        {
            using var builder = NewSkillBuilder();
            builder.WriteWorkbook(worksheet => WriteSkillRows(worksheet, 120));

            var sync = builder.Service.Sync("技能");

            Assert.True(sync.IsSuccess, sync.Message);
            Assert.Equal(120, builder.LoadMirror().Rows.Count);
        }

        [Fact]
        public void MonsterSyncYields120Rows()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 120));

            var sync = builder.Service.Sync("怪物");

            Assert.True(sync.IsSuccess, sync.Message);
            Assert.Equal(120, builder.LoadMirror().Rows.Count);
        }

        [Fact]
        public void BackpackRoundTripEditsOnlyTouchedRows()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(worksheet => WriteBackpackRows(worksheet, 100));

            AssertRoundTripEditsOnlyTouchedRows(builder, "背包", "SellPrice", 99999, 88888, 100);
        }

        [Fact]
        public void SkillRoundTripKeeps120Rows()
        {
            using var builder = NewSkillBuilder();
            builder.WriteWorkbook(worksheet => WriteSkillRows(worksheet, 120));

            AssertRoundTripEditsOnlyTouchedRows(builder, "技能", "SkillName", "烈焰斩·改", "冰霜新星", 120);
        }

        [Fact]
        public void MonsterRoundTripEditsOnlyTouchedRows()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 120));

            AssertRoundTripEditsOnlyTouchedRows(builder, "怪物", "HealthPoint", 55555, 66666, 120);
        }

        private static ConfigTestWorkbookBuilder NewBackpackBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("背包", "道具");
            builder.WriteSchema(BackpackSchemaJson);
            return builder;
        }

        private static ConfigTestWorkbookBuilder NewSkillBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("技能", "技能");
            builder.WriteSchema(SkillSchemaJson);
            return builder;
        }

        private static ConfigTestWorkbookBuilder NewMonsterBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("怪物", "怪物");
            builder.WriteSchema(MonsterSchemaJson);
            return builder;
        }

        /// <summary>改镜像首尾两行后 Apply 再 Sync，断言两处改动读得回来且其余行逐字段不变。</summary>
        private static void AssertRoundTripEditsOnlyTouchedRows(
            ConfigTestWorkbookBuilder builder,
            string tableName,
            string editedField,
            object firstRowValue,
            object lastRowValue,
            int expectedRowCount)
        {
            var sync = builder.Service.Sync(tableName);
            Assert.True(sync.IsSuccess, sync.Message);

            var before = builder.LoadMirror();
            Assert.Equal(expectedRowCount, before.Rows.Count);

            before.Rows[0][editedField] = firstRowValue;
            before.Rows[before.Rows.Count - 1][editedField] = lastRowValue;
            before.SaveToFile(builder.MirrorPath);

            var apply = builder.Service.Apply(tableName);
            Assert.True(apply.IsSuccess, apply.Message);

            var resync = builder.Service.Sync(tableName);
            Assert.True(resync.IsSuccess, resync.Message);

            var after = builder.LoadMirror();
            Assert.Equal(expectedRowCount, after.Rows.Count);
            Assert.Equal(firstRowValue, after.Rows[0][editedField]);
            Assert.Equal(lastRowValue, after.Rows[after.Rows.Count - 1][editedField]);

            AssertMirrorsEqual(before, after);
        }

        private static void AssertMirrorsEqual(MirrorDocument expected, MirrorDocument actual)
        {
            Assert.Equal(expected.Rows.Count, actual.Rows.Count);
            for (var rowIndex = 0; rowIndex < expected.Rows.Count; rowIndex++)
            {
                var expectedRow = expected.Rows[rowIndex];
                var actualRow = actual.Rows[rowIndex];
                Assert.Equal(expectedRow.Count, actualRow.Count);
                foreach (var pair in expectedRow)
                {
                    Assert.Equal(pair.Value, actualRow[pair.Key]);
                }
            }
        }

        private static void WriteBackpackRows(IXLWorksheet worksheet, int rowCount)
        {
            var headers = new[] { "编号", "名称", "堆叠上限", "售价" };
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(1, column + 1).Value = headers[column];
            }

            for (var row = 0; row < rowCount; row++)
            {
                var excelRow = row + 2;
                worksheet.Cell(excelRow, 1).Value = 1001 + row;
                worksheet.Cell(excelRow, 2).Value = $"物品{row + 1}";
                worksheet.Cell(excelRow, 3).Value = 1 + (row % 99);
                worksheet.Cell(excelRow, 4).Value = 10 + row;
            }
        }

        private static void WriteSkillRows(IXLWorksheet worksheet, int rowCount)
        {
            var headers = new[] { "编号", "全局编号", "技能名称", "冷却秒数", "是否被动" };
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(1, column + 1).Value = headers[column];
            }

            for (var row = 0; row < rowCount; row++)
            {
                var excelRow = row + 2;
                worksheet.Cell(excelRow, 1).Value = 2001 + row;
                worksheet.Cell(excelRow, 2).Value = 900000000000L + row;
                worksheet.Cell(excelRow, 3).Value = $"技能{row + 1}";
                worksheet.Cell(excelRow, 4).Value = 1.5f + row;
                worksheet.Cell(excelRow, 5).Value = row % 2 == 0;
            }
        }

        private static void WriteMonsterRows(IXLWorksheet worksheet, int rowCount)
        {
            var headers = new[] { "关卡编号", "怪物编号", "怪物名称", "生命值", "是否精英" };
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(1, column + 1).Value = headers[column];
            }

            for (var row = 0; row < rowCount; row++)
            {
                var excelRow = row + 2;
                worksheet.Cell(excelRow, 1).Value = 1 + (row / 10);
                worksheet.Cell(excelRow, 2).Value = 1 + (row % 10);
                worksheet.Cell(excelRow, 3).Value = $"怪物{row + 1}";
                worksheet.Cell(excelRow, 4).Value = 100 + row;
                worksheet.Cell(excelRow, 5).Value = row % 3 == 0;
            }
        }
    }
}
