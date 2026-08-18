using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条收件箱扫描结果：文件路径、解析出的信封与失败原因三者齐备。</summary>
    public sealed class InboxScanEntry
    {
        /// <summary>
        /// 构造一条扫描结果。
        /// </summary>
        /// <param name="filePath">信封文件路径。</param>
        /// <param name="envelope">解析成功的信封，失败为 null。</param>
        /// <param name="failureReason">解析失败原因，成功为空串。</param>
        public InboxScanEntry(string filePath, InboxEnvelope envelope, string failureReason)
        {
            FilePath = filePath;
            Envelope = envelope;
            FailureReason = failureReason;
        }

        /// <summary>信封文件路径。</summary>
        public string FilePath { get; }

        /// <summary>解析成功的信封，失败为 null。</summary>
        public InboxEnvelope Envelope { get; }

        /// <summary>解析失败原因，成功为空串。</summary>
        public string FailureReason { get; }
    }

    /// <summary>扫描收件箱目录，把每个 JSON 文件解析成信封条目。</summary>
    public static class InboxScanner
    {
        /// <summary>
        /// 扫描目录下所有顶层 *.json 并逐条解析；目录不存在时返回空列表。
        /// 结果按文件路径的序数序排序，保证多次扫描顺序一致。
        /// </summary>
        /// <param name="inboxDirectory">收件箱目录。</param>
        public static IReadOnlyList<InboxScanEntry> Scan(string inboxDirectory)
        {
            var entries = new List<InboxScanEntry>();
            if (!Directory.Exists(inboxDirectory))
            {
                return entries;
            }

            foreach (var filePath in Directory.EnumerateFiles(inboxDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var envelope = InboxEnvelope.TryRead(filePath, out var parsed, out var failureReason)
                    ? parsed
                    : null;
                entries.Add(new InboxScanEntry(filePath, envelope, failureReason));
            }

            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.FilePath, right.FilePath));
            return entries;
        }
    }
}
