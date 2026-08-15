using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>并发写防护：xlsx 在同步后被外部改动、或被其他进程占用时，Apply 拒绝且一个字节都不写。</summary>
    public class ConcurrentWriteTests
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

        [Fact]
        public void ApplyRefusesWhenWorkbookChangedAfterSync()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(WriteBackpackRows);
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            EditWorkbookCell(builder.TablePath, "道具");

            var apply = builder.Service.Apply("背包");

            Assert.False(apply.IsSuccess);
            Assert.Contains("同步之后被改过", apply.Message);
        }

        [Fact]
        public void ApplyWritesNothingWhenWorkbookChangedAfterSync()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(WriteBackpackRows);
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            EditWorkbookCell(builder.TablePath, "道具");
            var bytesBeforeApply = File.ReadAllBytes(builder.TablePath);

            var apply = builder.Service.Apply("背包");
            var bytesAfterApply = File.ReadAllBytes(builder.TablePath);

            Assert.False(apply.IsSuccess);
            Assert.Equal(bytesBeforeApply, bytesAfterApply);
        }

        [Fact]
        public void ApplySucceedsWhenWorkbookUntouched()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(WriteBackpackRows);
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 777;
            mirror.SaveToFile(builder.MirrorPath);

            var apply = builder.Service.Apply("背包");

            Assert.True(apply.IsSuccess, apply.Message);
        }

        [Fact]
        public void ApplyRefusesWhenWorkbookIsLockedByAnotherProcess()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(WriteBackpackRows);
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 777;
            mirror.SaveToFile(builder.MirrorPath);

            using var stream = new FileStream(builder.TablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var apply = builder.Service.Apply("背包");

            Assert.False(apply.IsSuccess);
            Assert.Contains("文件被占用", apply.Message);
        }

        [Fact]
        public void ApplySucceedsAgainAfterRefreshingBaselineWithSync()
        {
            using var builder = NewBackpackBuilder();
            builder.WriteWorkbook(WriteBackpackRows);
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            EditWorkbookCell(builder.TablePath, "道具");

            // 重新 Sync 把外部改动收进镜像并刷新基线，之后 Apply 又能走通。
            Assert.True(builder.Service.Sync("背包").IsSuccess);

            var mirror = builder.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 888;
            mirror.SaveToFile(builder.MirrorPath);

            var apply = builder.Service.Apply("背包");

            Assert.True(apply.IsSuccess, apply.Message);
        }

        private static ConfigTestWorkbookBuilder NewBackpackBuilder()
        {
            var builder = new ConfigTestWorkbookBuilder("背包", "道具");
            builder.WriteSchema(BackpackSchemaJson);
            return builder;
        }

        private static void WriteBackpackRows(IXLWorksheet worksheet)
        {
            var headers = new[] { "编号", "名称", "堆叠上限", "售价" };
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(1, column + 1).Value = headers[column];
            }

            for (var row = 0; row < 3; row++)
            {
                var excelRow = row + 2;
                worksheet.Cell(excelRow, 1).Value = 1001 + row;
                worksheet.Cell(excelRow, 2).Value = $"物品{row + 1}";
                worksheet.Cell(excelRow, 3).Value = 1 + row;
                worksheet.Cell(excelRow, 4).Value = 10 + row;
            }
        }

        private static void EditWorkbookCell(string workbookPath, string sheetName)
        {
            using var workbook = new XLWorkbook(workbookPath);
            workbook.Worksheet(sheetName).Cell(2, 1).Value = 999999;
            workbook.SaveAs(workbookPath);
        }
    }
}
