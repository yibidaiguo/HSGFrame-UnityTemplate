using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>命令断点：把命令分步执行，每完成一步就写回断点文件，失败后带 resume:true 可从断点续跑。</summary>
    public sealed class CommandProgress
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _commandName;
        private readonly string _inputHash;
        private readonly string _progressFilePath;
        private readonly List<string> _completedStepNames;

        private CommandProgress(string commandName, string inputHash, string progressFilePath, IEnumerable<string> completedStepNames)
        {
            _commandName = commandName;
            _inputHash = inputHash;
            _progressFilePath = progressFilePath;
            _completedStepNames = new List<string>(completedStepNames);
        }

        /// <summary>把参数 JSON 规范化后算出的短哈希，用来给断点文件命名。</summary>
        /// <param name="argumentsJson">参数 JSON 原文，null 按空字符串算。</param>
        public static string ComputeInputHash(string argumentsJson)
        {
            var normalized = Normalize(argumentsJson);
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return hex.Substring(0, 16);
        }

        /// <summary>
        /// 加载（或新建）断点：resume 为 true 且断点文件哈希匹配时读回已完成步骤，否则从头开始并清掉旧断点。
        /// </summary>
        /// <param name="progressRootDirectory">断点根目录。</param>
        /// <param name="commandName">命令名。</param>
        /// <param name="argumentsJson">参数 JSON 原文。</param>
        /// <param name="resume">为 true 时尝试续跑。</param>
        public static CommandProgress Load(string progressRootDirectory, string commandName, string argumentsJson, bool resume)
        {
            var inputHash = ComputeInputHash(argumentsJson);
            var progressFilePath = Path.Combine(
                progressRootDirectory,
                "Temp",
                "EditorCommand",
                $"{commandName}-{inputHash}.progress.json");

            Directory.CreateDirectory(progressRootDirectory);

            var loadedSteps = new List<string>();
            if (resume && File.Exists(progressFilePath))
            {
                ProgressFileModel model = null;
                try
                {
                    model = ReadModel(progressFilePath);
                }
                catch (JsonException)
                {
                    // 断点文件损坏等于没有断点：从头跑，不把坏文件留给下一次。
                    model = null;
                }

                if (model != null && string.Equals(model.InputHash, inputHash, StringComparison.Ordinal))
                {
                    loadedSteps.AddRange(model.CompletedSteps ?? new List<string>());
                }
                else
                {
                    File.Delete(progressFilePath);
                }
            }
            else if (File.Exists(progressFilePath))
            {
                File.Delete(progressFilePath);
            }

            return new CommandProgress(commandName, inputHash, progressFilePath, loadedSteps);
        }

        /// <summary>已完成步骤名集合。</summary>
        public IReadOnlyCollection<string> CompletedStepNames => _completedStepNames;

        /// <summary>断点文件的完整路径。</summary>
        public string ProgressFilePath => _progressFilePath;

        /// <summary>
        /// 执行一步：已完成的步骤直接跳过返回 false；否则执行后立刻写回断点文件返回 true。
        /// 步骤执行抛异常时原样向外抛，断点文件保留在上一次写回的状态。
        /// </summary>
        /// <param name="stepName">步骤名。</param>
        /// <param name="stepAction">步骤要执行的动作。</param>
        public bool RunStep(string stepName, Action stepAction)
        {
            if (_completedStepNames.Contains(stepName))
            {
                return false;
            }

            stepAction();

            _completedStepNames.Add(stepName);
            WriteModel();
            return true;
        }

        /// <summary>整条命令成功后删除断点文件，断点目录空则一并删掉。</summary>
        public void Complete()
        {
            if (File.Exists(_progressFilePath))
            {
                File.Delete(_progressFilePath);
            }

            var directory = Path.GetDirectoryName(_progressFilePath);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        private void WriteModel()
        {
            var model = new ProgressFileModel
            {
                Command = _commandName,
                InputHash = _inputHash,
                CompletedSteps = new List<string>(_completedStepNames),
                UpdatedAt = DateTime.Now.ToString("o")
            };

            var directory = Path.GetDirectoryName(_progressFilePath);
            Directory.CreateDirectory(directory);
            File.WriteAllText(_progressFilePath, JsonSerializer.Serialize(model, JsonOptions));
        }

        private static ProgressFileModel ReadModel(string path)
        {
            return JsonSerializer.Deserialize<ProgressFileModel>(File.ReadAllText(path), JsonOptions);
        }

        /// <summary>算哈希时要摘掉的控制参数名：它决定「怎么跑」，而不是「跑什么」。</summary>
        private const string ResumePropertyName = "Resume";

        private static string Normalize(string argumentsJson)
        {
            if (string.IsNullOrEmpty(argumentsJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return JsonSerializer.Serialize(document.RootElement);
                }

                // Resume 自己也在参数 JSON 里，把它算进哈希的话，resume:true 的那次必然与
                // resume:false 那次算出不同的哈希，断点永远匹配不上——续跑就成了摆设。
                // 属性名排序一并做掉：同一份参数换个书写顺序应当得到同一个断点。
                var retained = document.RootElement
                    .EnumerateObject()
                    .Where(property => !string.Equals(property.Name, ResumePropertyName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToDictionary(property => property.Name, property => property.Value.GetRawText());

                return JsonSerializer.Serialize(retained);
            }
            catch (JsonException)
            {
                return argumentsJson;
            }
        }

        /// <summary>断点文件的磁盘形状，键用中文与结构化日志流一致。</summary>
        private sealed class ProgressFileModel
        {
            /// <summary>命令名。</summary>
            [JsonPropertyName("命令")]
            public string Command { get; set; }

            /// <summary>输入哈希。</summary>
            [JsonPropertyName("输入哈希")]
            public string InputHash { get; set; }

            /// <summary>已完成步骤名列表。</summary>
            [JsonPropertyName("已完成步骤")]
            public List<string> CompletedSteps { get; set; }

            /// <summary>更新时间。</summary>
            [JsonPropertyName("更新时间")]
            public string UpdatedAt { get; set; }
        }
    }
}
