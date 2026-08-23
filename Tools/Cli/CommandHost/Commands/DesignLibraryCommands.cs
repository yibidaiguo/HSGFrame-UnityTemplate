using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>设计库命令的参数。</summary>
    public sealed class DesignLibraryArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>模块名，查锚点时用。</summary>
        [Summary("模块名，如 Inventory；查锚点时用")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>资产类型，查同类时用。</summary>
        [Summary("资产类型，如 UI元素；留空表示不挑类型")]
        [DefaultValue("")]
        public string AssetType { get; set; }

        /// <summary>取几张参考图。</summary>
        [Summary("取几张参考图；0 表示不取（省 token 那一档）")]
        [DefaultValue(1)]
        public int ReferenceImageCount { get; set; }

        /// <summary>只比对不写。</summary>
        [Summary("为 true 时只比对不写文件（幂等门禁用）")]
        [DefaultValue(false)]
        public bool VerifyOnly { get; set; }

        /// <summary>重建索引时算不算主色。</summary>
        [Summary("重建索引时算不算主色；算主色要逐张解码 PNG，几百张时明显变慢")]
        [DefaultValue(true)]
        public bool WithPalette { get; set; }
    }

    /// <summary>
    /// 设计库命令：重建资产索引、查这次该用什么锚点。
    ///
    /// 两条都是确定性的，不调模型——「跟人聊出风格」那一步归助手，
    /// 混进来的话一条本该秒回的查询会变得又慢又花钱。
    /// </summary>
    public static class DesignLibraryCommands
    {
        /// <summary>扫磁盘重建资产库索引。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("design.library.rebuild")]
        [Summary("扫落点重建资产库索引：这个项目已经做过什么")]
        public static CommandResult Rebuild(DesignLibraryArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var index = DesignLibraryIndex.Rebuild(repositoryRoot, arguments.WithPalette);
            var path = DesignLibraryIndex.FilePathFor(repositoryRoot);

            if (arguments.VerifyOnly)
            {
                var expected = index.Render();
                if (!File.Exists(path))
                {
                    return index.Entries.Count == 0
                        ? CommandResult.Success("资产库索引尚未生成，落点里也没有资产——对得上")
                        : CommandResult.Failure(
                            $"资产库索引尚未生成，但落点里有 {index.Entries.Count} 张资产",
                            new[] { "跑一次 design.library.rebuild" });
                }

                return string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal)
                    ? CommandResult.Success($"资产库索引与落点一致（{index.Entries.Count} 条）")
                    : CommandResult.Failure(
                        "资产库索引与落点对不上",
                        new[] { "重跑 design.library.rebuild——对不上的索引比没有索引更糟，它会让「查过了，没有」变成假话" });
            }

            var written = index.Write(repositoryRoot, out var changed, out var reason);
            if (written.Length == 0)
            {
                return CommandResult.Failure("资产库索引写不出：" + reason);
            }

            var lines = new List<string> { $"{(changed ? "已更新" : "无变化")}　{RelativeTo(repositoryRoot, written)}" };
            var byModule = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                var key = entry.Module.Length == 0 ? "（无模块）" : entry.Module;
                byModule[key] = byModule.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            foreach (var pair in byModule)
            {
                lines.Add($"  {pair.Key}　{pair.Value} 张");
            }

            return CommandResult.Success($"资产库索引：{index.Entries.Count} 条", lines);
        }

        /// <summary>查这次出图该带什么锚点：总设计层、色板、负面清单、参考图。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("design.anchor")]
        [Summary("按三档读取策略取风格锚点：默认档（总设计层+定稿）+ 锚点档（参考图）")]
        public static CommandResult Anchor(DesignLibraryArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var anchor = StyleAnchorResolver.Resolve(
                repositoryRoot, arguments.Module ?? "", arguments.AssetType ?? "", arguments.ReferenceImageCount);

            var lines = new List<string>(anchor.Notes);
            foreach (var path in anchor.ReferenceImages)
            {
                lines.Add("  参考图 " + RelativeTo(repositoryRoot, path));
            }

            var fragment = StyleAnchorResolver.ToPromptFragment(anchor);
            lines.Add(fragment.Length > 0 ? "进提示词的那段：" + fragment : "没有可进提示词的锚点");

            return CommandResult.Success(
                anchor.IsColdStart ? "冷启动：还没有可用锚点" : $"锚点已取（定稿 {(anchor.StyleFinalName.Length > 0 ? anchor.StyleFinalName : "无")}）",
                lines);
        }

        /// <summary>把设计库渲成两份给人看的文档：策划一份、美术一份。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("design.library.view")]
        [Summary("渲设计库视图：策划与美术各一份 markdown，推飞书用的就是它们")]
        public static CommandResult View(DesignLibraryArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var index = DesignLibraryIndex.Read(repositoryRoot);
            var notes = new List<string>();

            // **读磁盘上那份索引，不现扫**：视图要跟索引一致，
            // 现扫的话会出现「视图里有、索引里没有」，而门禁比的是索引。
            if (index.Entries.Count == 0)
            {
                notes.Add("索引是空的——先跑 design.library.rebuild");
            }

            if (arguments.VerifyOnly)
            {
                var gameSame = SameOnDisk(DesignLibraryView.GameDocumentPath(repositoryRoot), DesignLibraryView.RenderGame(repositoryRoot));
                var artSame = SameOnDisk(DesignLibraryView.ArtDocumentPath(repositoryRoot), DesignLibraryView.RenderArt(repositoryRoot, index));

                return gameSame && artSame
                    ? CommandResult.Success("设计库视图与仓库一致", notes)
                    : CommandResult.Failure("设计库视图过期了，重跑 design.library.view", notes);
            }

            var written = DesignLibraryView.Write(repositoryRoot, index, notes);
            notes.Add("推给飞书：bridge.ensure 建/取两份文档的 key，再 doc.push 推正文");

            return written
                ? CommandResult.Success("设计库视图已渲", notes)
                : CommandResult.Failure("设计库视图没渲全", notes);
        }

        /// <summary>磁盘上那份跟现渲的一不一样。</summary>
        /// <param name="path">文件路径。</param>
        /// <param name="content">现渲的正文。</param>
        private static bool SameOnDisk(string path, string content)
        {
            try
            {
                return File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>取仓库根：参数给了用参数的，没给用当前目录。</summary>
        /// <param name="arguments">命令参数。</param>
        private static string ResolveRoot(DesignLibraryArguments arguments)
        {
            return string.IsNullOrWhiteSpace(arguments.RepositoryRoot)
                ? Directory.GetCurrentDirectory()
                : arguments.RepositoryRoot;
        }

        /// <summary>把绝对路径缩成相对仓库根的路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="path">绝对路径。</param>
        private static string RelativeTo(string repositoryRoot, string path)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
