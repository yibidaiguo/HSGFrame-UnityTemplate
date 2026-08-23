using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把设计库渲成给人看的两份文档：策划一份、美术一份。
    ///
    /// **两份分开是刻意的**（子文档 09 §一）：「断签从第 1 天重计」和「UI 图标一律 Q 版」
    /// 是两种东西——读者不同、改的人不同、过期的方式也不同。
    /// 堆在一份里，两边都得从一堆不相干的条目里筛自己那部分。
    ///
    /// 这两份是**视图不是事实源**（铁律：池子是唯一事实源，下游皆视图）。
    /// 它们从仓库里的东西现渲出来，改它们不会改任何事实——所以顶上写死一句
    /// 「这是生成的」，免得有人在飞书上直接编辑然后奇怪为什么下次全没了。
    /// </summary>
    public static class DesignLibraryView
    {
        /// <summary>策划设计库文档的落点：_Generated/DesignLibrary/game.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string GameDocumentPath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Generated", "DesignLibrary", "game.md");
        }

        /// <summary>美术设计库文档的落点：_Generated/DesignLibrary/art.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ArtDocumentPath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Generated", "DesignLibrary", "art.md");
        }

        /// <summary>头一行的免责话，两份共用。</summary>
        private const string GeneratedNotice =
            "> 这份是**生成的视图**，改它不会改任何事实——事实在仓库里。\n"
            + "> 要改内容去改仓库里的源文件，然后重跑 `design.library.view`。\n";

        /// <summary>
        /// 渲策划设计库：总设计层 + 每个模块的设计记录摘要 + 术语表。
        ///
        /// 眼下 `Pools/Designs/Game/` 那一层还没有任何写入方——
        /// **所以这份多半是空的，而它就该如实显示为空**。
        /// 拿别处的东西填满它，只会让人以为策划库已经在用了。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string RenderGame(string repositoryRoot)
        {
            var builder = new StringBuilder();
            builder.Append("# 策划设计库\n\n").Append(GeneratedNotice).Append('\n');

            if (DesignDirection.TryRead(repositoryRoot, out var direction, out _) && direction != null && direction.HasContent)
            {
                builder.Append("## 总设计\n\n").Append(direction.Text.TrimEnd()).Append("\n\n");
            }
            else
            {
                builder.Append("## 总设计\n\n还没有定。第一次要出东西时助手会问一句；"
                    + "不想现在定就先放着，**不会硬塞一个编出来的方向**。\n\n");
            }

            var gameRoot = Path.Combine(repositoryRoot, "Pools", "Designs", "Game");
            var modules = ListDirectories(gameRoot);

            builder.Append("## 各模块的当前设计\n\n");
            if (modules.Count == 0)
            {
                builder.Append("还没有任何模块的设计记录。\n\n");
            }
            else
            {
                foreach (var module in modules)
                {
                    builder.Append("### ").Append(module).Append("\n\n");
                    var digest = Path.Combine(gameRoot, module, "digest.md");
                    builder.Append(ReadOrPlaceholder(digest, "这个模块还没有生成过汇总。")).Append("\n\n");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 渲美术设计库：定稿（色板 / 负面清单）+ 资产清单（按模块分组）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="index">资产库索引。</param>
        public static string RenderArt(string repositoryRoot, DesignLibraryIndex index)
        {
            var builder = new StringBuilder();
            builder.Append("# 美术设计库\n\n").Append(GeneratedNotice).Append('\n');

            ArtStyleFinal.TryRead(ArtStyleFinal.ProjectFilePath(repositoryRoot), "", out var project, out _);
            builder.Append("## 项目级定稿\n\n");
            AppendFinal(builder, project, "还没有定过项目级风格。");

            var byModule = new SortedDictionary<string, List<DesignLibraryEntry>>(StringComparer.Ordinal);
            foreach (var entry in index?.Entries ?? Array.Empty<DesignLibraryEntry>())
            {
                var key = entry.Module.Length == 0 ? "（无模块）" : entry.Module;
                if (!byModule.TryGetValue(key, out var list))
                {
                    list = new List<DesignLibraryEntry>();
                    byModule[key] = list;
                }

                list.Add(entry);
            }

            builder.Append("## 各模块\n\n");
            if (byModule.Count == 0)
            {
                builder.Append("库里还没有任何资产。第一次出图之后就有了。\n\n");
                return builder.ToString();
            }

            foreach (var pair in byModule)
            {
                builder.Append("### ").Append(pair.Key).Append('\n').Append('\n');

                if (!string.Equals(pair.Key, "（无模块）", StringComparison.Ordinal))
                {
                    ArtStyleFinal.TryRead(
                        ArtStyleFinal.ModuleFilePath(repositoryRoot, pair.Key), pair.Key, out var moduleFinal, out _);
                    AppendFinal(builder, moduleFinal, "这个模块还没有单独的定稿，跟项目级走。");
                }

                builder.Append("| 命名 | 类型 | 产出方式 | 主色 | 落点 |\n");
                builder.Append("|---|---|---|---|---|\n");
                foreach (var entry in pair.Value)
                {
                    builder.Append("| ").Append(entry.Naming)
                        .Append(" | ").Append(entry.AssetType.Length == 0 ? "—" : entry.AssetType)
                        .Append(" | ").Append(entry.Origin.Length == 0 ? "—" : entry.Origin)
                        .Append(" | ").Append(entry.Palette.Count == 0 ? "—" : string.Join(" ", entry.Palette))
                        .Append(" | `").Append(entry.Destination).Append("` |\n");
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 两份都写到磁盘。内容没变就不动文件——无谓的重写会让 git 里多出没有实质改动的 diff，
        /// 也让「重渲无 diff」那道幂等门禁能成立。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="index">资产库索引。</param>
        /// <param name="notes">写了什么，一句一条。</param>
        public static bool Write(string repositoryRoot, DesignLibraryIndex index, List<string> notes)
        {
            var written = true;
            written &= WriteOne(GameDocumentPath(repositoryRoot), RenderGame(repositoryRoot), "策划设计库", notes);
            written &= WriteOne(ArtDocumentPath(repositoryRoot), RenderArt(repositoryRoot, index), "美术设计库", notes);
            return written;
        }

        /// <summary>写一份，没变就不动。</summary>
        /// <param name="path">落点。</param>
        /// <param name="content">正文。</param>
        /// <param name="label">给人看的名字。</param>
        /// <param name="notes">流水。</param>
        private static bool WriteOne(string path, string content, string label, List<string> notes)
        {
            try
            {
                if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                {
                    notes?.Add($"{label}：无变化");
                    return true;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content, new UTF8Encoding(false));
                notes?.Add($"{label}：已更新");
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                notes?.Add($"{label} 写不下去：{exception.Message}");
                return false;
            }
        }

        /// <summary>把一份定稿摆成几行；没有就写那句占位话。</summary>
        /// <param name="builder">输出缓冲。</param>
        /// <param name="final">定稿；可为 null。</param>
        /// <param name="placeholder">没有时写什么。</param>
        private static void AppendFinal(StringBuilder builder, ArtStyleFinal final, string placeholder)
        {
            if (final == null)
            {
                builder.Append(placeholder).Append("\n\n");
                return;
            }

            builder.Append("- **").Append(final.Name.Length == 0 ? "（没名字）" : final.Name)
                .Append("**（v").Append(final.Version).Append("，来源：").Append(final.Origin).Append(")\n");

            if (final.Palette.Count > 0)
            {
                builder.Append("- 色板：").Append(string.Join(" ", final.Palette)).Append('\n');
            }

            if (final.NegativeList.Count > 0)
            {
                builder.Append("- 明确不要：").Append(string.Join("；", final.NegativeList)).Append('\n');
            }

            if (final.ReferenceImages.Count > 0)
            {
                builder.Append("- 参考图：").Append(string.Join("、", final.ReferenceImages)).Append('\n');
            }

            builder.Append('\n');
        }

        /// <summary>列子目录名，排过序——不排的话重渲会出无谓的 diff。</summary>
        /// <param name="root">父目录。</param>
        private static IReadOnlyList<string> ListDirectories(string root)
        {
            var names = new List<string>();
            if (!Directory.Exists(root))
            {
                return names;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                names.Add(Path.GetFileName(directory));
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>读一份文件；不在就给占位话。</summary>
        /// <param name="path">文件路径。</param>
        /// <param name="placeholder">不在时写什么。</param>
        private static string ReadOrPlaceholder(string path, string placeholder)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).TrimEnd() : placeholder;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "（读不动：" + exception.Message + "）";
            }
        }
    }
}
