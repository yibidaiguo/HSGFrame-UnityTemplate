using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>配置表桥接命令的参数。</summary>
    public sealed class ConfigArguments
    {
        /// <summary>要处理的表名。</summary>
        [Summary("要处理的表名")]
        public string TableName { get; set; }

        /// <summary>配置根目录，相对模板根目录（即包含 Config / Tools / Solutions 的目录）。</summary>
        [Summary("配置根目录，相对模板根")]
        [DefaultValue("Config")]
        public string ConfigRoot { get; set; }
    }

    /// <summary>配置表查询命令的参数。</summary>
    public sealed class ConfigQueryArguments
    {
        /// <summary>要查的表名。</summary>
        [Summary("要查的表名")]
        public string TableName { get; set; }

        /// <summary>主键值，多主键表用竖线按 schema 里主键字段的顺序分隔。</summary>
        [Summary("主键值，多主键表用竖线按 schema 里主键字段的顺序分隔")]
        public string PrimaryKey { get; set; }

        /// <summary>配置根目录，相对模板根目录（即包含 Config / Tools / Solutions 的目录）。</summary>
        [Summary("配置根目录，相对模板根")]
        [DefaultValue("Config")]
        public string ConfigRoot { get; set; }
    }

    /// <summary>配置表桥接命令：sync / apply / validate / query 四个流程的 CLI 入口。</summary>
    public static class ConfigCommands
    {
        /// <summary>
        /// 把 Excel 配置表同步到镜像 JSON（Excel 为准）。
        /// </summary>
        /// <param name="arguments">同步参数。</param>
        [EditorCommand("config.sync")]
        [Summary("把 Excel 配置表同步到镜像 JSON（Excel 为准）")]
        public static CommandResult Sync(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Sync(arguments.TableName));
        }

        /// <summary>
        /// 把镜像 JSON 回写到 Excel（先校验基线哈希，不一致拒绝）。
        /// </summary>
        /// <param name="arguments">应用参数。</param>
        [EditorCommand("config.apply")]
        [Summary("把镜像 JSON 回写到 Excel（先校验基线哈希）")]
        public static CommandResult Apply(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Apply(arguments.TableName));
        }

        /// <summary>
        /// 按 schema 逐字段校验镜像 JSON。
        /// </summary>
        /// <param name="arguments">校验参数。</param>
        [EditorCommand("config.validate")]
        [Summary("按 schema 逐字段校验镜像 JSON")]
        public static CommandResult Validate(ConfigArguments arguments)
        {
            return Run(arguments, service => service.Validate(arguments.TableName));
        }

        /// <summary>
        /// 按主键从镜像里取出一行，输出该行的 JSON。
        /// </summary>
        /// <param name="arguments">查询参数。</param>
        [EditorCommand("config.query")]
        [Summary("查某表某条数据：按主键取出一行，输出该行的镜像 JSON")]
        public static CommandResult Query(ConfigQueryArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.TableName))
            {
                return CommandResult.Failure("参数 TableName 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.PrimaryKey))
            {
                return CommandResult.Failure("参数 PrimaryKey 为必填项");
            }

            var configRoot = string.IsNullOrWhiteSpace(arguments.ConfigRoot) ? "Config" : arguments.ConfigRoot;
            var schemaPath = Path.Combine(configRoot, "Schema", arguments.TableName + ".schema.json");
            var mirrorPath = Path.Combine(configRoot, "Mirror", arguments.TableName + ".json");

            TableSchema schema;
            MirrorDocument mirror;
            try
            {
                schema = SchemaLoader.LoadFromFile(schemaPath);
                mirror = MirrorDocument.LoadFromFile(mirrorPath);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"查询表「{arguments.TableName}」失败：{exception.Message}");
            }

            var primaryKeyFields = schema.Fields.Where(field => field.IsPrimaryKey).ToList();
            var segments = arguments.PrimaryKey.Split('|');
            if (segments.Length != primaryKeyFields.Count)
            {
                return CommandResult.Failure(
                    $"主键段数与表的主键字段数对不上：期望 {primaryKeyFields.Count} 段，实际 {segments.Length} 段。" +
                    "位置：PrimaryKey。" +
                    "按 schema 里主键字段的顺序用竖线分隔重写。" +
                    $"参考：{schemaPath}");
            }

            foreach (var row in mirror.Rows)
            {
                var matched = true;
                for (var index = 0; index < primaryKeyFields.Count; index++)
                {
                    var field = primaryKeyFields[index];
                    if (!row.TryGetValue(field.IdentifierName, out var value)
                        || !string.Equals(
                            Convert.ToString(value, CultureInfo.InvariantCulture),
                            segments[index],
                            StringComparison.Ordinal))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    var rowJson = JsonSerializer.Serialize(row, new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    return CommandResult.Success(
                        $"表「{arguments.TableName}」命中主键 {arguments.PrimaryKey}",
                        new[] { rowJson });
                }
            }

            return CommandResult.Failure(
                $"表「{arguments.TableName}」里没有这个主键 {arguments.PrimaryKey}。" +
                "位置：PrimaryKey。" +
                "先跑 config.sync 确认镜像是最新的，或换一个存在的主键。" +
                $"参考：{mirrorPath}。行数：{mirror.Rows.Count}");
        }

        private static CommandResult Run(ConfigArguments arguments, Func<ConfigSyncService, ConfigOperationResult> operation)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.TableName))
            {
                return CommandResult.Failure("参数 TableName 为必填项");
            }

            var configRoot = string.IsNullOrWhiteSpace(arguments.ConfigRoot)
                ? "Config"
                : arguments.ConfigRoot;
            var service = new ConfigSyncService(configRoot);
            var result = operation(service);

            return result.IsSuccess
                ? CommandResult.Success(result.Message, result.Details)
                : CommandResult.Failure(result.Message, result.Details);
        }
    }
}
