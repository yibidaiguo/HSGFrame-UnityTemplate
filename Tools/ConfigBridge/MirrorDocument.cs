using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>镜像 JSON 的内存模型：表名与按行组织的键值对，键用英文标识名。</summary>
    public sealed class MirrorDocument
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>表名，例如「背包」。</summary>
        public string TableName { get; set; }

        /// <summary>数据行，每行是「标识名 → 值」的映射。</summary>
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();

        /// <summary>从镜像 JSON 文件读入；值先以 JsonElement 形态存在，调用方按需 NormalizeValues。</summary>
        public static MirrorDocument LoadFromFile(string mirrorPath)
        {
            var json = File.ReadAllText(mirrorPath);
            return JsonSerializer.Deserialize<MirrorDocument>(json, SerializerOptions);
        }

        /// <summary>把当前镜像写回 JSON 文件，中文原样输出不转义。</summary>
        public void SaveToFile(string mirrorPath)
        {
            var json = JsonSerializer.Serialize(this, SerializerOptions);
            File.WriteAllText(mirrorPath, json);
        }

        /// <summary>
        /// 把每行里仍是 JsonElement 的值按 schema 字段类型转成 CLR 值，未知键跳过留给校验阶段报告。
        /// 转换失败会抛异常，用于回写前确保拿到的都是合法值。
        /// </summary>
        public void NormalizeValues(TableSchema schema)
        {
            foreach (var row in Rows)
            {
                var keys = row.Keys.ToList();
                foreach (var key in keys)
                {
                    var field = schema.FindByIdentifierName(key);
                    if (field == null)
                    {
                        continue;
                    }

                    row[key] = ConvertValue(row[key], field.TypeName);
                }
            }
        }

        /// <summary>按类型名把值转成 CLR 原生值；已经是 CLR 值时原样返回。</summary>
        internal static object ConvertValue(object value, string typeName)
        {
            if (value is not JsonElement element)
            {
                return value;
            }

            switch (typeName)
            {
                case "Int32":
                    return element.GetInt32();
                case "Int64":
                    return element.GetInt64();
                case "Single":
                    return element.GetSingle();
                case "Boolean":
                    return element.GetBoolean();
                case "String":
                    return element.GetString();
                default:
                    throw new NotSupportedException($"不支持的类型名：{typeName}");
            }
        }
    }
}
