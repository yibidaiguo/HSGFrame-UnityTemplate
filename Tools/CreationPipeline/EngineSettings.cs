using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>引擎工作模式：值守、轮询与唤醒。</summary>
    public enum EngineMode
    {
        /// <summary>值守：无常驻进程，人跑一条算一条。</summary>
        Standby,

        /// <summary>轮询：定时扫队列。</summary>
        Polling,

        /// <summary>唤醒：事件提前唤醒 + 轮询兜底。</summary>
        Wakeup
    }

    /// <summary>
    /// 引擎配置：Config/创作管线/引擎.json 的内存形态。
    /// 文件缺失或坏掉时返回一份默认配置（值守 / 60 / 2 / 500000 / 60），原因记在 LoadFailureReason；
    /// 单个键缺失时该键取默认值，其余键照读。默认模式必须是值守——配置缺失时最安全的行为是永不自动。
    /// </summary>
    public sealed class EngineSettings
    {
        /// <summary>
        /// 构造一份引擎配置。
        /// </summary>
        /// <param name="mode">引擎模式。</param>
        /// <param name="pollIntervalSeconds">轮询间隔秒数。</param>
        /// <param name="retryLimit">工作项失败自动修复的重试上限。</param>
        /// <param name="defaultLanguageModelBudget">预算默认值里的 llm 上限。</param>
        /// <param name="defaultImageBudget">预算默认值里的生图上限。</param>
        /// <param name="loadFailureReason">加载失败原因，正常为空串。</param>
        public EngineSettings(
            EngineMode mode,
            int pollIntervalSeconds,
            int retryLimit,
            int defaultLanguageModelBudget,
            int defaultImageBudget,
            string loadFailureReason)
        {
            Mode = mode;
            PollIntervalSeconds = pollIntervalSeconds;
            RetryLimit = retryLimit;
            DefaultLanguageModelBudget = defaultLanguageModelBudget;
            DefaultImageBudget = defaultImageBudget;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>引擎配置文件的路径：&lt;仓库根&gt;/Config/创作管线/引擎.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SettingsFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Config", "创作管线", "引擎.json");
        }

        /// <summary>
        /// 从仓库根加载引擎配置：读 &lt;仓库根&gt;/Config/创作管线/引擎.json。
        /// 文件不存在、JSON 语法错误或根不是对象时返回默认配置不抛异常，原因记进 LoadFailureReason；
        /// 单个键缺失或类型不对时该键取默认值，其余键照读。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static EngineSettings Load(string repositoryRoot)
        {
            var filePath = SettingsFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new EngineSettings(EngineMode.Standby, 60, 2, 500000, 60, $"引擎配置文件不存在：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new EngineSettings(EngineMode.Standby, 60, 2, 500000, 60, $"引擎配置解析失败：{filePath}：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new EngineSettings(EngineMode.Standby, 60, 2, 500000, 60, $"引擎配置根必须是对象：{filePath}");
                }

                var mode = EngineMode.Standby;
                if (TryParseMode(ReadStringOrEmpty(root, "模式"), out var parsedMode))
                {
                    mode = parsedMode;
                }

                return new EngineSettings(
                    mode,
                    ReadInt(root, "轮询间隔秒", 60),
                    ReadInt(root, "重试上限", 2),
                    ReadBudget(root, "llm上限", 500000),
                    ReadBudget(root, "生图上限", 60),
                    "");
            }
        }

        /// <summary>
        /// 把配置写回 &lt;仓库根&gt;/Config/创作管线/引擎.json，目录不存在就建；缩进 + 不转义中文。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="settings">要写盘的引擎配置。</param>
        public static void Save(string repositoryRoot, EngineSettings settings)
        {
            var filePath = SettingsFile(repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = new JsonObject
            {
                ["模式"] = ToChineseName(settings.Mode),
                ["轮询间隔秒"] = settings.PollIntervalSeconds,
                ["重试上限"] = settings.RetryLimit,
                ["预算默认值"] = new JsonObject
                {
                    ["llm上限"] = settings.DefaultLanguageModelBudget,
                    ["生图上限"] = settings.DefaultImageBudget
                }
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        /// <summary>引擎模式。</summary>
        public EngineMode Mode { get; }

        /// <summary>轮询间隔秒数。</summary>
        public int PollIntervalSeconds { get; }

        /// <summary>工作项失败自动修复的重试上限。</summary>
        public int RetryLimit { get; }

        /// <summary>预算默认值里的 llm 上限，对应「llm上限」。</summary>
        public int DefaultLanguageModelBudget { get; }

        /// <summary>预算默认值里的生图上限，对应「生图上限」。</summary>
        public int DefaultImageBudget { get; }

        /// <summary>加载失败原因，正常加载为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 返回一份只有模式不同的新实例（本类保持不可变）。
        /// </summary>
        /// <param name="mode">新的引擎模式。</param>
        public EngineSettings WithMode(EngineMode mode)
        {
            return new EngineSettings(mode, PollIntervalSeconds, RetryLimit, DefaultLanguageModelBudget, DefaultImageBudget, LoadFailureReason);
        }

        /// <summary>
        /// 把模式转成中文名：值守 / 轮询 / 唤醒；未知枚举值退回「值守」。
        /// </summary>
        /// <param name="mode">引擎模式。</param>
        public static string ToChineseName(EngineMode mode)
        {
            switch (mode)
            {
                case EngineMode.Standby:
                    return "值守";
                case EngineMode.Polling:
                    return "轮询";
                case EngineMode.Wakeup:
                    return "唤醒";
                default:
                    return "值守";
            }
        }

        /// <summary>
        /// 把中文名解析成模式：值守 / 轮询 / 唤醒；解析不了返回 false。
        /// </summary>
        /// <param name="chineseName">中文模式名。</param>
        /// <param name="mode">解析出的模式；失败时为默认值。</param>
        public static bool TryParseMode(string chineseName, out EngineMode mode)
        {
            switch (chineseName)
            {
                case "值守":
                    mode = EngineMode.Standby;
                    return true;
                case "轮询":
                    mode = EngineMode.Polling;
                    return true;
                case "唤醒":
                    mode = EngineMode.Wakeup;
                    return true;
                default:
                    mode = default;
                    return false;
            }
        }

        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            // 以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver：
            // 配置 JSON 里的 JsonObject 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读整数属性；缺失或类型不对给默认值。</summary>
        private static int ReadInt(JsonElement element, string propertyName, int fallback)
        {
            if (element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number))
            {
                return number;
            }

            return fallback;
        }

        /// <summary>读「预算默认值」对象里的键；缺失或类型不对给默认值。</summary>
        private static int ReadBudget(JsonElement root, string key, int fallback)
        {
            if (root.TryGetProperty("预算默认值", out var budgetElement)
                && budgetElement.ValueKind == JsonValueKind.Object)
            {
                return ReadInt(budgetElement, key, fallback);
            }

            return fallback;
        }
    }
}
