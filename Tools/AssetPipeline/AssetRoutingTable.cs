using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>收件箱归档路由的一条：一组扩展名对应一个目标目录。</summary>
    public sealed class AssetRoutingEntry
    {
        /// <summary>这一条路由覆盖的扩展名集合，例如 [".png", ".tga"]。</summary>
        [JsonPropertyName("扩展名")]
        public IReadOnlyList<string> Extensions { get; set; } = Array.Empty<string>();

        /// <summary>目标目录，相对 Assets 根书写，例如 "_Project/Art/Texture"。</summary>
        [JsonPropertyName("目标目录")]
        public string TargetDirectory { get; set; }
    }

    /// <summary>收件箱归档路由表：按扩展名把资产分派到正式目录。</summary>
    public sealed class AssetRoutingTable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>路由表说明，仅供人阅读。</summary>
        [JsonPropertyName("说明")]
        public string Description { get; set; }

        /// <summary>路由条目列表。</summary>
        [JsonPropertyName("路由")]
        public IReadOnlyList<AssetRoutingEntry> Entries { get; set; } = Array.Empty<AssetRoutingEntry>();

        /// <summary>从「归档路由.json」读回一张路由表，文件缺失或格式不对时抛 AssetRoutingException。</summary>
        /// <param name="path">路由文件路径。</param>
        public static AssetRoutingTable LoadFromFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var table = JsonSerializer.Deserialize<AssetRoutingTable>(json, JsonOptions);
                if (table == null)
                {
                    throw new AssetRoutingException(
                        $"位置：{path}；原因：路由表反序列化结果为空；修复：核对「归档路由.json」内容；参考：路由表按扩展名把资产分派到正式目录");
                }

                return table;
            }
            catch (JsonException exception)
            {
                throw new AssetRoutingException(
                    $"位置：{path}；原因：路由表格式错误（{exception.Message}）；修复：把「归档路由.json」改成合法 JSON；参考：路由表按扩展名把资产分派到正式目录",
                    exception);
            }
            catch (IOException exception)
            {
                throw new AssetRoutingException(
                    $"位置：{path}；原因：路由表读取失败（{exception.Message}）；修复：确认文件存在且可读；参考：路由表按扩展名把资产分派到正式目录",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new AssetRoutingException(
                    $"位置：{path}；原因：路由表无权访问（{exception.Message}）；修复：授予读取权限后重试；参考：路由表按扩展名把资产分派到正式目录",
                    exception);
            }
        }

        /// <summary>按扩展名查目标目录，扩展名比较忽略大小写；查不到返回 null。</summary>
        /// <param name="extension">要查的扩展名，例如 ".png"。</param>
        public string FindTargetDirectory(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }

            foreach (var entry in Entries ?? Array.Empty<AssetRoutingEntry>())
            {
                foreach (var candidateExtension in entry.Extensions ?? Array.Empty<string>())
                {
                    if (string.Equals(candidateExtension, extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.TargetDirectory;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>归档路由读取失败时抛出，消息按四要素书写。</summary>
    public sealed class AssetRoutingException : Exception
    {
        /// <summary>用四要素消息构造异常。</summary>
        /// <param name="message">按「位置 / 原因 / 修复 / 参考」四要素书写的消息。</param>
        public AssetRoutingException(string message)
            : base(message)
        {
        }

        /// <summary>用四要素消息与内层异常构造异常。</summary>
        /// <param name="message">按四要素书写的消息。</param>
        /// <param name="innerException">内层异常。</param>
        public AssetRoutingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
