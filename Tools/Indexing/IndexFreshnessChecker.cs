using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.Indexing
{
    /// <summary>逐类重算源哈希与索引文件比对，报告缺失或过期的索引。</summary>
    public static class IndexFreshnessChecker
    {
        /// <summary>校验全部索引的新鲜度，全新鲜时返回空列表。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="configuration">索引配置。</param>
        public static IReadOnlyList<string> Check(string repositoryRoot, IndexConfiguration configuration)
        {
            var problems = new List<string>();

            foreach (var definition in configuration.Definitions)
            {
                var outputPath = Path.Combine(repositoryRoot, definition.OutputPath);
                if (!File.Exists(outputPath))
                {
                    problems.Add($"索引「{definition.IndexName}」索引尚未生成：{definition.OutputPath}，请运行 index.rebuild 重建");
                    continue;
                }

                var existing = IndexDocument.LoadFromFile(outputPath);
                var current = IndexBuilder.Build(repositoryRoot, definition);
                if (!string.Equals(existing.SourceHash, current.SourceHash, StringComparison.Ordinal))
                {
                    problems.Add($"索引「{definition.IndexName}」索引已过期：{definition.OutputPath}，请运行 index.rebuild 重建");
                }
            }

            return problems;
        }
    }
}
