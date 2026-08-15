using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSGFrame.Input
{
    /// <summary>输入绑定的 JSON 编解码。</summary>
    public static class InputBindingCodec
    {
        // Encoder 用 UnsafeRelaxedJsonEscaping 让中文键名与值原样输出，不转义成 \uXXXX；缩进便于人直接改这份配置。
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>把绑定表序列化成 JSON 文本。</summary>
        public static string ToJson(InputBindingTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            var dto = new BindingTableDto { Bindings = table.Snapshot().ToList() };
            return JsonSerializer.Serialize(dto, Options);
        }

        /// <summary>从 JSON 文本反序列化出绑定表，格式不对时抛 InputBindingException。</summary>
        public static InputBindingTable FromJson(string json)
        {
            BindingTableDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<BindingTableDto>(json, Options);
            }
            catch (JsonException exception)
            {
                throw new InputBindingException(
                    $"位置：绑定 JSON；原因：JSON 无法解析；修复：核对 JSON 语法；参考：{exception.Message}",
                    exception);
            }

            if (dto == null || dto.Bindings == null)
            {
                throw new InputBindingException(
                    "位置：绑定 JSON；原因：缺少「绑定」字段；修复：补上顶层「绑定」数组；参考：{ \"绑定\": [] }");
            }

            var entries = new List<InputBindingEntry>(dto.Bindings.Count);
            for (var index = 0; index < dto.Bindings.Count; index++)
            {
                var item = dto.Bindings[index];
                if (item == null || string.IsNullOrEmpty(item.ActionName))
                {
                    throw new InputBindingException(
                        $"位置：绑定第 {index} 条；原因：缺少「动作」字段；修复：给每条绑定补上「动作」；参考：\"动作\": \"跳跃\"");
                }

                entries.Add(new InputBindingEntry
                {
                    ActionName = item.ActionName,
                    PrimaryKey = item.PrimaryKey,
                    SecondaryKey = item.SecondaryKey,
                });
            }

            return new InputBindingTable(entries);
        }

        private sealed class BindingTableDto
        {
            [JsonPropertyName("绑定")]
            public List<InputBindingEntry> Bindings { get; set; }
        }
    }

    /// <summary>输入绑定读写失败时抛出，消息按四要素书写。</summary>
    public sealed class InputBindingException : Exception
    {
        /// <summary>以失败消息构造。</summary>
        public InputBindingException(string message)
            : base(message)
        {
        }

        /// <summary>以失败消息与内部异常构造。</summary>
        public InputBindingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
