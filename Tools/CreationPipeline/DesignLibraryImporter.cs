using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>把一张人确认过的效果图收进设计库的结果。</summary>
    /// <param name="Imported">效果图有没有真收进去。</param>
    /// <param name="ReferencePath">效果图在设计库里的落点；没收成为空串。</param>
    /// <param name="FinalCreated">顺带带出了模块定稿 v1 没有。</param>
    /// <param name="Notes">一句一条，进执行流水。</param>
    public sealed record DesignLibraryImportResult(
        bool Imported, string ReferencePath, bool FinalCreated, IReadOnlyList<string> Notes);

    /// <summary>
    /// 把「人点确认的那张整屏效果图」收进设计库对应的模块。
    ///
    /// **点拆图就是选中的动作**——人从几张候选里挑一张点下去，那一下已经表达了
    /// 「这张是对的」。所以不必再另做一个「选片」步骤：这条链上已有的那个人审关卡
    /// 顺手就把设计库填上了。
    ///
    /// 收的是**原稿**，不是重绘产物。这两者当风格锚点时天差地别：
    /// 拿重绘产物当锚点会**世代退化**——模型参考自己的输出，下一轮再参考
    /// 「参考自己输出的输出」，几轮之后离原稿越来越远，而每一步看着都合理。
    ///
    /// 效果图住 <c>Pools/Designs/Art/&lt;模块&gt;/refs/</c> 而不是资产落点：
    /// 它不是游戏资产（不进包、不被代码引用、没有命名前缀规矩），
    /// 混进 <c>Art/Texture/</c> 只会让资产规格门禁对着一张设计稿判红。
    /// </summary>
    public static class DesignLibraryImporter
    {
        /// <summary>写盘选项：缩进、中文原样。这份要给人读，也要能看 git diff。
        /// **必须以 JsonSerializerOptions.Default 为基底**——裸 new 出来的没有 TypeInfoResolver，
        /// ToJsonString 会当场抛「must specify a TypeInfoResolver」，而那句话指不到真因上。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>定稿里放几个主色。与项目级定稿同一个数（子文档 06 §五：k-means 8 色）。</summary>
        private const int FinalPaletteCount = 8;

        /// <summary>
        /// 收一张效果图。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名；空串时不收——**不许往「无模块」里堆**，
        /// 那等于把所有界面的原稿倒进一个筐，查同类时全是不相干的东西。</param>
        /// <param name="sourceImagePath">人确认的那张整屏图。</param>
        /// <param name="assetIdentifier">这张图的资产 id，用作文件名。</param>
        public static DesignLibraryImportResult Import(
            string repositoryRoot, string moduleName, string sourceImagePath, string assetIdentifier)
        {
            var notes = new List<string>();
            var module = (moduleName ?? "").Trim();

            if (module.Length == 0)
            {
                notes.Add("这一张没有模块名，没收进设计库——不许往「无模块」里堆，"
                    + "那等于把所有界面的原稿倒进一个筐，往后查同类全是不相干的东西");
                return new DesignLibraryImportResult(false, "", false, notes);
            }

            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                notes.Add($"效果图不在了（{sourceImagePath}），没收进设计库");
                return new DesignLibraryImportResult(false, "", false, notes);
            }

            var directory = DesignLibraryIndex.ReferenceDirectory(repositoryRoot, module);
            var naming = string.IsNullOrWhiteSpace(assetIdentifier)
                ? Path.GetFileNameWithoutExtension(sourceImagePath)
                : assetIdentifier;
            var destination = Path.Combine(directory, naming + ".png");

            try
            {
                Directory.CreateDirectory(directory);
                File.Copy(sourceImagePath, destination, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                notes.Add("效果图收不进设计库：" + exception.Message);
                return new DesignLibraryImportResult(false, "", false, notes);
            }

            notes.Add($"效果图已收进设计库：{module}/refs/{naming}.png");

            var finalCreated = TrySeedModuleFinal(repositoryRoot, module, destination, naming, notes);
            return new DesignLibraryImportResult(true, destination, finalCreated, notes);
        }

        /// <summary>
        /// 模块定稿还没有时，用这张效果图带出 v1：主色从图上聚类、参考图就是它自己。
        ///
        /// 来源写「选片带出」——那是门禁允许的两种来源之一（另一种是「人定」）。
        /// **这不算机器编定稿**：图是人挑的，主色是从那张图上算出来的，
        /// 没有任何一处是模型现编的形容词。
        ///
        /// 已经有定稿时**一个字都不动**：升版是另一件事，要列出受影响的存量资产
        /// （子文档 09 §七 第 3 条），不能靠「又点了一次拆图」悄悄改掉。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="referencePath">效果图在设计库里的绝对路径。</param>
        /// <param name="naming">效果图文件名（不含扩展名）。</param>
        /// <param name="notes">流水。</param>
        private static bool TrySeedModuleFinal(
            string repositoryRoot, string moduleName, string referencePath, string naming, List<string> notes)
        {
            var finalPath = ArtStyleFinal.ModuleFilePath(repositoryRoot, moduleName);
            if (File.Exists(finalPath))
            {
                notes.Add($"{moduleName} 已经有定稿了，没动它——改风格是升版，得单独走（要列出受影响的存量资产）");
                return false;
            }

            var decoded = PngDecoder.DecodeFile(referencePath);
            if (!decoded.Succeeded)
            {
                notes.Add($"效果图读不动（{decoded.FailureReason}），没带出定稿");
                return false;
            }

            var clustered = ColorPalette.Cluster(decoded.Image, FinalPaletteCount);
            if (!clustered.Clustered)
            {
                notes.Add($"主色算不出来（{clustered.FailureReason}），没带出定稿");
                return false;
            }

            var palette = new JsonArray();
            foreach (var swatch in clustered.Swatches)
            {
                palette.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:x2}{1:x2}{2:x2}",
                    swatch.Color.Red,
                    swatch.Color.Green,
                    swatch.Color.Blue));
            }

            var final = new JsonObject
            {
                ["_说明"] = "由「点拆图选中这张效果图」带出的第一版。主色是从那张图上聚类算出来的，"
                    + "参考图就是它本身——没有任何一处是编的。要改方向就升版，"
                    + "升版要列出受影响的存量资产（子文档 09 §七）。",
                ["契约版本"] = "1.0.0",
                ["名称"] = moduleName + "风格@v1",
                ["版本"] = 1,
                ["来源"] = ArtStyleFinal.OriginSelection,
                ["色板"] = palette,
                ["负面清单"] = new JsonArray(),
                ["参考图"] = new JsonArray { moduleName + "/refs/" + naming + ".png" }
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                File.WriteAllText(
                    finalPath,
                    final.ToJsonString(WriteOptions) + "\n",
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                notes.Add("定稿写不下去：" + exception.Message);
                return false;
            }

            notes.Add($"顺带定了 {moduleName} 的第一版风格（{palette.Count} 个主色，来源「选片带出」）"
                + "——往后这个模块的图都以它为锚点");
            return true;
        }
    }
}
