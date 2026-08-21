using System;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 本机下游配置：从 <c>Tools/CreationPipeline/Config/local.json</c> 读出，文件在 .gitignore 里、可能根本不存在。
    /// 文件不存在是正常状态（Loaded=true、内容为空）；文件存在但解析失败才是坏（Loaded=false）。
    ///
    /// 密钥红线（决策 5、78）：本文件是「项目的 AI 钱包」，里面存的是密钥值。
    /// 密钥值只许出现在 <see cref="TryGetSecret"/> 的 out 参数与请求信封的「配置」里；
    /// 不许进任何日志、异常消息、ToString()、返回文案；
    /// 也不许写「密钥长度」「前四位」这类看着无害的东西——那也是泄露。
    /// </summary>
    public sealed class LocalBridgeSettings
    {
        /// <summary>文件不存在时 TryGet 给出的原因。</summary>
        public const string MissingFileReason = "本机配置不存在";

        /// <summary>
        /// 构造一份本机配置。
        /// </summary>
        /// <param name="loaded">文件存在且解析成功，或文件不存在；false 表示文件坏掉。</param>
        /// <param name="root">顶层 JSON 对象；文件不存在时为空对象。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为 ""。</param>
        public LocalBridgeSettings(bool loaded, JsonElement root, string loadFailureReason)
        {
            Loaded = loaded;
            Root = root;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>文件存在且解析成功，或文件不存在；false 表示文件坏掉。</summary>
        public bool Loaded { get; }

        /// <summary>顶层 JSON 对象；文件不存在时是空对象。</summary>
        public JsonElement Root { get; }

        /// <summary>文件坏掉时的原因；正常时为 ""。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 从仓库根读本机配置。文件不存在 → Loaded=true、内容为空（正常状态）；
        /// 文件存在但 JSON 坏掉或顶层不是对象 → Loaded=false、reason 写清坏在哪。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static LocalBridgeSettings Load(string repositoryRoot)
        {
            var filePath = SettingsFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new LocalBridgeSettings(true, default, "");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new LocalBridgeSettings(false, default, $"本机配置不是合法 JSON：{filePath}：{exception.Message}");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new LocalBridgeSettings(false, default, $"本机配置不是合法 JSON：{filePath}（顶层必须是对象）");
                }

                // Clone 一份让它在 document 释放后仍可读。
                return new LocalBridgeSettings(true, document.RootElement.Clone(), "");
            }
        }

        /// <summary>
        /// 取某 driver 的本机配置对象（<c>下游配置.&lt;driver&gt;</c>）。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="configuration">命中的配置对象；没配时为 default。</param>
        public bool TryGetDriverConfiguration(string driverName, out JsonElement configuration)
        {
            configuration = default;
            if (!Loaded || Root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!Root.TryGetProperty("下游配置", out var downstream)
                || downstream.ValueKind != JsonValueKind.Object
                || !downstream.TryGetProperty(driverName ?? "", out configuration)
                || configuration.ValueKind != JsonValueKind.Object)
            {
                configuration = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 取顶层密钥值。密钥值只许从这个 out 参数出去，调用方只许把它拼进请求信封的「配置」，
        /// 不许写进日志、异常消息或返回文案（决策 5、78）。
        /// </summary>
        /// <param name="secretFieldName">密钥字段名，如「飞书应用密钥」。</param>
        /// <param name="value">密钥值；没配时为 ""。</param>
        public bool TryGetSecret(string secretFieldName, out string value)
        {
            value = "";
            if (!Loaded || Root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!Root.TryGetProperty(secretFieldName ?? "", out var element) || element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>本机配置文件的路径：Tools/CreationPipeline/Config/local.json（在 .gitignore 里）。</summary>
        internal static string SettingsFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json");
        }
    }
}
