using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次选片卡片装配的结果：装出来的卡片（可能为 null）与装配过程中发现的全部问题。</summary>
    public sealed class SelectionCardBuildResult
    {
        /// <summary>
        /// 构造一次装配结果。
        /// </summary>
        /// <param name="card">装出来的选片卡片；条件不满足时为 null。</param>
        /// <param name="findings">装配过程中发现的全部问题。</param>
        public SelectionCardBuildResult(SelectionCard card, IReadOnlyList<PoolFinding> findings)
        {
            Card = card;
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>装出来的选片卡片；条件不满足时为 null。</summary>
        public SelectionCard Card { get; }

        /// <summary>装配过程中发现的全部问题。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 扫某资产的变体目录装配一张选片卡片：只收顶层合格变体（图片 + 溯源边车齐全），
    /// 数弃置文件，按轮次定提示语。本类不做尺寸/格式/命名的机检——那是资产规格检查器的活。
    /// </summary>
    public static class SelectionCardBuilder
    {
        /// <summary>变体目录缺省时的参考示例路径。</summary>
        private const string ReferenceSchemaPath = "Pools/Schema/基线/溯源.schema.json";

        /// <summary>允许的图片后缀，比较时大小写不敏感。</summary>
        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

        /// <summary>
        /// 装配一张选片卡片。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="round">选片轮次，从 1 起；小于 1 按 1 处理并出一条 finding。</param>
        public static SelectionCardBuildResult Build(
            string repositoryRoot,
            string requirementIdentifier,
            string assetIdentifier,
            int round)
        {
            var findings = new List<PoolFinding>();
            var effectiveRound = round;
            if (effectiveRound < 1)
            {
                effectiveRound = 1;
                findings.Add(new PoolFinding(
                    "轮次",
                    $"轮次 {round} 小于 1，按第 1 轮处理",
                    "轮次从 1 起传",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
            }

            var variantDirectory = AssetPaths.VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier);
            if (!Directory.Exists(variantDirectory))
            {
                findings.Add(new PoolFinding(
                    variantDirectory,
                    $"变体目录不存在：{variantDirectory}",
                    "先跑生图把变体落进 30-产物/<资产id>/变体/",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new SelectionCardBuildResult(null, findings);
            }

            var qualifiedVariants = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(variantDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(filePath);
                if (!IsImageFile(fileName))
                {
                    // 「*.溯源.json」不是变体，后缀过滤自然排除，这里只是明确写出这条规则。
                    continue;
                }

                var sidecarPath = AssetPaths.SidecarFile(repositoryRoot, requirementIdentifier, assetIdentifier, fileName);
                if (!File.Exists(sidecarPath))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"变体「{fileName}」没有溯源边车，来路不明，不算合格变体",
                        "给这张图补 <变体文件名>.溯源.json 边车",
                        ReferenceSchemaPath));
                    continue;
                }

                qualifiedVariants.Add(fileName);
            }

            qualifiedVariants.Sort(StringComparer.Ordinal);

            var rejectedDirectory = AssetPaths.RejectedDirectory(repositoryRoot, requirementIdentifier, assetIdentifier);
            var rejectedCount = 0;
            if (Directory.Exists(rejectedDirectory))
            {
                rejectedCount = Directory.EnumerateFiles(rejectedDirectory, "*", SearchOption.TopDirectoryOnly).Count();
            }

            if (qualifiedVariants.Count == 0)
            {
                findings.Add(new PoolFinding(
                    variantDirectory,
                    "没有合格变体，不出选片卡片",
                    "重新生成变体，并保证每张图都有溯源边车",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new SelectionCardBuildResult(null, findings);
            }

            var card = new SelectionCard(
                requirementIdentifier,
                assetIdentifier,
                effectiveRound,
                qualifiedVariants,
                rejectedCount);
            return new SelectionCardBuildResult(card, findings);
        }

        /// <summary>文件名后缀是否属于允许的图片格式，大小写不敏感。</summary>
        private static bool IsImageFile(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            foreach (var allowed in AllowedImageExtensions)
            {
                if (string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
