using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>把镜像内存模型回写进 Excel：打开既有工作簿改单元格另存，格式与厂商扩展得以保留。</summary>
    public static class ExcelTableWriter
    {
        /// <summary>按 schema 把镜像行写入工作簿；工作簿不存在时新建并写中文表头。</summary>
        public static void Write(string workbookPath, TableSchema schema, MirrorDocument mirror)
        {
            var isNewWorkbook = !File.Exists(workbookPath);
            using var workbook = isNewWorkbook ? new XLWorkbook() : new XLWorkbook(workbookPath);

            var worksheet = GetOrCreateWorksheet(workbook, schema);

            if (isNewWorkbook)
            {
                WriteHeader(worksheet, schema);
            }

            WriteRows(worksheet, schema, mirror);
            ClearExtraRows(worksheet, schema, mirror.Rows.Count);

            workbook.SaveAs(workbookPath);
        }

        private static IXLWorksheet GetOrCreateWorksheet(XLWorkbook workbook, TableSchema schema)
        {
            return workbook.TryGetWorksheet(schema.SheetName, out var existing)
                ? existing
                : workbook.AddWorksheet(schema.SheetName);
        }

        private static void WriteHeader(IXLWorksheet worksheet, TableSchema schema)
        {
            for (var fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
            {
                worksheet.Cell(1, fieldIndex + 1).Value = schema.Fields[fieldIndex].DisplayName;
            }
        }

        private static void WriteRows(IXLWorksheet worksheet, TableSchema schema, MirrorDocument mirror)
        {
            for (var rowIndex = 0; rowIndex < mirror.Rows.Count; rowIndex++)
            {
                var row = mirror.Rows[rowIndex];
                for (var fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
                {
                    var field = schema.Fields[fieldIndex];
                    var cell = worksheet.Cell(rowIndex + 2, fieldIndex + 1);
                    if (row.TryGetValue(field.IdentifierName, out var value))
                    {
                        SetCellValue(cell, value, field.TypeName);
                    }
                    else
                    {
                        cell.Clear();
                    }
                }
            }
        }

        private static void SetCellValue(IXLCell cell, object value, string typeName)
        {
            switch (typeName)
            {
                case "Int32":
                    cell.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    break;
                case "Int64":
                    cell.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    break;
                case "Single":
                    cell.Value = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    break;
                case "Boolean":
                    cell.Value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case "String":
                    cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    break;
                default:
                    throw new NotSupportedException($"不支持的类型名：{typeName}");
            }
        }

        private static void ClearExtraRows(IXLWorksheet worksheet, TableSchema schema, int mirrorRowCount)
        {
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

            // 表头占第 1 行，数据从第 2 行开始；镜像更短时把多余的数据行清空。
            var firstExtraRow = mirrorRowCount + 2;
            for (var row = firstExtraRow; row <= lastRow; row++)
            {
                for (var fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
                {
                    worksheet.Cell(row, fieldIndex + 1).Clear();
                }
            }
        }
    }
}
