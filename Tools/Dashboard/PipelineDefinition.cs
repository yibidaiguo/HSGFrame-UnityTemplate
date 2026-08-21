using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.Dashboard
{
    /// <summary>流水线里的一个步骤：名称、要跑的程序与参数，以及它是否需要 Unity。</summary>
    public sealed class PipelineStep
    {
        /// <summary>步骤名称，例如「dotnet test」。</summary>
        [JsonPropertyName("名称")]
        public string Name { get; set; }

        /// <summary>要运行的程序文件名，例如 dotnet 或 pwsh。</summary>
        [JsonPropertyName("程序")]
        public string FileName { get; set; }

        /// <summary>传给程序的有序参数列表。</summary>
        [JsonPropertyName("参数")]
        public IReadOnlyList<string> Arguments { get; set; }

        /// <summary>为 true 时该步骤需要 Unity 编辑器在场才能跑。</summary>
        [JsonPropertyName("需要Unity")]
        public bool RequiresUnity { get; set; }
    }

    /// <summary>一条流水线：名称、说明与有序的步骤清单。</summary>
    public sealed class PipelineDefinition
    {
        /// <summary>流水线名称。</summary>
        [JsonPropertyName("名称")]
        public string Name { get; set; }

        /// <summary>这条流水线做什么的说明。</summary>
        [JsonPropertyName("说明")]
        public string Description { get; set; }

        /// <summary>有序的步骤清单。</summary>
        [JsonPropertyName("步骤")]
        public IReadOnlyList<PipelineStep> Steps { get; set; }
    }

    /// <summary>整份流水线定义文件：一条或多条流水线的集合。</summary>
    public sealed class PipelineCatalog
    {
        private static readonly JsonSerializerOptions DefinitionOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>文件里的全部流水线，按定义顺序排列。</summary>
        [JsonPropertyName("流水线")]
        public IReadOnlyList<PipelineDefinition> Pipelines { get; set; }

        /// <summary>从「pipelines.json」读回全部流水线，格式不对时抛 PipelineDefinitionException。</summary>
        /// <param name="path">定义文件路径。</param>
        public static PipelineCatalog LoadFromFile(string path)
        {
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (IOException exception)
            {
                throw new PipelineDefinitionException(
                    $"位置：{path}；原因：定义文件读取失败：{exception.Message}；修复：确认文件存在且可读；参考：Pipelines/pipelines.json");
            }

            try
            {
                var catalog = JsonSerializer.Deserialize<PipelineCatalog>(json, DefinitionOptions);
                if (catalog == null)
                {
                    throw new PipelineDefinitionException(
                        $"位置：{path}；原因：定义内容为空；修复：填写「流水线」数组；参考：Pipelines/pipelines.json");
                }

                return catalog;
            }
            catch (JsonException exception)
            {
                throw new PipelineDefinitionException(
                    $"位置：{path}；原因：定义格式不合法：{exception.Message}；修复：按报错的行列修正 JSON；参考：Pipelines/pipelines.json");
            }
        }

        /// <summary>按名称找一条流水线，找不到返回 null。</summary>
        /// <param name="pipelineName">流水线名称。</param>
        public PipelineDefinition Find(string pipelineName)
        {
            if (Pipelines == null)
            {
                return null;
            }

            foreach (var pipeline in Pipelines)
            {
                if (pipeline != null && string.Equals(pipeline.Name, pipelineName, StringComparison.Ordinal))
                {
                    return pipeline;
                }
            }

            return null;
        }
    }

    /// <summary>流水线定义读取失败时抛出，消息按四要素书写。</summary>
    public sealed class PipelineDefinitionException : Exception
    {
        /// <summary>用已拼好的四要素消息构造异常。</summary>
        /// <param name="message">四要素消息。</param>
        public PipelineDefinitionException(string message)
            : base(message)
        {
        }
    }
}
