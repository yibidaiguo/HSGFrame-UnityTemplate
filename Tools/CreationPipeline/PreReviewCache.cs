using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 预审报告缓存（决策 90）：按输入哈希缓存，命中不重判。
    /// 缓存键 = SHA256(提示词全文 + 模型名 + 提示词版本)——少模型名或提示词版本就是错的：
    /// 换了模型还命中旧缓存，报告就在说谎。
    /// 缓存文件落 <c>_Tasks/.prereview-cache/&lt;哈希&gt;.json</c>，内容就是报告 JSON。
    /// </summary>
    public static class PreReviewCache
    {
        /// <summary>缓存目录名（相对 _Tasks）。</summary>
        public const string CacheDirectoryName = ".prereview-cache";

        /// <summary>缓存目录：_Tasks/.prereview-cache。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string CacheDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "_Tasks", CacheDirectoryName);
        }

        /// <summary>
        /// 算缓存键：SHA256(提示词全文 + 模型名 + 提示词版本)，十六进制小写。
        /// 用换行符隔开三份输入，防止「提示词末尾 + 模型名」与另一份输入的边界歧义。
        /// </summary>
        /// <param name="promptText">提示词全文。</param>
        /// <param name="modelName">请求时配置的模型名。</param>
        /// <param name="promptVersion">提示词版本。</param>
        public static string ComputeKey(string promptText, string modelName, string promptVersion)
        {
            var combined = string.Concat(promptText ?? "", "\n", modelName ?? "", "\n", promptVersion ?? "");
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>缓存文件路径：_Tasks/.prereview-cache/&lt;哈希&gt;.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="key">缓存键。</param>
        public static string CacheFile(string repositoryRoot, string key)
        {
            return Path.Combine(CacheDirectory(repositoryRoot), key + ".json");
        }

        /// <summary>
        /// 按缓存键读缓存报告；命中时返回的报告标 <c>来自缓存=true</c>。
        /// 缓存文件不存在、坏掉或形状不对一律视为未命中（返回 false），绝不把坏缓存当结论。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="key">缓存键。</param>
        /// <param name="report">命中时的报告（来自缓存=true）。</param>
        public static bool TryLoad(string repositoryRoot, string key, out PreReviewReport report)
        {
            report = null;
            var filePath = CacheFile(repositoryRoot, key);
            if (!File.Exists(filePath))
            {
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }

            var loaded = PreReviewReport.TryFromJson(json);
            if (loaded == null)
            {
                return false;
            }

            // 缓存里存的「来自缓存」是第一次落盘时的值，命中的报告必须重标成 true。
            report = loaded.AsStamped(loaded.Timestamp, fromCache: true);
            return true;
        }

        /// <summary>把报告写进缓存：_Tasks/.prereview-cache/&lt;键&gt;.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="key">缓存键。</param>
        /// <param name="report">要缓存的报告。</param>
        public static void Save(string repositoryRoot, string key, PreReviewReport report)
        {
            var directory = CacheDirectory(repositoryRoot);
            Directory.CreateDirectory(directory);
            var filePath = CacheFile(repositoryRoot, key);
            File.WriteAllText(filePath, report.ToJson(), new UTF8Encoding(false));
        }
    }
}
