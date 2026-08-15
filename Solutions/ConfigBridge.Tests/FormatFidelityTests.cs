using System;
using ClosedXML.Excel;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>格式保真回归：Sync → 改镜像 → Apply 回写后，表头格式、批注、冻结与附加 Sheet 公式原样保留。</summary>
    public class FormatFidelityTests
    {
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

        private const string HeaderCommentText = "回归观测点";

        [Fact]
        public void ApplyPreservesHeaderBold()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            var worksheet = workbook.Worksheet("技能");
            Assert.True(worksheet.Cell(1, 1).Style.Font.Bold);
        }

        [Fact]
        public void ApplyPreservesHeaderFillColorExactly()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            var worksheet = workbook.Worksheet("技能");

            // 断言精确 ARGB 值，而不是只断言「有填充」——填充还在但颜色变了是这类回归最典型的假通过。
            Assert.Equal(unchecked((int)0xFFADD8E6), worksheet.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb());
        }

        [Fact]
        public void ApplyPreservesHeaderCommentText()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            var worksheet = workbook.Worksheet("技能");
            Assert.Equal(HeaderCommentText, worksheet.Cell(1, 1).GetComment().Text);
        }

        [Fact]
        public void ApplyPreservesFrozenFirstRow()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            var worksheet = workbook.Worksheet("技能");
            Assert.Equal(1, worksheet.SheetView.SplitRow);
        }

        [Fact]
        public void ApplyPreservesStatisticsSheetFormulas()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            var statistics = workbook.Worksheet("统计");
            Assert.Equal("COUNTA(技能!A2:A100000)", statistics.Cell(1, 2).FormulaA1);
            Assert.Equal("AVERAGE(技能!D2:D100000)", statistics.Cell(2, 2).FormulaA1);
        }

        [Fact]
        public void ApplyPreservesSheetCountAndOrder()
        {
            using var builder = NewSkillWorkbook();
            ApplyOneEditAndReopen(builder);

            using var workbook = new XLWorkbook(builder.TablePath);
            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Equal("技能", workbook.Worksheet(1).Name);
            Assert.Equal("统计", workbook.Worksheet(2).Name);
        }

        private static ConfigTestWorkbookBuilder NewSkillWorkbook()
        {
            var builder = new ConfigTestWorkbookBuilder("技能", "技能");
            builder.WriteSchema(SkillSchemaJson);
            builder.WriteWorkbook(WriteSkillSheet, WriteStatisticsSheet);
            return builder;
        }

        private static void WriteSkillSheet(IXLWorksheet worksheet)
        {
            var headers = new[] { "编号", "全局编号", "技能名称", "冷却秒数", "是否被动" };
            for (var column = 0; column < headers.Length; column++)
            {
                var cell = worksheet.Cell(1, column + 1);
                cell.Value = headers[column];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#ADD8E6");
                cell.GetComment().AddText(HeaderCommentText);
            }

            worksheet.SheetView.FreezeRows(1);

            for (var row = 0; row < 3; row++)
            {
                var excelRow = row + 2;
                worksheet.Cell(excelRow, 1).Value = 2001 + row;
                worksheet.Cell(excelRow, 2).Value = 900000000000L + row;
                worksheet.Cell(excelRow, 3).Value = $"技能{row + 1}";
                worksheet.Cell(excelRow, 4).Value = 1.5f + row;
                worksheet.Cell(excelRow, 5).Value = row % 2 == 0;
            }
        }

        private static void WriteStatisticsSheet(XLWorkbook workbook)
        {
            var statistics = workbook.AddWorksheet("统计");
            statistics.Cell(1, 2).FormulaA1 = "COUNTA(技能!A2:A100000)";
            statistics.Cell(2, 2).FormulaA1 = "AVERAGE(技能!D2:D100000)";
        }

        private static void ApplyOneEditAndReopen(ConfigTestWorkbookBuilder builder)
        {
            var sync = builder.Service.Sync("技能");
            Assert.True(sync.IsSuccess, sync.Message);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["SkillName"] = "改过的技能名";
            mirror.SaveToFile(builder.MirrorPath);

            var apply = builder.Service.Apply("技能");
            Assert.True(apply.IsSuccess, apply.Message);
        }
    }
}
