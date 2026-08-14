using System;
using System.Collections.Generic;
using System.Globalization;
using ClosedXML.Excel;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>用 ClosedXML 读取配置表 Excel，转成镜像内存模型。</summary>
    public static class ExcelTableReader
    {
        /// <summary>打开工作簿，按 schema 读指定 Sheet 的表头与数据行，返回镜像。</summary>
        public static MirrorDocument Read(string workbookPath, TableSchema schema)
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.TryGetWorksheet(schema.SheetName, out var worksheet))
            {
                throw new InvalidOperationException(
                    $"工作簿里找不到 Sheet「{schema.SheetName}」（表 {schema.TableName}）");
            }

            var columnByIdentifier = MapHeaderColumns(worksheet, schema);

            var mirror = new MirrorDocument { TableName = schema.TableName };

            // 第 2 行起是数据，读到第一个整行空白为止。
            var rowIndex = 2;
            while (true)
            {
                var row = new Dictionary<string, object>();
                var hasAnyValue = false;

                foreach (var field in schema.Fields)
                {
                    var cell = worksheet.Cell(rowIndex, columnByIdentifier[field.IdentifierName]);
                    if (!cell.IsEmpty())
                    {
                        hasAnyValue = true;
                    }

                    row[field.IdentifierName] = ReadCellValue(cell, field.TypeName);
                }

                if (!hasAnyValue)
                {
                    break;
                }

                mirror.Rows.Add(row);
                rowIndex++;
            }

            return mirror;
        }

        private static Dictionary<string, int> MapHeaderColumns(IXLWorksheet worksheet, TableSchema schema)
        {
            var columnByIdentifier = new Dictionary<string, int>();
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (var column = 1; column <= lastColumn; column++)
            {
                var cell = worksheet.Cell(1, column);
                if (cell.IsEmpty())
                {
                    continue;
                }

                var headerText = cell.GetString().Trim();
                var field = schema.FindByDisplayName(headerText);
                if (field == null)
                {
                    throw new InvalidOperationException(
                        $"表 {schema.TableName} 的表头列「{headerText}」不在 schema 中");
                }

                if (columnByIdentifier.ContainsKey(field.IdentifierName))
                {
                    throw new InvalidOperationException(
                        $"表 {schema.TableName} 的表头列「{headerText}」出现重复");
                }

                columnByIdentifier[field.IdentifierName] = column;
            }

            foreach (var field in schema.Fields)
            {
                if (!columnByIdentifier.ContainsKey(field.IdentifierName))
                {
                    throw new InvalidOperationException(
                        $"表 {schema.TableName} 的表头缺少字段「{field.DisplayName}」");
                }
            }

            return columnByIdentifier;
        }

        private static object ReadCellValue(IXLCell cell, string typeName)
        {
            if (cell.IsEmpty())
            {
                return DefaultValue(typeName);
            }

            switch (typeName)
            {
                case "Int32":
                    return Convert.ToInt32(cell.GetDouble(), CultureInfo.InvariantCulture);
                case "Int64":
                    return Convert.ToInt64(cell.GetDouble(), CultureInfo.InvariantCulture);
                case "Single":
                    return Convert.ToSingle(cell.GetDouble(), CultureInfo.InvariantCulture);
                case "Boolean":
                    return cell.DataType == XLDataType.Boolean
                        ? cell.GetBoolean()
                        : bool.Parse(cell.GetString().Trim());
                case "String":
                    return cell.GetString();
                default:
                    throw new NotSupportedException($"不支持的类型名：{typeName}");
            }
        }

        private static object DefaultValue(string typeName)
        {
            switch (typeName)
            {
                case "Int32":
                    return 0;
                case "Int64":
                    return 0L;
                case "Single":
                    return 0f;
                case "Boolean":
                    return false;
                case "String":
                    return string.Empty;
                default:
                    throw new NotSupportedException($"不支持的类型名：{typeName}");
            }
        }
    }
}
