using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次出图该带上的风格锚点：默认档读到的东西 + 锚点档的参考图。
    /// </summary>
    /// <param name="DirectionText">总设计层全文；没有时为空串。</param>
    /// <param name="Palette">生效色板（模块级优先，没有则项目级）。</param>
    /// <param name="NegativeList">负面清单，**项目级与模块级取并集**。</param>
    /// <param name="ReferenceImages">参考图的绝对路径，已按 §三 的上限截断。</param>
    /// <param name="StyleFinalName">生效定稿名，进溯源边车；没有时为空串。</param>
    /// <param name="IsColdStart">是不是冷启动——库里什么都没有。</param>
    /// <param name="Notes">这次取了什么、跳过了什么，一句一条，进执行流水。</param>
    public sealed record StyleAnchor(
        string DirectionText,
        IReadOnlyList<string> Palette,
        IReadOnlyList<string> NegativeList,
        IReadOnlyList<string> ReferenceImages,
        string StyleFinalName,
        bool IsColdStart,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// 按三档读取策略（子文档 10 §三）取这次出图要用的风格锚点。
    ///
    /// **默认档只读两样**：总设计层（一份短 md）与模块定稿里的结构化几行（色板、负面清单）。
    /// 设计记录、汇总、别的模块的定稿、历史策划文档一律不读——那些只在人开口时才读。
    /// 设计库越全，「每次全读一遍」越贵，而 token 全花在每次都一样的那部分上。
    ///
    /// **锚点档默认只取 1 张参考图**，不是 3 张：图是 token 大头，
    /// 而锚风格这件事上一张真图已经比十句形容词管用。
    /// </summary>
    public static class StyleAnchorResolver
    {
        /// <summary>默认取几张参考图。1 不是拍脑袋——见类型摘要。</summary>
        public const int DefaultReferenceImageCount = 1;

        /// <summary>
        /// 取锚点。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名；空串表示还没归模块。</param>
        /// <param name="assetType">资产类型，用来查同类；空串表示不挑类型。</param>
        /// <param name="referenceImageCount">取几张参考图；0 表示不取（省 token 的那一档）。</param>
        public static StyleAnchor Resolve(
            string repositoryRoot, string moduleName, string assetType, int referenceImageCount)
        {
            var notes = new List<string>();
            var module = moduleName ?? "";

            // 默认档之一：总设计层。
            var directionText = "";
            if (DesignDirection.TryRead(repositoryRoot, out var direction, out var directionReason))
            {
                if (direction != null && direction.HasContent)
                {
                    directionText = direction.Text;
                    notes.Add($"总设计层：{direction.LineCount} 行");
                }
                else
                {
                    notes.Add("还没有总设计层");
                }
            }
            else
            {
                notes.Add(directionReason);
            }

            // 默认档之二：定稿里的结构化几行。项目级与模块级都读——
            // 加起来几十个 token，跟总设计层不是一个量级，放进默认档不心疼。
            ArtStyleFinal.TryRead(ArtStyleFinal.ProjectFilePath(repositoryRoot), "", out var project, out _);

            ArtStyleFinal moduleFinal = null;
            if (module.Length > 0)
            {
                ArtStyleFinal.TryRead(ArtStyleFinal.ModuleFilePath(repositoryRoot, module), module, out moduleFinal, out _);
            }

            // 色板：模块级优先——它本来就是项目级的子集（门禁查），取它更准。
            var palette = moduleFinal != null && moduleFinal.Palette.Count > 0
                ? moduleFinal.Palette
                : (project?.Palette ?? Array.Empty<string>());

            // 负面清单**取并集**：只取模块那份的话，一个模块的疏忽就能把项目级约束整条丢掉。
            var negative = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in new[] { project?.NegativeList, moduleFinal?.NegativeList })
            {
                foreach (var item in source ?? Array.Empty<string>())
                {
                    if (seen.Add(item.Trim()))
                    {
                        negative.Add(item);
                    }
                }
            }

            var styleFinalName = moduleFinal?.Name ?? project?.Name ?? "";
            if (styleFinalName.Length > 0)
            {
                notes.Add($"定稿：{styleFinalName}（色板 {palette.Count} 色，负面 {negative.Count} 条）");
            }

            // 锚点档：同模块已有资产当参考图。
            var references = new List<string>();
            if (referenceImageCount > 0)
            {
                var index = DesignLibraryIndex.Read(repositoryRoot);
                foreach (var entry in index.FindSimilar(module, assetType ?? "", referenceImageCount))
                {
                    var path = Path.Combine(
                        repositoryRoot, "UnityProject", entry.Destination.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(path))
                    {
                        references.Add(path);
                    }
                }

                notes.Add(references.Count > 0
                    ? $"参考图：{references.Count} 张（同模块已有资产）"
                    : "库里还没有同类资产可当参考图");
            }

            // 冷启动判据（子文档 10 §四）：总设计层没有，或者（模块定稿没有且同类资产为 0）。
            var isColdStart = directionText.Length == 0
                || (moduleFinal == null && references.Count == 0 && palette.Count == 0);

            if (isColdStart)
            {
                notes.Add("冷启动：库里没有可用锚点，该先跟人聊一轮（不许现编一个方向塞进定稿）");
            }

            return new StyleAnchor(directionText, palette, negative, references, styleFinalName, isColdStart, notes);
        }

        /// <summary>
        /// 把锚点拼成一段能直接追加进提示词的文字。
        /// **参考图不在这里**——它走「参考图」那条入参（图生图的锚点槽），不是文字。
        /// 没有任何锚点时返回空串，让调用方如实说「这一批没有风格锚点」，而不是拼一段空话。
        /// </summary>
        /// <param name="anchor">锚点。</param>
        public static string ToPromptFragment(StyleAnchor anchor)
        {
            if (anchor == null)
            {
                return "";
            }

            var parts = new List<string>();
            if (anchor.Palette.Count > 0)
            {
                parts.Add("配色贴近这几个色：" + string.Join("、", anchor.Palette));
            }

            if (anchor.NegativeList.Count > 0)
            {
                parts.Add("明确不要：" + string.Join("；", anchor.NegativeList));
            }

            return parts.Count == 0 ? "" : string.Join("。", parts);
        }
    }
}
