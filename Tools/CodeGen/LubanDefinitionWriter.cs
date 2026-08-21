using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.CodeGen
{
    /// <summary>把本仓库的表 schema 翻译成 Luban 的 defines 与 luban.conf。schema 仍是唯一真相，Luban 那套定义是它的投影。</summary>
    /// <remarks>
    /// 这一块最难重新发现的是 Luban 的数据形状约定，写在下面两处注释里，别丢：
    /// 1. 镜像 JSON 是 <c>{"tableName":…,"rows":[…]}</c>，直接喂给 Luban 会被当成一条记录；
    ///    正确做法是 <c>input="*rows@文件名.json"</c>——<c>@</c> 前面是 JSON 字段名，后面是文件名，
    ///    <c>*</c> 是 Luban 的「多记录」标记，两者缺一不可，缺了它 Luban 会把整份对象当一条记录读。
    /// 2. 多主键走 <c>mode="list"</c> + <c>index="字段1+字段2"</c>：<c>+</c> 连接是联合索引，
    ///    生成 <c>Get(字段1, 字段2)</c>；<c>,</c> 连接是互相独立的索引，生成 <c>GetBy字段1</c> / <c>GetBy字段2</c>。
    /// </remarks>
    public static class LubanDefinitionWriter
    {
        // schema 字段类型名到 Luban 类型名的映射。白名单与 SchemaLoader 对齐，五种类型全覆盖。
        private static readonly IReadOnlyDictionary<string, string> LubanTypeNamesByFieldType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Int32"] = "int",
                ["Int64"] = "long",
                ["Single"] = "float",
                ["Boolean"] = "bool",
                ["String"] = "string"
            };

        /// <summary>按全部 schema 写出 defines 与 conf，返回写出的文件路径。</summary>
        /// <param name="templateRoot">模板根目录。</param>
        /// <param name="workingDirectory">Luban 工作目录，defines 与 conf 落在这里。</param>
        public static IReadOnlyList<string> Write(string templateRoot, string workingDirectory)
        {
            var schemaDirectory = Path.Combine(templateRoot, "Config", "Schema");
            if (!Directory.Exists(schemaDirectory))
            {
                return Array.Empty<string>();
            }

            var schemaFiles = Directory.GetFiles(schemaDirectory, "*.schema.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (schemaFiles.Count == 0)
            {
                return Array.Empty<string>();
            }

            var schemas = schemaFiles.Select(SchemaLoader.LoadFromFile).ToList();

            var definesPath = Path.Combine(workingDirectory, "Defines", "defines.xml");
            var configurationPath = Path.Combine(workingDirectory, "luban.conf");

            Directory.CreateDirectory(Path.GetDirectoryName(definesPath));
            File.WriteAllText(definesPath, RenderDefines(schemas));
            File.WriteAllText(configurationPath, RenderConfiguration(templateRoot, workingDirectory));

            return new[] { definesPath, configurationPath };
        }

        /// <summary>由表 schema 推 Luban 表名。Tb 前缀是 Luban 约定，避免与 bean 同名。</summary>
        public static string TableNameOf(TableSchema schema)
        {
            return "Tb" + schema.TableIdentifierName;
        }

        private static string RenderDefines(IReadOnlyList<TableSchema> schemas)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<module name=\"\">");
            foreach (var schema in schemas)
            {
                RenderBean(builder, schema);
                RenderTable(builder, schema);
            }

            builder.AppendLine("</module>");
            return builder.ToString();
        }

        private static void RenderBean(StringBuilder builder, TableSchema schema)
        {
            builder.AppendLine($"  <bean name=\"{schema.TableIdentifierName}\">");
            foreach (var field in schema.Fields)
            {
                builder.AppendLine($"    <var name=\"{field.IdentifierName}\" type=\"{MapTypeName(field.TypeName)}\"/>");
            }

            builder.AppendLine("  </bean>");
        }

        private static void RenderTable(StringBuilder builder, TableSchema schema)
        {
            // 镜像文件名跟**标识名**走（Config/Mirror/Bag.json），不跟展示表名走。
            // 两者从 d2b 批起分开：展示名是给人看的（schema 的 tableName，中文），
            // 文件名要 ASCII（决策 1）。这里写错的表现是 Luban 报
            // 「'TbBag' 的 input 文件或目录不存在」——不是编译错，是生成时才炸。
            var input = $"*rows@{schema.TableIdentifierName}.json";
            var primaryKeys = schema.Fields.Where(field => field.IsPrimaryKey).ToList();

            if (primaryKeys.Count == 1)
            {
                // 单主键走 Luban 默认的 map 模式，index 只写字段名即可。
                builder.AppendLine(
                    $"  <table name=\"{TableNameOf(schema)}\" value=\"{schema.TableIdentifierName}\" input=\"{input}\" index=\"{primaryKeys[0].IdentifierName}\"/>");
            }
            else
            {
                // 多主键用 '+' 连接成联合索引，合起来才唯一；',' 连接是独立索引，语义不对。
                var indexText = string.Join("+", primaryKeys.Select(key => key.IdentifierName));
                builder.AppendLine(
                    $"  <table name=\"{TableNameOf(schema)}\" value=\"{schema.TableIdentifierName}\" input=\"{input}\" mode=\"list\" index=\"{indexText}\"/>");
            }
        }

        private static string MapTypeName(string fieldTypeName)
        {
            if (LubanTypeNamesByFieldType.TryGetValue(fieldTypeName, out var lubanTypeName))
            {
                return lubanTypeName;
            }

            throw new NotSupportedException($"暂不支持的字段类型：{fieldTypeName}");
        }

        private static string RenderConfiguration(string templateRoot, string workingDirectory)
        {
            // dataDir 相对 conf 文件所在目录解析，指向镜像目录；input 再落到具体文件。
            var mirrorDirectory = Path.Combine(templateRoot, "Config", "Mirror");
            var dataDir = ToPosixRelativePath(workingDirectory, mirrorDirectory);

            // conf 字段少且结构固定，手工拼比走 JsonSerializer 更省事。
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"groups\": [");
            builder.AppendLine("    {\"names\":[\"c\"], \"default\":true},");
            builder.AppendLine("    {\"names\":[\"s\"], \"default\":true},");
            builder.AppendLine("    {\"names\":[\"e\"], \"default\":true}");
            builder.AppendLine("  ],");
            builder.AppendLine("  \"schemaFiles\": [ {\"fileName\":\"Defines\", \"type\":\"\"} ],");
            builder.AppendLine($"  \"dataDir\": \"{dataDir}\",");
            builder.AppendLine("  \"targets\": [");
            builder.AppendLine("    {\"name\":\"all\", \"manager\":\"Tables\", \"groups\":[\"c\",\"s\",\"e\"], \"topModule\":\"cfg\"}");
            builder.AppendLine("  ],");
            builder.AppendLine("  \"xargs\": []");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string ToPosixRelativePath(string fromDirectory, string toDirectory)
        {
            return Path.GetRelativePath(Path.GetFullPath(fromDirectory), Path.GetFullPath(toDirectory))
                .Replace('\\', '/');
        }
    }
}
