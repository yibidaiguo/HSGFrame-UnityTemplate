using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>资产清单里的一条：这个元素要不要出图、出到哪、为什么不出。</summary>
    /// <param name="ElementIdentifier">元素 id。</param>
    /// <param name="ElementType">元素类型。</param>
    /// <param name="Naming">这张图的命名（T_ 前缀 + PascalCase）。</param>
    /// <param name="Destination">落点目录（已带模块或 Shared）。</param>
    /// <param name="Width">要的像素宽。</param>
    /// <param name="Height">要的像素高。</param>
    /// <param name="RepeatCount">同款几个（只出一张，其余是实例）。</param>
    /// <param name="Action">出图 / 复用已有 / 不出图。</param>
    /// <param name="Reason">为什么是这个处置，一句人话。</param>
    public sealed record InterfaceAssetEntry(
        string ElementIdentifier,
        string ElementType,
        string Naming,
        string Destination,
        int Width,
        int Height,
        int RepeatCount,
        string Action,
        string Reason);

    /// <summary>
    /// 从界面规格算出资产清单：**一屏要真出几张图，在这里定**。
    ///
    /// 一屏从「画面上能框出来的一百多个」收敛到「真正要出的二十几个」，靠的是三条，
    /// 而不是靠给切图算法加阈值：
    ///
    /// 1. **有些类型根本不出图**——Label 的文案由 UI Toolkit 出（生图模型写不对字）、
    ///    Container 的底图是另一个元素、Decoration 属于底图的一部分（单独切只会往图集里塞碎图）；
    /// 2. **重复件只出一张**——四个一样的格子是一个资产的四个实例，不是四个资产；
    /// 3. **通用件先查库**——`Art/Texture/Ui/Shared/` 里有同名的就复用。
    ///    没有这一条的话 `Shared/` 建了也会空着：没有任何环节会往里放东西。
    /// </summary>
    public static class InterfaceAssetManifest
    {
        /// <summary>处置：真去出一张图。</summary>
        public const string ActionGenerate = "出图";

        /// <summary>处置：库里已经有了，复用。</summary>
        public const string ActionReuse = "复用已有";

        /// <summary>处置：这一类不出图。</summary>
        public const string ActionSkip = "不出图";

        /// <summary>UI 贴图的落点根：Assets/Game/Art/Texture/Ui/。</summary>
        private const string UiTextureRoot = "Assets/Game/Art/Texture/Ui";

        /// <summary>通用件的目录名。《结构规范-资源》第四节：跨模块通用资产住 Shared/。</summary>
        private const string SharedFolderName = "Shared";

        /// <summary>
        /// 算这一屏的资产清单。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（查已有资产用）。</param>
        /// <param name="spec">界面规格。</param>
        /// <param name="catalog">元素类型模板目录（决定哪类要出图）。</param>
        public static IReadOnlyList<InterfaceAssetEntry> Build(
            string repositoryRoot, InterfaceSpec spec, UiElementTemplateCatalog catalog)
        {
            var entries = new List<InterfaceAssetEntry>();
            if (spec == null)
            {
                return entries;
            }

            var moduleFolder = spec.PanelName.Length > 0 ? spec.PanelName : "";

            foreach (var element in spec.Elements)
            {
                var template = catalog?.Find(element.ElementType);
                if (template == null || !template.NeedsImage)
                {
                    entries.Add(new InterfaceAssetEntry(
                        element.Identifier, element.ElementType, "", "", 0, 0, element.RepeatCount,
                        ActionSkip,
                        template == null
                            ? "类型没有模板，不敢替它决定出不出图"
                            : element.ElementType + " 这一类不出图"));
                    continue;
                }

                element.ReadLayout(out _, out _, out var width, out var height);
                var naming = "T_" + element.Identifier;
                var folder = element.IsShared ? SharedFolderName : moduleFolder;
                var destination = folder.Length > 0 ? UiTextureRoot + "/" + folder + "/" : UiTextureRoot + "/";

                // 通用件先查库。查的是**落点里有没有同名文件**——
                // 感知哈希那种「长得像不像」留到以后，先把「同一个名字只出一次」做扎实。
                if (element.IsShared && ExistingAsset(repositoryRoot, destination, naming))
                {
                    entries.Add(new InterfaceAssetEntry(
                        element.Identifier, element.ElementType, naming, destination, width, height,
                        element.RepeatCount, ActionReuse,
                        "通用件，" + destination + naming + ".png 已经有了"));
                    continue;
                }

                var reason = element.RepeatCount > 1
                    ? "同款 " + element.RepeatCount + " 个，只出一张，其余是同一资产的实例"
                    : "";

                entries.Add(new InterfaceAssetEntry(
                    element.Identifier, element.ElementType, naming, destination, width, height,
                    element.RepeatCount, ActionGenerate, reason));
            }

            return entries;
        }

        /// <summary>这一屏真要发几次生图调用。**卡片上要摆的就是这个数**，不是元素总数。</summary>
        /// <param name="entries">资产清单。</param>
        public static int CountToGenerate(IReadOnlyList<InterfaceAssetEntry> entries)
        {
            var count = 0;
            foreach (var entry in entries ?? Array.Empty<InterfaceAssetEntry>())
            {
                if (string.Equals(entry.Action, ActionGenerate, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>落点里有没有这张图。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="destination">落点目录（Assets/ 起头的工程内相对路径）。</param>
        /// <param name="naming">文件名（不含扩展名）。</param>
        private static bool ExistingAsset(string repositoryRoot, string destination, string naming)
        {
            try
            {
                var path = Path.Combine(
                    repositoryRoot,
                    "UnityProject",
                    destination.Replace('/', Path.DirectorySeparatorChar),
                    naming + ".png");
                return File.Exists(path);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException)
            {
                // 查不动就当没有：多出一张图是浪费，少出一张是缺件——两害相权取其轻。
                return false;
            }
        }
    }
}
