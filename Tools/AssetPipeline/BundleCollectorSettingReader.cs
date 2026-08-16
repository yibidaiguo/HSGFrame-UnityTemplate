using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>收集器配置里的一个 group：组名与它底下的收集路径。</summary>
    public sealed class BundleCollectorGroupEntry
    {
        /// <summary>构造一个 group 条目。</summary>
        /// <param name="groupName">组名，已还原过 Unity 的 \uXXXX 转义。</param>
        /// <param name="collectPaths">这个组底下的收集路径，按文件里出现的顺序。</param>
        public BundleCollectorGroupEntry(string groupName, IReadOnlyList<string> collectPaths)
        {
            GroupName = groupName;
            CollectPaths = collectPaths ?? Array.Empty<string>();
        }

        /// <summary>组名。</summary>
        public string GroupName { get; }

        /// <summary>这个组底下的收集路径。</summary>
        public IReadOnlyList<string> CollectPaths { get; }
    }

    /// <summary>
    /// 把 YooAsset 的 <c>BundleCollectorSetting.asset</c> 读成「组名 → 收集路径」。
    /// 只认 <c>GroupName</c> 与 <c>CollectPath</c> 两个键，逐行扫——引一个 YAML 库来读这两行不划算，
    /// 而且 Unity 写出来的这份文件形状是固定的。
    /// </summary>
    public static class BundleCollectorSettingReader
    {
        private const string GroupNameMarker = "- GroupName:";
        private const string CollectPathMarker = "- CollectPath:";

        /// <summary>读出配置里的全部 group；文件不存在或路径为空时返回空清单，不抛异常。</summary>
        /// <param name="settingFilePath">收集器配置文件路径。</param>
        public static IReadOnlyList<BundleCollectorGroupEntry> Read(string settingFilePath)
        {
            if (string.IsNullOrWhiteSpace(settingFilePath) || !File.Exists(settingFilePath))
            {
                return Array.Empty<BundleCollectorGroupEntry>();
            }

            var entries = new List<BundleCollectorGroupEntry>();
            string currentGroupName = null;
            var currentPaths = new List<string>();

            foreach (var rawLine in File.ReadLines(settingFilePath))
            {
                var line = rawLine.TrimStart();
                if (line.StartsWith(GroupNameMarker, StringComparison.Ordinal))
                {
                    if (currentGroupName != null)
                    {
                        entries.Add(new BundleCollectorGroupEntry(currentGroupName, currentPaths.ToArray()));
                    }

                    currentGroupName = DecodeScalar(line.Substring(GroupNameMarker.Length));
                    currentPaths = new List<string>();
                    continue;
                }

                if (currentGroupName != null && line.StartsWith(CollectPathMarker, StringComparison.Ordinal))
                {
                    currentPaths.Add(DecodeScalar(line.Substring(CollectPathMarker.Length)));
                }
            }

            if (currentGroupName != null)
            {
                entries.Add(new BundleCollectorGroupEntry(currentGroupName, currentPaths.ToArray()));
            }

            return entries;
        }

        // Unity 对含非 ASCII 的值会写成带双引号的 \uXXXX 转义，纯 ASCII 则原样不加引号，两种都要认。
        private static string DecodeScalar(string rawValue)
        {
            var value = rawValue.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }

            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '\\' || index + 1 >= value.Length)
                {
                    builder.Append(value[index]);
                    continue;
                }

                var escape = value[index + 1];
                if ((escape == 'u' || escape == 'U') && index + 5 < value.Length
                    && ushort.TryParse(
                        value.Substring(index + 2, 4),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var codePoint))
                {
                    builder.Append((char)codePoint);
                    index += 5;
                    continue;
                }

                if (escape == '\\' || escape == '"')
                {
                    builder.Append(escape);
                    index += 1;
                    continue;
                }

                builder.Append(value[index]);
            }

            return builder.ToString();
        }
    }
}
