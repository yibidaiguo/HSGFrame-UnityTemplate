using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 下游对象台账：这个项目连的是**哪几个**下游对象（表、空间、节点……），按 driver 分节。
    ///
    /// 为什么不放 <c>local.json</c>：那份在 <c>.gitignore</c> 里（它装着密钥）。
    /// 对象 id 放进去，换台机器一 clone 就没了，链路会**再建一批新的**——
    /// 于是同一个项目在下游有两套表，数据分家。而这些 id **不是密钥**，
    /// 它们是「这个仓库连的是哪几个东西」，本来就该跟着仓库走。
    ///
    /// 所以分工是：**对象 id 进 git 的这份台账，密钥一步不让地留在 local.json**（决策 5）。
    ///
    /// 台账是**自动创建的落点**：链路要用某个对象时先看台账，
    /// 台账没有就去下游建一个，建完把 id 回填进来。下一次（哪怕换了机器）就直接用，
    /// 不会重复建。这条规矩对五样东西一视同仁——知识空间、多维表格、需求表、任务表、文档父节点。
    /// </summary>
    public sealed class DownstreamObjectLedger
    {
        /// <summary>台账文件里放各 driver 对象的那一节。</summary>
        public const string ObjectsSectionKey = "对象";

        /// <summary>台账的契约版本键。</summary>
        public const string ContractVersionKey = "契约版本";

        /// <summary>当前契约版本。</summary>
        public const string ContractVersion = "1.0.0";

        /// <summary>写盘选项：缩进、中文原样。台账要给人读，也要能看 git diff。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 构造一份台账。
        /// </summary>
        /// <param name="byDriver">driver 名 → （对象键 → 值）。</param>
        /// <param name="loadFailureReason">加载失败原因；正常（含文件不存在）为空串。</param>
        public DownstreamObjectLedger(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byDriver,
            string loadFailureReason)
        {
            ByDriver = byDriver ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>driver 名 → （对象键 → 值）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByDriver { get; }

        /// <summary>
        /// 加载失败原因；正常为空串。**文件不存在不算失败**——第一次跑就是这种情况，
        /// 那时台账本来就该是空的，链路会把五样对象一个个建出来再回填。
        /// </summary>
        public string LoadFailureReason { get; }

        /// <summary>台账文件路径：Tools/CreationPipeline/Config/downstream-objects.json（进 git）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string LedgerFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "downstream-objects.json");
        }

        /// <summary>给人看的相对路径，用在提示文案里。</summary>
        public static string RelativeLedgerPath()
        {
            return "Tools/CreationPipeline/Config/downstream-objects.json";
        }

        /// <summary>
        /// 读台账。文件不存在给一份空台账（不算失败）；JSON 坏掉给空台账**并带上原因**——
        /// 坏掉与空是两支（决策 42）：空台账会去建对象，而坏掉时该让人先去修文件，
        /// 不然一个多打的逗号就能让链路在下游又建一批新表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static DownstreamObjectLedger Load(string repositoryRoot)
        {
            var filePath = LedgerFile(repositoryRoot);
            var empty = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (!File.Exists(filePath))
            {
                return new DownstreamObjectLedger(empty, "");
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new DownstreamObjectLedger(empty, $"下游对象台账不是合法 JSON：{exception.Message}（{RelativeLedgerPath()}）");
            }

            if (node is not JsonObject root)
            {
                return new DownstreamObjectLedger(empty, $"下游对象台账顶层不是对象（{RelativeLedgerPath()}）");
            }

            if (root[ObjectsSectionKey] is not JsonObject objects)
            {
                return new DownstreamObjectLedger(empty, "");
            }

            var byDriver = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var driverPair in objects)
            {
                if (driverPair.Value is not JsonObject driverObject)
                {
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var fieldPair in driverObject)
                {
                    if (fieldPair.Key.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (fieldPair.Value is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
                    {
                        values[fieldPair.Key] = text;
                    }
                }

                byDriver[driverPair.Key] = values;
            }

            return new DownstreamObjectLedger(byDriver, "");
        }

        /// <summary>读某个 driver 的某个对象 id；没有给空串。</summary>
        /// <param name="driverName">driver 名。</param>
        /// <param name="objectKey">对象键，如「多维表格标识」。</param>
        public string Read(string driverName, string objectKey)
        {
            return ByDriver.TryGetValue(driverName ?? "", out var values)
                && values.TryGetValue(objectKey ?? "", out var value)
                ? value
                : "";
        }

        /// <summary>某个 driver 的全部对象；没有给空表。</summary>
        /// <param name="driverName">driver 名。</param>
        public IReadOnlyDictionary<string, string> ReadAll(string driverName)
        {
            return ByDriver.TryGetValue(driverName ?? "", out var values)
                ? values
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 回填若干对象 id。空值的键**跳过而不是写空串**：写空串等于宣告「这个对象不存在」，
        /// 下一次链路又会去建一个新的。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="values">对象键 → 值。</param>
        public static ConfigWriteOutcome Write(
            string repositoryRoot,
            string driverName,
            IReadOnlyDictionary<string, string> values)
        {
            var filePath = LedgerFile(repositoryRoot);
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return ConfigWriteOutcome.Failure("driver 名为空，不知道回填到哪一节", filePath);
            }

            if (values == null || values.Count == 0)
            {
                return ConfigWriteOutcome.Success("没有要回填的对象", filePath);
            }

            JsonObject root;
            try
            {
                root = ReadRootForWrite(filePath);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure(exception.Message, filePath);
            }

            root[ContractVersionKey] = ContractVersion;
            if (root[ObjectsSectionKey] is not JsonObject objects)
            {
                objects = new JsonObject();
                root[ObjectsSectionKey] = objects;
            }

            if (objects[driverName] is not JsonObject driverObject)
            {
                driverObject = new JsonObject();
                objects[driverName] = driverObject;
            }

            var written = new List<string>();
            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                driverObject[pair.Key] = pair.Value;
                written.Add(pair.Key);
            }

            if (written.Count == 0)
            {
                return ConfigWriteOutcome.Success("没有要回填的对象（给的值都是空的）", filePath);
            }

            written.Sort(StringComparer.Ordinal);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
                File.WriteAllText(filePath, root.ToJsonString(WriteOptions) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return ConfigWriteOutcome.Failure($"台账写不下去：{exception.Message}", filePath);
            }

            return ConfigWriteOutcome.Success(
                $"已回填 {driverName} 的 {written.Count} 个对象：{string.Join("、", written)}",
                filePath);
        }

        /// <summary>
        /// 读顶层对象准备改写：文件不存在给一份带说明的新骨架；
        /// JSON 坏掉抛 InvalidOperationException——**绝不**当空对象接着写，
        /// 那等于拿一份干净骨架把已有的对象 id 全盖掉，下次就在下游建出第二套表。
        /// </summary>
        private static JsonObject ReadRootForWrite(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new JsonObject
                {
                    ["_说明"] = "下游对象台账：这个仓库连的是哪几个下游对象（表 / 空间 / 节点），按 driver 分节。"
                        + "进 git，跟着仓库走——换台机器 clone 下来还是同一批对象，不会重复建。"
                        + "**密钥一个都不许有**，密钥全在 local.json（决策 5）。"
                        + "值由链路自动回填：要用某个对象时先看这里，没有就去下游建一个再写回来。",
                    [ContractVersionKey] = ContractVersion,
                    [ObjectsSectionKey] = new JsonObject()
                };
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException(
                    $"下游对象台账不是合法 JSON，没敢写：{exception.Message}（先把 {RelativeLedgerPath()} 修成合法 JSON）");
            }

            if (node is not JsonObject root)
            {
                throw new InvalidOperationException($"下游对象台账顶层不是对象，没敢写（{RelativeLedgerPath()}）");
            }

            return root;
        }
    }
}
