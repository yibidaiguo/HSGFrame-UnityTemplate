using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 资产分层检查：每个资产都得落到完整深度，门类要在词表里，扩展名要对得上那一类。
    ///
    /// **这道门禁是冲着「以后再说」去的。** 从前的规范写着「模块少时可以先平铺，涨了再开模块夹」，
    /// 结果真实工程跑了几个月之后：<c>Art/Model/</c> 两百多个文件平铺在根上，
    /// <c>Art/Material/</c> 只分出 Character 与 Level 两夹、其余全在根，<c>Art/Audio/</c> 一个夹都没有。
    /// 没人偷懒——是规则给了「以后再开」这个选项，而「以后」不会自己到。
    /// 所以这一版取消平铺档，并且**把它变成一道会红的检查**：靠自觉的规则等于没有规则。
    ///
    /// 四种违规分开报，因为修法完全不同：
    /// 深度不够要挪目录；门类不认识要么改名要么往词表里加一条；
    /// 扩展名不对是**东西放错了树**（动画夹里躺着 fbx 就是这一条）；模块名是垃圾名要重新起。
    /// </summary>
    public static class AssetLayoutChecker
    {
        /// <summary>规范里这一节的位置，报违规时指过去。</summary>
        private const string SpecificationReference = "Specifications/structure-assets.md 第二节 + Specifications/Baseline/asset-layout.baseline.json";

        /// <summary>Unity 的 .meta 与目录说明文件不算资产，不参与分层检查。</summary>
        private static readonly string[] IgnoredFileNames = { "import-rules.json", ".gitkeep", ".ds_store" };

        /// <summary>
        /// 检查资产根下的分层，返回全部违规。
        /// </summary>
        /// <param name="assetsRootDirectory">Unity 工程的 Assets 目录。</param>
        /// <param name="ruleSet">分层词表。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(string assetsRootDirectory, AssetLayoutRuleSet ruleSet)
        {
            var violations = new List<AssetBundleGroupViolation>();
            if (ruleSet == null)
            {
                return violations;
            }

            if (ruleSet.LoadFailureReason.Length > 0)
            {
                violations.Add(new AssetBundleGroupViolation(
                    ruleSet.AssetRoot,
                    ruleSet.LoadFailureReason,
                    "从模板同步一份 Specifications/Baseline/asset-layout.baseline.json",
                    SpecificationReference));
                return violations;
            }

            if (string.IsNullOrWhiteSpace(assetsRootDirectory) || !Directory.Exists(assetsRootDirectory))
            {
                // **「没扫成」与「没问题」必须分开**（决策 42）。这一条最早就是踩出来的：
                // 扫描根算错时目录不存在，函数直接返回空表，门禁于是报「通过，问题 0 条」——
                // 而真实工程里两百个文件正平铺在根上。绿得完全错误。
                violations.Add(new AssetBundleGroupViolation(
                    assetsRootDirectory ?? "",
                    "Assets 根目录不存在，这一趟一个文件都没扫",
                    "把 AssetsRootDirectory 指向 Unity 工程的 Assets 目录",
                    SpecificationReference));
                return violations;
            }

            var assetRoot = Path.Combine(assetsRootDirectory, ruleSet.AssetRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(assetRoot))
            {
                // 资产根还没建出来是正常的（新项目还没有任何美术资产），不是违规。
                // 与上面那条的区别在于：Assets 在、只是 Art 还没建——那确实没东西可查。
                return violations;
            }

            var fullAssetRoot = Path.GetFullPath(assetRoot);
            foreach (var filePath in Directory.EnumerateFiles(fullAssetRoot, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);
                if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    || IgnoredFileNames.Contains(fileName.ToLowerInvariant()))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(fullAssetRoot, filePath).Replace('\\', '/');
                var segments = relative.Split('/');
                var directoryDepth = segments.Length - 1;
                var displayPath = ruleSet.AssetRoot + "/" + relative;

                // ---- 一、深度 ----
                if (directoryDepth < ruleSet.MinimumDepth)
                {
                    violations.Add(new AssetBundleGroupViolation(
                        displayPath,
                        $"只在资产根下 {directoryDepth} 层目录里，规定是 {ruleSet.MinimumDepth} 层"
                            + $"（<类型>/<门类>/<模块>/文件）",
                        "把它挪到完整深度。一个文件的目录不丑，两百个文件的根目录才丑——"
                            + "「先平铺以后再分」这一档已经取消了，因为「以后」不会自己到",
                        SpecificationReference));
                    continue;
                }

                var typeName = segments[0];
                var rule = ruleSet.Find(typeName);
                if (rule == null)
                {
                    violations.Add(new AssetBundleGroupViolation(
                        displayPath,
                        $"第一层「{typeName}」不是词表里的资产类型"
                            + $"（有的是：{string.Join("、", ruleSet.TypeNames.OrderBy(name => name, StringComparer.Ordinal))}）",
                        "改成词表里的类型，或往 asset-layout 的「类型」里加一条",
                        SpecificationReference));
                    continue;
                }

                // ---- 二、门类 ----
                var categoryName = segments[1];
                if (!rule.Categories.Contains(categoryName, StringComparer.Ordinal))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        displayPath,
                        $"第二层「{categoryName}」不在 {typeName} 的门类词表里"
                            + $"（有的是：{string.Join("、", rule.Categories.OrderBy(name => name, StringComparer.Ordinal))}）",
                        "改成词表里的门类，或往 asset-layout 里给这一类加一条门类——"
                            + "加门类是加数据，不用改代码",
                        SpecificationReference));
                    continue;
                }

                // ---- 三、扩展名 ----
                // 这一条抓的是**东西放错了树**：动画夹里躺着 .fbx 就是它。
                var extension = Path.GetExtension(relative).ToLowerInvariant();
                if (rule.AllowedExtensions.Count > 0 && !rule.AllowedExtensions.Contains(extension, StringComparer.Ordinal))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        displayPath,
                        $"{typeName} 这棵树只收 {string.Join(" / ", rule.AllowedExtensions)}，这个是「{extension}」",
                        $"把它挪到该去的那棵树。带动画的模型属于 Model/，"
                            + "动画片段要从模型里提成 .anim 再放 Animation/——"
                            + "模型摆在动画夹里，人点开只会看到一个模型",
                        SpecificationReference));
                    continue;
                }

                // ---- 四、模块层的名字 ----
                var moduleName = segments[2];
                if (ruleSet.BannedModuleNames.Contains(moduleName, StringComparer.OrdinalIgnoreCase))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        displayPath,
                        $"模块层叫「{moduleName}」——这种名字等于没分",
                        "按这批资产属于谁起名：玩法模块名（Inventory、Combat）或具体主题（Grass、Oak、StoneWall）。"
                            + "Misc 这类名字一旦出现就会变成新的垃圾堆",
                        SpecificationReference));
                }
            }

            return violations
                .OrderBy(item => item.AssetPath, StringComparer.Ordinal)
                .ToList();
        }
    }
}
