using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>五种字段类型（Int32 / Int64 / Single / Boolean / String）的取值、精度与校验报错覆盖。</summary>
    public class TypeCoverageTests
    {
        private const string TypeSchemaJson = """
        {
          "tableName": "类型表",
          "tableIdentifierName": "TypeTable",
          "sheetName": "类型",
          "fields": [
            { "displayName": "编号", "identifierName": "Id",       "typeName": "Int32",   "isPrimaryKey": true },
            { "displayName": "大整数", "identifierName": "BigValue", "typeName": "Int64",   "isPrimaryKey": false },
            { "displayName": "小数",  "identifierName": "Fraction", "typeName": "Single",  "isPrimaryKey": false },
            { "displayName": "开关",  "identifierName": "Flag",     "typeName": "Boolean", "isPrimaryKey": false },
            { "displayName": "文本",  "identifierName": "Text",     "typeName": "String",  "isPrimaryKey": false }
          ]
        }
        """;

        [Fact]
        public void SyncWritesFiveCorrectJsonValueKinds()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);

            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var root = JsonNode.Parse(File.ReadAllText(builder.MirrorPath));
            var row = root["rows"][0];
            Assert.Equal(JsonValueKind.Number, row["Id"].GetValueKind());
            Assert.Equal(JsonValueKind.Number, row["BigValue"].GetValueKind());
            Assert.Equal(JsonValueKind.Number, row["Fraction"].GetValueKind());
            Assert.Equal(JsonValueKind.True, row["Flag"].GetValueKind());
            Assert.Equal(JsonValueKind.String, row["Text"].GetValueKind());
        }

        [Fact]
        public void Int64ValueBeyondIntMaxSurvivesRoundTrip()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            Assert.Equal(900000000000L, (long)mirror.Rows[0]["BigValue"]);

            mirror.Rows[0]["BigValue"] = 900000000000L;
            mirror.SaveToFile(builder.MirrorPath);
            Assert.True(builder.Service.Apply("类型表").IsSuccess);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var after = builder.LoadMirror();
            Assert.Equal(900000000000L, (long)after.Rows[0]["BigValue"]);
        }

        [Fact]
        public void SingleFractionSurvivesRoundTrip()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            Assert.Equal(9.75f, (float)mirror.Rows[0]["Fraction"]);

            mirror.Rows[0]["Fraction"] = 9.75f;
            mirror.SaveToFile(builder.MirrorPath);
            Assert.True(builder.Service.Apply("类型表").IsSuccess);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var after = builder.LoadMirror();
            Assert.Equal(9.75f, (float)after.Rows[0]["Fraction"]);
        }

        [Fact]
        public void BooleanTrueAndFalseSurviveRoundTrip()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            Assert.True((bool)mirror.Rows[0]["Flag"]);
            Assert.False((bool)mirror.Rows[1]["Flag"]);
        }

        [Fact]
        public void StringWithChineseAndMiddleDotSurvivesRoundTrip()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            Assert.Equal("烈焰斩·改", (string)mirror.Rows[0]["Text"]);

            mirror.Rows[0]["Text"] = "烈焰斩·改";
            mirror.SaveToFile(builder.MirrorPath);
            Assert.True(builder.Service.Apply("类型表").IsSuccess);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var after = builder.LoadMirror();
            Assert.Equal("烈焰斩·改", (string)after.Rows[0]["Text"]);
        }

        [Fact]
        public void EmptyStringCellSurvivesAsEmptyStringNotNull()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            var text = mirror.Rows[1]["Text"];
            Assert.NotNull(text);
            Assert.Equal(string.Empty, (string)text);
        }

        [Fact]
        public void ValidateReportsRowAndFieldOnTypeMismatch()
        {
            using var builder = NewTypeBuilder();
            builder.WriteWorkbook(WriteTypeRows);
            Assert.True(builder.Service.Sync("类型表").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["Id"] = "not-a-number";
            mirror.SaveToFile(builder.MirrorPath);

            var result = builder.Service.Validate("类型表");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Details, detail => detail.Contains("第 1 行") && detail.Contains("Id"));
        }

        private static ConfigTestWorkbookBuilder NewTypeBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("类型表", "类型");
            builder.WriteSchema(TypeSchemaJson);
            return builder;
        }

        private static void WriteTypeRows(IXLWorksheet worksheet)
        {
            var headers = new[] { "编号", "大整数", "小数", "开关", "文本" };
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(1, column + 1).Value = headers[column];
            }

            // 第 1 行：各类型的代表性值。
            worksheet.Cell(2, 1).Value = 1;
            worksheet.Cell(2, 2).Value = 900000000000L;
            worksheet.Cell(2, 3).Value = 9.75f;
            worksheet.Cell(2, 4).Value = true;
            worksheet.Cell(2, 5).Value = "烈焰斩·改";

            // 第 2 行：Flag=false，文本列故意留空，验证空字符串往返。
            worksheet.Cell(3, 1).Value = 2;
            worksheet.Cell(3, 2).Value = 1L;
            worksheet.Cell(3, 3).Value = 0.5f;
            worksheet.Cell(3, 4).Value = false;
        }
    }
}
