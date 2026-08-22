using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 改插件声明清单 <c>Tools/CreationPipeline/Config/editor-plugins.json</c>：加一条、改一条、删一条。
    ///
    /// 一条声明由 (宿主, 名称) 认领——同一个插件装进两个宿主是两条，不是一条。
    /// 读改写、不重建：文件里的说明字段与契约版本原样保留；JSON 坏掉时拒绝写，
    /// 绝不拿一份干净骨架把人写了一半的清单盖掉。写回时按 (宿主, 名称) 排序，
    /// 免得每改一条 git diff 就整篇翻个个儿。
    /// </summary>
    public static class EditorPluginWriter
    {
        /// <summary>清单里装条目的那个数组的键名。</summary>
        private const string EntryListName = "插件";

        /// <summary>新建清单时写进去的契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>
        /// 加一条或改一条声明：(宿主, 名称) 已经有了就整条覆盖，没有就追加。
        /// 名称与宿主必填——没有这两样就认领不了一条声明，也就无从判断改的是哪一条。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="entry">要写的声明。</param>
        public static ConfigWriteOutcome Upsert(string repositoryRoot, EditorPluginEntry entry)
        {
            var filePath = EditorPluginManifest.ManifestFile(repositoryRoot);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            {
                return ConfigWriteOutcome.Failure("插件名必填", filePath);
            }

            if (string.IsNullOrWhiteSpace(entry.HostName))
            {
                return ConfigWriteOutcome.Failure("宿主必填：unity，或 Bridges/ 下的目录名", filePath);
            }

            JsonObject root;
            try
            {
                root = ReadRoot(filePath);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure(exception.Message, filePath);
            }

            var entries = ReadEntries(root, out var failureReason);
            if (failureReason != null)
            {
                return ConfigWriteOutcome.Failure(failureReason, filePath);
            }

            var replaced = entries.RemoveAll(existing => Same(existing, entry.HostName, entry.Name)) > 0;
            entries.Add(entry);
            WriteEntries(filePath, root, entries);
            return ConfigWriteOutcome.Success(
                replaced
                    ? $"已改声明：{entry.HostName} / {entry.Name}"
                    : $"已加声明：{entry.HostName} / {entry.Name}",
                filePath);
        }

        /// <summary>
        /// 删一条声明。删的是「我们不再要这个插件了」这句话，**不动磁盘上装好的东西**——
        /// 面板不卸载任何插件，删声明只是让它从清单上消失。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="hostName">宿主名。</param>
        /// <param name="name">插件名。</param>
        public static ConfigWriteOutcome Remove(string repositoryRoot, string hostName, string name)
        {
            var filePath = EditorPluginManifest.ManifestFile(repositoryRoot);
            if (string.IsNullOrWhiteSpace(hostName) || string.IsNullOrWhiteSpace(name))
            {
                return ConfigWriteOutcome.Failure("宿主与插件名都要给：一条声明由这两样认领", filePath);
            }

            if (!File.Exists(filePath))
            {
                return ConfigWriteOutcome.Failure("插件声明清单还不存在，没什么可删的", filePath);
            }

            JsonObject root;
            try
            {
                root = ReadRoot(filePath);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure(exception.Message, filePath);
            }

            var entries = ReadEntries(root, out var failureReason);
            if (failureReason != null)
            {
                return ConfigWriteOutcome.Failure(failureReason, filePath);
            }

            if (entries.RemoveAll(existing => Same(existing, hostName, name)) == 0)
            {
                return ConfigWriteOutcome.Failure($"清单里没有 {hostName} / {name} 这一条", filePath);
            }

            WriteEntries(filePath, root, entries);
            return ConfigWriteOutcome.Success(
                $"已删声明：{hostName} / {name}（磁盘上装好的东西一个字节都没动）",
                filePath);
        }

        /// <summary>一条声明是不是由这对 (宿主, 名称) 认领的。</summary>
        private static bool Same(EditorPluginEntry entry, string hostName, string name)
        {
            return string.Equals(entry.HostName, hostName, StringComparison.Ordinal)
                && string.Equals(entry.Name, name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 读顶层对象：文件不存在给一份带说明的骨架；
        /// JSON 坏掉抛 InvalidOperationException——绝不当成空清单接着写，那会把人写的东西整片抹掉。
        /// </summary>
        private static JsonObject ReadRoot(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new JsonObject
                {
                    ["_说明"] = "插件声明清单：只收包管理器看不见的那类编辑器插件（解包安装的）。面板「桥接包」页与 bridge.inventory 读这份清单。",
                    ["契约版本"] = ContractVersion
                };
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"插件声明清单不是合法 JSON，没敢写：{exception.Message}");
            }

            if (node is not JsonObject root)
            {
                throw new InvalidOperationException("插件声明清单顶层不是对象，没敢写");
            }

            return root;
        }

        /// <summary>把清单里已有的条目读成对象列表；「插件」不是数组时给原因。</summary>
        private static List<EditorPluginEntry> ReadEntries(JsonObject root, out string failureReason)
        {
            failureReason = null;
            var entries = new List<EditorPluginEntry>();
            if (!root.ContainsKey(EntryListName))
            {
                return entries;
            }

            if (root[EntryListName] is not JsonArray array)
            {
                failureReason = $"清单里的「{EntryListName}」不是数组，不敢动它";
                return entries;
            }

            foreach (var item in array)
            {
                if (item is not JsonObject entryObject)
                {
                    continue;
                }

                entries.Add(new EditorPluginEntry(
                    ReadString(entryObject, "名称"),
                    ReadString(entryObject, "宿主"),
                    ReadString(entryObject, "标志路径"),
                    ReadString(entryObject, "版本"),
                    ReadString(entryObject, "来源"),
                    ReadString(entryObject, "安装步骤"),
                    ReadString(entryObject, "说明")));
            }

            return entries;
        }

        /// <summary>把条目按 (宿主, 名称) 排序写回，顶层其余键原样保留。</summary>
        private static void WriteEntries(string filePath, JsonObject root, List<EditorPluginEntry> entries)
        {
            var ordered = entries
                .OrderBy(entry => entry.HostName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal);
            var array = new JsonArray();
            foreach (var entry in ordered)
            {
                array.Add(new JsonObject
                {
                    ["名称"] = entry.Name,
                    ["宿主"] = entry.HostName,
                    ["标志路径"] = entry.MarkerPath,
                    ["版本"] = entry.Version,
                    ["来源"] = entry.Source,
                    ["安装步骤"] = entry.InstallSteps,
                    ["说明"] = entry.Description
                });
            }

            root[EntryListName] = array;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            // 末尾补一个换行：JsonNode 不写它，而仓库里的 JSON 都是有的——
            // 少这一个字符，每次保存都会在 git diff 里留一条「\ No newline at end of file」的噪音。
            File.WriteAllText(filePath, root.ToJsonString(options) + Environment.NewLine, new UTF8Encoding(false));
        }

        /// <summary>读一个字符串属性；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject entryObject, string propertyName)
        {
            return entryObject[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }
    }
}
