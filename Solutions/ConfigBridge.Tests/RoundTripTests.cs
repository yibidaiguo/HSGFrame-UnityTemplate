using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Template.Toolkit.ConfigBridge;
using Xunit;

namespace Template.Toolkit.ConfigBridge.Tests
{
    public class RoundTripTests
    {
        private const string SchemaJson = """
        {
          "tableName": "背包",
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
        public void RoundTripSyncsEditedMirrorBackToWorkbook()
        {
            using var fixture = new TestConfigFixture();

            var sync = fixture.Service.Sync("背包");
            Assert.True(sync.IsSuccess, sync.Message);

            var mirror = fixture.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 999;
            mirror.SaveToFile(fixture.MirrorPath);

            var apply = fixture.Service.Apply("背包");
            Assert.True(apply.IsSuccess, apply.Message);

            var resync = fixture.Service.Sync("背包");
            Assert.True(resync.IsSuccess, resync.Message);

            var reread = fixture.LoadMirror();
            Assert.Equal(999, (int)reread.Rows[0]["SellPrice"]);
        }

        [Fact]
        public void ApplyKeepsHeaderBold()
        {
            using var fixture = new TestConfigFixture();

            fixture.Service.Sync("背包");

            var mirror = fixture.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 777;
            mirror.SaveToFile(fixture.MirrorPath);

            var apply = fixture.Service.Apply("背包");
            Assert.True(apply.IsSuccess, apply.Message);

            using var workbook = new XLWorkbook(fixture.WorkbookPath);
            var worksheet = workbook.Worksheet("道具");
            Assert.True(worksheet.Cell(1, 1).Style.Font.Bold);
        }

        [Fact]
        public void ApplyRefusesWhenBaselineMismatchesAndWritesNothing()
        {
            using var fixture = new TestConfigFixture();

            fixture.Service.Sync("背包");

            var mirror = fixture.LoadMirror();
            mirror.Rows[0]["SellPrice"] = 555;
            mirror.SaveToFile(fixture.MirrorPath);

            TamperWorkbookHash(fixture.BaselinePath);

            var beforeHash = BaselineStore.ComputeFileHash(fixture.WorkbookPath);
            var apply = fixture.Service.Apply("背包");
            var afterHash = BaselineStore.ComputeFileHash(fixture.WorkbookPath);

            Assert.False(apply.IsSuccess);
            Assert.Equal(beforeHash, afterHash);
        }

        [Fact]
        public void ValidateReportsUnknownKeyAndTypeError()
        {
            using var fixture = new TestConfigFixture();

            fixture.Service.Sync("背包");

            var mirror = fixture.LoadMirror();
            mirror.Rows[0]["ExtraField"] = 123;
            mirror.Rows[0]["SellPrice"] = "not-a-number";
            mirror.SaveToFile(fixture.MirrorPath);

            var result = fixture.Service.Validate("背包");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Details, detail => detail.Contains("schema 之外"));
            Assert.Contains(result.Details, detail => detail.Contains("类型转换失败"));
        }

        [Fact]
        public void ValidateReportsDuplicatePrimaryKey()
        {
            using var fixture = new TestConfigFixture();

            fixture.Service.Sync("背包");

            var mirror = fixture.LoadMirror();
            mirror.Rows[1]["ItemId"] = mirror.Rows[0]["ItemId"];
            mirror.SaveToFile(fixture.MirrorPath);

            var result = fixture.Service.Validate("背包");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Details, detail => detail.Contains("主键重复"));
        }

        private static void TamperWorkbookHash(string baselinePath)
        {
            var root = JsonNode.Parse(File.ReadAllText(baselinePath));
            root["tables"]["背包"]["workbookHash"] = new string('0', 64);
            File.WriteAllText(baselinePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed class TestConfigFixture : IDisposable
        {
            public TestConfigFixture()
            {
                ConfigRoot = Path.Combine(Path.GetTempPath(), "ConfigBridgeTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(ConfigRoot, "Schema"));
                Directory.CreateDirectory(Path.Combine(ConfigRoot, "Tables"));
                Directory.CreateDirectory(Path.Combine(ConfigRoot, "Mirror"));

                File.WriteAllText(SchemaPath, SchemaJson);
                CreateWorkbook(WorkbookPath);

                Service = new ConfigSyncService(ConfigRoot);
            }

            public string ConfigRoot { get; }

            public ConfigSyncService Service { get; }

            public string SchemaPath => Path.Combine(ConfigRoot, "Schema", "背包.schema.json");

            public string WorkbookPath => Path.Combine(ConfigRoot, "Tables", "背包.xlsx");

            public string MirrorPath => Path.Combine(ConfigRoot, "Mirror", "背包.json");

            public string BaselinePath => Path.Combine(ConfigRoot, "Mirror", ".baseline.json");

            public MirrorDocument LoadMirror()
            {
                var mirror = MirrorDocument.LoadFromFile(MirrorPath);
                mirror.NormalizeValues(SchemaLoader.LoadFromFile(SchemaPath));
                return mirror;
            }

            public void Dispose()
            {
                if (Directory.Exists(ConfigRoot))
                {
                    Directory.Delete(ConfigRoot, recursive: true);
                }
            }

            private static void CreateWorkbook(string workbookPath)
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.AddWorksheet("道具");

                var headers = new[] { "编号", "名称", "堆叠上限", "售价" };
                for (var column = 0; column < headers.Length; column++)
                {
                    var cell = worksheet.Cell(1, column + 1);
                    cell.Value = headers[column];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#ADD8E6");
                }

                WriteRow(worksheet, 2, 1001, "木剑", 1, 25);
                WriteRow(worksheet, 3, 1002, "治疗药水", 99, 10);
                WriteRow(worksheet, 4, 1003, "皮甲", 1, 60);

                workbook.SaveAs(workbookPath);
            }

            private static void WriteRow(IXLWorksheet worksheet, int row, int itemId, string itemName, int stackLimit, int sellPrice)
            {
                worksheet.Cell(row, 1).Value = itemId;
                worksheet.Cell(row, 2).Value = itemName;
                worksheet.Cell(row, 3).Value = stackLimit;
                worksheet.Cell(row, 4).Value = sellPrice;
            }
        }
    }
}
