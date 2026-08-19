using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把校验错误文案目录导出成下游可读的 JSON：pool.pull 拒收回贴与下游助手共用同一份文案，
    /// 文案的唯一来源始终是 ValidationMessageCatalog。
    /// </summary>
    public static class ValidationMessageExporter
    {
        /// <summary>
        /// 把 ValidationMessageCatalog.Entries 写成 JSON 文件：顶层 说明 / 条目，
        /// 条目项键 规则id / 文案 / 修复建议；目标目录不存在时先创建。
        /// </summary>
        /// <param name="filePath">输出路径。</param>
        public static void WriteTo(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entries = new List<object>(ValidationMessageCatalog.Entries.Count);
            foreach (var entry in ValidationMessageCatalog.Entries)
            {
                entries.Add(new Dictionary<string, string>
                {
                    ["规则id"] = entry.RuleIdentifier,
                    ["文案"] = entry.MessageTemplate,
                    ["修复建议"] = entry.FixTemplate
                });
            }

            var payload = new Dictionary<string, object>
            {
                ["说明"] = "拒收回贴与下游助手共用同一份文案，改文案只改 ValidationMessageCatalog",
                ["条目"] = entries
            };

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(false));
        }

        /// <summary>写校验错误文案用的序列化选项：缩进 + 中文不转义。</summary>
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
    }
}
