using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 「你能读哪些文件」的清单——只有路径，没有内容。
    ///
    /// **不给清单，模型就会编路径。** 它知道自己能读代码，于是猜一个
    /// `UnityProject/Assets/Game/Scripts/Inventory/Inventory.cs`，
    /// 引擎回一句「这个文件不在」，一轮就白花了。给一份清单，
    /// 它点的每个路径都是真存在的。
    ///
    /// 只列路径是刻意的：一个中等项目几百个文件，路径清单几 KB，
    /// 而内容是几 MB。**要哪个由它点名，引擎按需去读**——
    /// 这正是「读代码」这一支存在的理由。
    /// </summary>
    public static class ReadableFileIndex
    {
        /// <summary>清单里最多列几个文件。超了按目录截断并如实说。</summary>
        public const int MaximumEntryCount = 600;

        /// <summary>
        /// 收集可读文件的相对路径。
        ///
        /// 判据与 <see cref="ProjectCodeReader"/> **共用同一个** `TryResolve`——
        /// 两处各写一遍的话，清单里会出现读不了的路径，
        /// 而模型照着清单点名却被拒，那种错读起来像引擎坏了。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<string> Collect(string repositoryRoot)
        {
            var result = new List<string>();
            var rootFullPath = Path.GetFullPath(repositoryRoot);

            foreach (var directory in ProjectCodeReader.AllowedRoots(repositoryRoot))
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                }
                catch (Exception exception)
                    when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    continue;
                }

                Array.Sort(files, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    if (result.Count >= MaximumEntryCount)
                    {
                        return result;
                    }

                    var relative = Path.GetRelativePath(rootFullPath, file).Replace('\\', '/');
                    if (ProjectCodeReader.TryResolve(rootFullPath, relative, out _, out _, out _))
                    {
                        result.Add(relative);
                    }
                }
            }

            return result;
        }

        /// <summary>把清单渲成一份 markdown，进知识包。</summary>
        /// <param name="paths">可读路径。</param>
        public static string Render(IReadOnlyList<string> paths)
        {
            var builder = new StringBuilder();
            builder.Append("# 你能读哪些文件\n\n");
            builder.Append("要看某个文件的内容时，填「要什么」: \"读代码\"，");
            builder.Append("把下面的路径写进「读代码请求.文件」（一次最多 6 个）。\n\n");
            builder.Append("**路径照抄这份清单，别自己拼**——拼错了引擎只会回一句「这个文件不在」，一轮白花。\n\n");

            if (paths == null || paths.Count == 0)
            {
                builder.Append("（一个都没有：这个仓库里那几个可读目录还是空的。）\n");
                return builder.ToString();
            }

            var currentDirectory = "";
            foreach (var path in paths)
            {
                var slash = path.LastIndexOf('/');
                var directory = slash < 0 ? "" : path.Substring(0, slash);
                if (!string.Equals(directory, currentDirectory, StringComparison.Ordinal))
                {
                    currentDirectory = directory;
                    builder.Append("\n## ").Append(directory.Length == 0 ? "（根目录）" : directory).Append('\n');
                }

                builder.Append("- ").Append(path).Append('\n');
            }

            if (paths.Count >= MaximumEntryCount)
            {
                builder.Append("\n（列到 ").Append(MaximumEntryCount)
                    .Append(" 个上限就停了，后面的没列——要看没列出来的，直接说文件名，我去找。）\n");
            }

            return builder.ToString();
        }
    }
}
