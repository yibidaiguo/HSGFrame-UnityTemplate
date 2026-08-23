using System;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把界面布局图交给需求案：从 `_Generated/Interfaces/` 拷一份进这条需求的 `media/`。
    ///
    /// **为什么要拷而不是直接引 `_Generated/`**：md 文档只存引用，引到哪就得保证那儿一直有。
    /// `_Generated/` 是本机产物、进 .gitignore，换台机器 clone 下来那张图不在——
    /// 而需求案是要推给别人看的，图断了整份文档就废了一半。
    /// `media/` 跟着需求进 git（决策 99 那一族），拷进去才是「这条需求自带的一张图」。
    ///
    /// 文件名走 ASCII（决策 1）：`media/UI-0001-layout.png`。
    /// </summary>
    public static class InterfaceLayoutMedia
    {
        /// <summary>媒体目录里的文件名后缀，拼在界面 id 后面。</summary>
        private const string FileNameSuffix = "-layout.png";

        /// <summary>某份界面规格在 media/ 里的相对路径（相对 index.md，正斜杠）。</summary>
        /// <param name="interfaceIdentifier">界面 id。</param>
        public static string RelativePathFor(string interfaceIdentifier)
        {
            return PoolPaths.RequirementMediaDirectoryName + "/" + (interfaceIdentifier ?? "") + FileNameSuffix;
        }

        /// <summary>
        /// 把布局位图拷进这条需求的 media/。
        ///
        /// 拷不成**不抛异常**，回空串加一句原因：布局图是需求案里的一张插图，
        /// 它没拷进去不该让「出功能图」这件事整体算失败——规格与清单都还好好的。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id；空表示这份规格没归需求，直接跳过。</param>
        /// <param name="interfaceIdentifier">界面 id。</param>
        /// <param name="rasterPath">位图源文件；空或不存在时跳过。</param>
        /// <param name="reason">跳过或失败的原因；成功时为空串。</param>
        public static string Publish(
            string poolRoot,
            string requirementIdentifier,
            string interfaceIdentifier,
            string rasterPath,
            out string reason)
        {
            reason = "";

            if (string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                reason = "这份界面规格没归到需求上，布局图不进 media/";
                return "";
            }

            if (string.IsNullOrWhiteSpace(interfaceIdentifier))
            {
                reason = "界面规格没有 id，布局图不知道该叫什么";
                return "";
            }

            if (string.IsNullOrWhiteSpace(rasterPath) || !File.Exists(rasterPath))
            {
                reason = "位图没渲出来，布局图不进 media/";
                return "";
            }

            try
            {
                var directory = PoolPaths.RequirementMediaDirectory(poolRoot, requirementIdentifier);
                Directory.CreateDirectory(directory);

                var target = Path.Combine(directory, interfaceIdentifier + FileNameSuffix);
                File.Copy(rasterPath, target, overwrite: true);
                return target;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = "布局图拷进 media/ 失败：" + exception.Message;
                return "";
            }
        }
    }
}
