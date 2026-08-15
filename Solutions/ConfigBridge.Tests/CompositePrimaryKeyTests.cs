using System;
using System.Linq;
using ClosedXML.Excel;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>两列复合主键的校验：组合唯一、组合重复、单列重复、主键为空与往返顺序。</summary>
    public class CompositePrimaryKeyTests
    {
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
        public void ValidatePassesWhenCompositeKeyIsUniqueEvenWithRepeatedFirstColumn()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 6));

            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            var result = builder.Service.Validate("怪物");

            Assert.True(result.IsSuccess, result.Message);
        }

        [Fact]
        public void ValidateFailsWithRowNumberWhenCompositeKeyDuplicates()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 6));

            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[1]["LevelId"] = mirror.Rows[0]["LevelId"];
            mirror.Rows[1]["MonsterId"] = mirror.Rows[0]["MonsterId"];
            mirror.SaveToFile(builder.MirrorPath);

            var result = builder.Service.Validate("怪物");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Details, detail => detail.Contains("第 2 行") && detail.Contains("主键重复"));
        }

        [Fact]
        public void ValidatePassesWhenOnlyFirstColumnRepeats()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 3));

            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            // 前三行第一列全相同、第二列各不相同，两列合起来仍唯一。
            var mirror = builder.LoadMirror();
            Assert.Equal(mirror.Rows[0]["LevelId"], mirror.Rows[1]["LevelId"]);
            Assert.Equal(mirror.Rows[1]["LevelId"], mirror.Rows[2]["LevelId"]);

            var result = builder.Service.Validate("怪物");

            Assert.True(result.IsSuccess, result.Message);
        }

        [Fact]
        public void ValidateFailsWhenPrimaryKeyColumnIsEmpty()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 6));

            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["MonsterId"] = null;
            mirror.SaveToFile(builder.MirrorPath);

            var result = builder.Service.Validate("怪物");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Details, detail => detail.Contains("主键为空"));
        }

        [Fact]
        public void RoundTripPreservesRowOrderForCompositeKeyTable()
        {
            using var builder = NewMonsterBuilder();
            builder.WriteWorkbook(worksheet => WriteMonsterRows(worksheet, 6));

            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            var before = builder.LoadMirror();
            var beforeOrder = before.Rows.Select(row => (int)row["LevelId"] * 1000 + (int)row["MonsterId"]).ToList();

            before.Rows[0]["HealthPoint"] = 99999;
            before.SaveToFile(builder.MirrorPath);
            Assert.True(builder.Service.Apply("怪物").IsSuccess);
            Assert.True(builder.Service.Sync("怪物").IsSuccess);

            var after = builder.LoadMirror();
            var afterOrder = after.Rows.Select(row => (int)row["LevelId"] * 1000 + (int)row["MonsterId"]).ToList();

            Assert.Equal(beforeOrder, afterOrder);
        }

        private static ConfigTestWorkbookBuilder NewMonsterBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("怪物", "怪物");
            builder.WriteSchema(MonsterSchemaJson);
            return builder;
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
                worksheet.Cell(excelRow, 1).Value = 1 + (row / 3);
                worksheet.Cell(excelRow, 2).Value = 1 + (row % 3);
                worksheet.Cell(excelRow, 3).Value = $"怪物{row + 1}";
                worksheet.Cell(excelRow, 4).Value = 100 + row;
                worksheet.Cell(excelRow, 5).Value = row % 2 == 0;
            }
        }
    }
}
