using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>配置桥接操作的结果：是否成功、给人看的一句话消息与逐条明细。</summary>
    public sealed class ConfigOperationResult
    {
        /// <summary>是否执行成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>面向人的结果消息。</summary>
        public string Message { get; }

        /// <summary>逐条明细，永远非 null。</summary>
        public IReadOnlyList<string> Details { get; }

        private ConfigOperationResult(bool isSuccess, string message, IReadOnlyList<string> details)
        {
            IsSuccess = isSuccess;
            Message = message;
            Details = details ?? Array.Empty<string>();
        }

        /// <summary>构造成功结果。</summary>
        public static ConfigOperationResult Success(string message, IReadOnlyList<string> details = null)
        {
            return new ConfigOperationResult(true, message, details);
        }

        /// <summary>构造失败结果。</summary>
        public static ConfigOperationResult Failure(string message, IReadOnlyList<string> details = null)
        {
            return new ConfigOperationResult(false, message, details);
        }
    }

    /// <summary>配置表桥接服务：按配置根目录约定路径，驱动 sync / apply / validate 三个流程。</summary>
    public sealed class ConfigSyncService
    {
        private readonly string _configRoot;

        /// <summary>用配置根目录构造服务；相对路径以模板根目录为基准。</summary>
        public ConfigSyncService(string configRoot = "Config")
        {
            _configRoot = configRoot;
        }

        /// <summary>把 Excel 同步成镜像 JSON（Excel 为准），成功后更新基线。</summary>
        public ConfigOperationResult Sync(string tableName)
        {
            try
            {
                var schema = SchemaLoader.LoadFromFile(SchemaPath(tableName));
                var workbookPath = WorkbookPath(tableName);
                var mirrorPath = MirrorPath(tableName);

                var mirror = ExcelTableReader.Read(workbookPath, schema);
                Directory.CreateDirectory(Path.GetDirectoryName(mirrorPath));
                mirror.SaveToFile(mirrorPath);

                var baselinePath = BaselinePath();
                var baseline = BaselineStore.Load(baselinePath);
                baseline.Update(tableName, workbookPath, mirrorPath);
                baseline.Save(baselinePath);

                return ConfigOperationResult.Success(
                    $"已同步表「{tableName}」：Excel → 镜像完成",
                    new[] { $"镜像行数：{mirror.Rows.Count}" });
            }
            catch (Exception exception)
            {
                return ConfigOperationResult.Failure($"同步表「{tableName}」失败：{exception.Message}");
            }
        }

        /// <summary>把镜像 JSON 回写进 Excel；写之前先做占用检测与基线哈希校验，任一不过就一个字节都不写。</summary>
        public ConfigOperationResult Apply(string tableName)
        {
            try
            {
                var schema = SchemaLoader.LoadFromFile(SchemaPath(tableName));
                var workbookPath = WorkbookPath(tableName);
                var mirrorPath = MirrorPath(tableName);
                var baselinePath = BaselinePath();

                // 占用检测必须放在基线哈希之前：文件被独占时连读哈希都会被锁卡住，
                // 先把「被占用」这个更靠前的失败拦下，才不会漏成裸异常。
                if (!TryOpenWorkbookExclusively(workbookPath, out var busyReason))
                {
                    return ConfigOperationResult.Failure(busyReason);
                }

                var baseline = BaselineStore.Load(baselinePath);
                if (!baseline.IsWorkbookInSync(tableName, workbookPath, out var reason))
                {
                    return ConfigOperationResult.Failure(DescribeWorkbookOutOfSync(workbookPath, baselinePath, reason));
                }

                var mirror = MirrorDocument.LoadFromFile(mirrorPath);
                mirror.NormalizeValues(schema);
                ExcelTableWriter.Write(workbookPath, schema, mirror);

                baseline.Update(tableName, workbookPath, mirrorPath);
                baseline.Save(baselinePath);

                return ConfigOperationResult.Success(
                    $"已应用表「{tableName}」：镜像 → Excel 完成",
                    new[] { $"镜像行数：{mirror.Rows.Count}" });
            }
            catch (Exception exception)
            {
                return ConfigOperationResult.Failure($"应用表「{tableName}」失败：{exception.Message}");
            }
        }

        /// <summary>逐行逐字段对照 schema 校验镜像，把发现的问题全部收进 Details。</summary>
        public ConfigOperationResult Validate(string tableName)
        {
            try
            {
                var schema = SchemaLoader.LoadFromFile(SchemaPath(tableName));
                var mirror = MirrorDocument.LoadFromFile(MirrorPath(tableName));

                var details = new List<string>();
                var primaryKeyFields = schema.Fields.Where(field => field.IsPrimaryKey).ToList();
                var seenPrimaryKeys = new HashSet<string>();

                for (var rowIndex = 0; rowIndex < mirror.Rows.Count; rowIndex++)
                {
                    var row = mirror.Rows[rowIndex];

                    foreach (var field in schema.Fields)
                    {
                        if (!row.TryGetValue(field.IdentifierName, out var value))
                        {
                            details.Add($"第 {rowIndex + 1} 行缺少字段「{field.IdentifierName}」");
                            continue;
                        }

                        try
                        {
                            MirrorDocument.ConvertValue(value, field.TypeName);
                        }
                        catch (Exception exception)
                        {
                            details.Add($"第 {rowIndex + 1} 行字段「{field.IdentifierName}」类型转换失败：{exception.Message}");
                        }
                    }

                    foreach (var key in row.Keys)
                    {
                        if (schema.FindByIdentifierName(key) == null)
                        {
                            details.Add($"第 {rowIndex + 1} 行出现 schema 之外的键「{key}」");
                        }
                    }

                    var hasEmptyPrimaryKey = primaryKeyFields.Any(field =>
                        !row.TryGetValue(field.IdentifierName, out var value) || IsNullOrEmptyValue(value));
                    if (hasEmptyPrimaryKey)
                    {
                        details.Add($"第 {rowIndex + 1} 行主键为空");
                        continue;
                    }

                    var primaryKeyText = string.Join("|", primaryKeyFields.Select(field =>
                        Convert.ToString(row[field.IdentifierName], CultureInfo.InvariantCulture)));
                    if (!seenPrimaryKeys.Add(primaryKeyText))
                    {
                        details.Add($"第 {rowIndex + 1} 行主键重复：{primaryKeyText}");
                    }
                }

                if (details.Count == 0)
                {
                    return ConfigOperationResult.Success(
                        $"表「{tableName}」校验通过",
                        new[] { $"行数：{mirror.Rows.Count}" });
                }

                return ConfigOperationResult.Failure(
                    $"表「{tableName}」校验失败，共 {details.Count} 处问题",
                    details);
            }
            catch (Exception exception)
            {
                return ConfigOperationResult.Failure($"校验表「{tableName}」失败：{exception.Message}");
            }
        }

        private string SchemaPath(string tableName)
        {
            return Path.Combine(_configRoot, "Schema", tableName + ".schema.json");
        }

        private string WorkbookPath(string tableName)
        {
            return Path.Combine(_configRoot, "Tables", tableName + ".xlsx");
        }

        private string MirrorPath(string tableName)
        {
            return Path.Combine(_configRoot, "Mirror", tableName + ".json");
        }

        private string BaselinePath()
        {
            return Path.Combine(_configRoot, "Mirror", ".baseline.json");
        }

        /// <summary>以独占读写方式打开 xlsx 一次并立刻释放，探测它是否正被其他进程占用。</summary>
        private static bool TryOpenWorkbookExclusively(string workbookPath, out string busyReason)
        {
            busyReason = string.Empty;

            // 文件不存在时没有「被占用」可言，后续写入会走新建分支，这里直接放行。
            if (!File.Exists(workbookPath))
            {
                return true;
            }

            try
            {
                using var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                busyReason = "xlsx 正被其他程序占用（文件被占用，多半是 Excel 或 WPS 开着），关掉占用它的程序再重试。";
                return false;
            }
        }

        /// <summary>拼出 xlsx 与基线不一致时的失败消息，带位置、原因、修复动作与参考四要素。</summary>
        private static string DescribeWorkbookOutOfSync(string workbookPath, string baselinePath, string reason)
        {
            if (reason.Contains("没有记录", StringComparison.Ordinal))
            {
                return $"{reason}。位置：{workbookPath}。先跑一次 config.sync 建立基线，再重做你的编辑。参考：{baselinePath}。";
            }

            return $"xlsx 在上次同步之后被改过，直接回写会覆盖掉别人的改动。位置：{workbookPath}。" +
                   $"先跑一次 config.sync 把 Excel 的改动收进镜像，再重做你的编辑。参考：{baselinePath}。细节：{reason}。";
        }

        private static bool IsNullOrEmptyValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    return true;
                }

                return element.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(element.GetString());
            }

            return value is string text && string.IsNullOrWhiteSpace(text);
        }
    }
}
