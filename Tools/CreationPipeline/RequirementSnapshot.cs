using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次需求快照的落点：版次与文件路径。</summary>
    public sealed class RequirementSnapshot
    {
        /// <summary>
        /// 构造一次需求快照落点。
        /// </summary>
        /// <param name="version">版次，从 1 起。</param>
        /// <param name="filePath">快照文件路径。</param>
        internal RequirementSnapshot(int version, string filePath)
        {
            Version = version;
            FilePath = filePath ?? "";
        }

        /// <summary>版次。</summary>
        public int Version { get; }

        /// <summary>快照文件路径。</summary>
        public string FilePath { get; }
    }

    /// <summary>
    /// 需求快照存取：取当前版次、把需求原文按新版次落盘。
    /// 快照只追加、既有版本文件永不覆盖——快照是基准，改基准等于把「当初照着什么做的」抹掉。
    /// </summary>
    public static class RequirementSnapshotStore
    {
        /// <summary>
        /// 扫任务目录里所有 00-需求.v&lt;N&gt;.json（N 是一位以上数字），返回最大的 N；
        /// 一个都没有返回 0，目录不存在也返回 0。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static int CurrentVersion(string repositoryRoot, string requirementIdentifier)
        {
            var directory = PipelinePaths.TaskDirectory(repositoryRoot, requirementIdentifier);
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var maxVersion = 0;
            foreach (var fileName in Directory.EnumerateFiles(directory, "00-需求.v*.json", SearchOption.TopDirectoryOnly))
            {
                var match = SnapshotFilePattern.Match(Path.GetFileName(fileName));
                if (match.Success && int.TryParse(match.Groups[1].Value, out var version) && version > maxVersion)
                {
                    maxVersion = version;
                }
            }

            return maxVersion;
        }

        /// <summary>
        /// 把需求原文按新版次（当前版次 + 1）落盘：目录不存在先建，原文逐字节原样写入
        /// （不重新序列化、不美化、不排键——快照的价值就在于它是当时那份原文）。
        /// 新版次的文件已经存在（并发或脏数据）时抛 InvalidOperationException，既有版本文件永不覆盖。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="requirementJsonText">需求原文，逐字节原样写入。</param>
        public static RequirementSnapshot Capture(string repositoryRoot, string requirementIdentifier, string requirementJsonText)
        {
            var newVersion = CurrentVersion(repositoryRoot, requirementIdentifier) + 1;
            var filePath = PipelinePaths.RequirementSnapshotFile(repositoryRoot, requirementIdentifier, newVersion);
            if (File.Exists(filePath))
            {
                throw new InvalidOperationException($"快照 v{newVersion} 已存在，不许覆盖");
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, requirementJsonText ?? "", new UTF8Encoding(false));
            return new RequirementSnapshot(newVersion, filePath);
        }

        /// <summary>快照文件名：00-需求.v&lt;N&gt;.json，N 是一位以上数字。</summary>
        private static readonly Regex SnapshotFilePattern = new Regex(@"^00-需求\.v(\d+)\.json$", RegexOptions.Compiled);
    }
}
