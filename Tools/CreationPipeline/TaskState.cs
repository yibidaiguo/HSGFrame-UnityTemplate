using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>任务预算：llm 与生图各自的上限、已用。</summary>
    public sealed class TaskBudget
    {
        /// <summary>
        /// 构造一份任务预算。
        /// </summary>
        /// <param name="languageModelLimit">llm 上限。</param>
        /// <param name="languageModelUsed">llm 已用。</param>
        /// <param name="imageLimit">生图上限。</param>
        /// <param name="imageUsed">生图已用。</param>
        public TaskBudget(int languageModelLimit, int languageModelUsed, int imageLimit, int imageUsed)
        {
            LanguageModelLimit = languageModelLimit;
            LanguageModelUsed = languageModelUsed;
            ImageLimit = imageLimit;
            ImageUsed = imageUsed;
        }

        /// <summary>llm 上限。</summary>
        public int LanguageModelLimit { get; }

        /// <summary>llm 已用。</summary>
        public int LanguageModelUsed { get; }

        /// <summary>生图上限。</summary>
        public int ImageLimit { get; }

        /// <summary>生图已用。</summary>
        public int ImageUsed { get; }
    }

    /// <summary>
    /// 任务状态：_Tasks/&lt;REQ&gt;/状态.json 的内存形态，面板与 task.status 的唯一数据源。
    /// 阶段、子状态、当前工作项、关卡待审、预算与产物哈希全部只读。
    /// </summary>
    public sealed class TaskState
    {
        /// <summary>
        /// 构造一份任务状态。
        /// </summary>
        /// <param name="stage">阶段。</param>
        /// <param name="subState">子状态。</param>
        /// <param name="currentWorkItem">当前工作项，可空。</param>
        /// <param name="pendingGate">关卡待审，可空。</param>
        /// <param name="budget">任务预算。</param>
        /// <param name="artifactHashes">产物哈希，键为产物相对路径；传 null 视为空字典。</param>
        public TaskState(
            string stage,
            string subState,
            string currentWorkItem,
            string pendingGate,
            TaskBudget budget,
            IReadOnlyDictionary<string, string> artifactHashes)
        {
            Stage = stage ?? "";
            SubState = subState ?? "";
            CurrentWorkItem = currentWorkItem;
            PendingGate = pendingGate;
            Budget = budget ?? new TaskBudget(0, 0, 0, 0);
            ArtifactHashes = artifactHashes ?? new Dictionary<string, string>();
        }

        /// <summary>阶段。</summary>
        public string Stage { get; }

        /// <summary>子状态。</summary>
        public string SubState { get; }

        /// <summary>当前工作项，可为 null。</summary>
        public string CurrentWorkItem { get; }

        /// <summary>关卡待审，可为 null。</summary>
        public string PendingGate { get; }

        /// <summary>任务预算。</summary>
        public TaskBudget Budget { get; }

        /// <summary>产物哈希，键为产物相对路径。</summary>
        public IReadOnlyDictionary<string, string> ArtifactHashes { get; }

        /// <summary>
        /// 尝试从仓库根加载某需求的任务状态：读 _Tasks/&lt;REQ&gt;/状态.json。
        /// 文件不存在或 JSON 坏掉时返回 false 并给出原因；缺的键取空串 / null / 0，不抛异常。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="state">加载出的任务状态；失败时为 null。</param>
        /// <param name="failureReason">失败原因，成功时为空串。</param>
        public static bool TryLoad(
            string repositoryRoot,
            string requirementIdentifier,
            out TaskState state,
            out string failureReason)
        {
            var filePath = PipelinePaths.TaskStateFile(repositoryRoot, requirementIdentifier);
            if (!File.Exists(filePath))
            {
                state = null;
                failureReason = $"任务状态文件不存在：{filePath}";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                state = null;
                failureReason = $"任务状态文件解析失败：{filePath}：{exception.Message}";
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    state = null;
                    failureReason = $"任务状态文件根必须是对象：{filePath}";
                    return false;
                }

                state = new TaskState(
                    ReadStringOrEmpty(root, "阶段"),
                    ReadStringOrEmpty(root, "子状态"),
                    ReadNullableString(root, "当前工作项"),
                    ReadNullableString(root, "关卡待审"),
                    ReadBudget(root),
                    ReadArtifactHashes(root));
                failureReason = "";
                return true;
            }
        }

        /// <summary>
        /// 把任务状态写回 _Tasks/&lt;REQ&gt;/状态.json，目录不存在就建；缩进 + 不转义中文。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="state">要写盘的任务状态。</param>
        public static void Save(string repositoryRoot, string requirementIdentifier, TaskState state)
        {
            var filePath = PipelinePaths.TaskStateFile(repositoryRoot, requirementIdentifier);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var budget = new JsonObject
            {
                ["llm上限"] = state.Budget.LanguageModelLimit,
                ["llm已用"] = state.Budget.LanguageModelUsed,
                ["生图上限"] = state.Budget.ImageLimit,
                ["生图已用"] = state.Budget.ImageUsed
            };

            var hashes = new JsonObject();
            foreach (var pair in state.ArtifactHashes)
            {
                hashes[pair.Key] = pair.Value;
            }

            var content = new JsonObject
            {
                ["阶段"] = state.Stage,
                ["子状态"] = state.SubState,
                ["当前工作项"] = state.CurrentWorkItem,
                ["关卡待审"] = state.PendingGate,
                ["预算"] = budget,
                ["产物哈希"] = hashes
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            // 以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver：
            // 状态 JSON 里的 JsonObject 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
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

        /// <summary>读可空字符串属性；缺失、null 或类型不对给 null。</summary>
        private static string ReadNullableString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        /// <summary>读「预算」对象；缺失或类型不对给全 0 的预算，单键缺失给 0。</summary>
        private static TaskBudget ReadBudget(JsonElement root)
        {
            if (!root.TryGetProperty("预算", out var budgetElement) || budgetElement.ValueKind != JsonValueKind.Object)
            {
                return new TaskBudget(0, 0, 0, 0);
            }

            return new TaskBudget(
                ReadInt(budgetElement, "llm上限", 0),
                ReadInt(budgetElement, "llm已用", 0),
                ReadInt(budgetElement, "生图上限", 0),
                ReadInt(budgetElement, "生图已用", 0));
        }

        /// <summary>读「产物哈希」对象；缺失或类型不对给空字典，非字符串值跳过。</summary>
        private static IReadOnlyDictionary<string, string> ReadArtifactHashes(JsonElement root)
        {
            var hashes = new Dictionary<string, string>();
            if (!root.TryGetProperty("产物哈希", out var hashesElement) || hashesElement.ValueKind != JsonValueKind.Object)
            {
                return hashes;
            }

            foreach (var property in hashesElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    hashes[property.Name] = property.Value.GetString() ?? "";
                }
            }

            return hashes;
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
    }
}
