using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 配方门禁的判定逻辑：逐配方核对文件齐全、节点 id 存在、请求字段在白名单、依赖在清单里。
    /// 一次报全部问题；某配方加载失败转成一条发现，不让异常穿出去。
    /// </summary>
    public static class RecipeInspector
    {
        /// <summary>请求字段的精确白名单。</summary>
        private static readonly string[] AllowedRequestFields =
        {
            "资产类型", "描述", "命名", "落点", "变体数", "域"
        };

        /// <summary>请求字段的前缀白名单：以这些开头即合法。</summary>
        private static readonly string[] AllowedRequestFieldPrefixes =
        {
            "规格.", "风格锚点."
        };

        /// <summary>
        /// 检查某 driver 下的全部配方；该 driver 没有配方时返回空列表，不是问题。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static IReadOnlyList<PoolFinding> Inspect(string repositoryRoot, string driverName)
        {
            var findings = new List<PoolFinding>();
            foreach (var recipeName in RecipeDefinition.DiscoverNames(repositoryRoot, driverName))
            {
                findings.AddRange(InspectOne(repositoryRoot, driverName, recipeName));
            }

            return findings;
        }

        /// <summary>逐条核对一个配方：文件齐全、节点 id、请求字段白名单与依赖声明。</summary>
        private static IReadOnlyList<PoolFinding> InspectOne(string repositoryRoot, string driverName, string recipeName)
        {
            var findings = new List<PoolFinding>();
            var workflowPath = RecipePaths.WorkflowFile(repositoryRoot, driverName, recipeName);
            var mappingPath = RecipePaths.MappingFile(repositoryRoot, driverName, recipeName);

            if (!File.Exists(workflowPath) || !File.Exists(mappingPath))
            {
                var missingPath = File.Exists(workflowPath) ? mappingPath : workflowPath;
                findings.Add(new PoolFinding(
                    missingPath,
                    $"配方「{recipeName}」缺文件：{Path.GetFileName(missingPath)}",
                    "把 workflow.json 与 映射.json 补全",
                    $"Bridges/{driverName}/配方/{recipeName}/映射.json"));
                return findings;
            }

            RecipeDefinition recipe;
            try
            {
                recipe = RecipeDefinition.Load(repositoryRoot, driverName, recipeName);
            }
            catch (InvalidOperationException exception)
            {
                // 加载失败转成一条发现，不让异常穿出去；文件缺失已在上面单独报过。
                findings.Add(new PoolFinding(
                    mappingPath,
                    exception.Message,
                    "按配方契约补齐 workflow.json 与 映射.json",
                    $"Bridges/{driverName}/配方/{recipeName}/映射.json"));
                return findings;
            }

            var workflowNodeSet = new HashSet<string>(recipe.WorkflowNodeIdentifiers, StringComparer.Ordinal);

            foreach (var entry in recipe.MappingEntries)
            {
                if (!workflowNodeSet.Contains(entry.NodeIdentifier))
                {
                    findings.Add(new PoolFinding(
                        mappingPath,
                        $"映射「{entry.RequestField}」指向的节点「{entry.NodeIdentifier}」不在 workflow 的节点 id 里",
                        "把节点id 改成 workflow.json 里真实存在的节点",
                        $"Bridges/{driverName}/配方/{recipeName}/workflow.json"));
                }
            }

            foreach (var slot in recipe.AnchorSlots)
            {
                if (!workflowNodeSet.Contains(slot.NodeIdentifier))
                {
                    findings.Add(new PoolFinding(
                        mappingPath,
                        $"锚点槽「{slot.SlotName}」指向的节点「{slot.NodeIdentifier}」不在 workflow 的节点 id 里",
                        "把节点id 改成 workflow.json 里真实存在的节点",
                        $"Bridges/{driverName}/配方/{recipeName}/workflow.json"));
                }
            }

            foreach (var entry in recipe.MappingEntries)
            {
                if (!IsAllowedRequestField(entry.RequestField))
                {
                    findings.Add(new PoolFinding(
                        mappingPath,
                        $"请求字段「{entry.RequestField}」不在白名单里",
                        "改成白名单字段：资产类型 / 描述 / 命名 / 落点 / 变体数 / 域，或以 规格. / 风格锚点. 开头",
                        $"Bridges/{driverName}/配方/{recipeName}/映射.json"));
                }
            }

            if (recipe.DependencyNames.Count > 0 && !DependencyManifest.Exists(repositoryRoot, driverName))
            {
                findings.Add(new PoolFinding(
                    mappingPath,
                    $"配方「{recipeName}」声明了 {recipe.DependencyNames.Count} 项依赖，但依赖清单文件不存在",
                    "补一份 Bridges/<driver>/依赖清单.json",
                    $"Bridges/{driverName}/依赖清单.json"));
            }
            else if (DependencyManifest.Exists(repositoryRoot, driverName))
            {
                DependencyManifest manifest;
                try
                {
                    manifest = DependencyManifest.Load(repositoryRoot, driverName);
                }
                catch (InvalidOperationException exception)
                {
                    findings.Add(new PoolFinding(
                        RecipePaths.DependencyManifestFile(repositoryRoot, driverName),
                        exception.Message,
                        "按依赖清单契约修复该文件",
                        $"Bridges/{driverName}/依赖清单.json"));
                    return findings;
                }

                foreach (var dependencyName in recipe.DependencyNames)
                {
                    if (!manifest.TryFind(dependencyName, out _))
                    {
                        findings.Add(new PoolFinding(
                            mappingPath,
                            $"配方「{recipeName}」声明的依赖「{dependencyName}」不在依赖清单里",
                            "在依赖清单里补这条，或从配方依赖数组里去掉",
                            $"Bridges/{driverName}/依赖清单.json"));
                    }
                }
            }

            return findings;
        }

        /// <summary>请求字段是否在精确白名单里，或以白名单前缀开头。</summary>
        private static bool IsAllowedRequestField(string requestField)
        {
            foreach (var allowed in AllowedRequestFields)
            {
                if (string.Equals(requestField, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var prefix in AllowedRequestFieldPrefixes)
            {
                if (requestField.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
