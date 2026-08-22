using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// driver → 资产类型 → 配方名的路由表：<c>Config/asset-recipe.json</c>（进 git）。
    ///
    /// 为什么要有：`bridge.generate` 必须点名一个配方，而助手是从聊天里认出「要一张图标」的，
    /// 它不知道图标该用哪份配方。这张表把「哪类资产用哪份配方」从代码里挪到配置里——
    /// 加一类资产、换一份配方都不用改代码。
    ///
    /// **为什么要按 driver 分**：配方名不通用。同样是图标，comfyui 那边叫 `icon@v5`
    /// （住 recipes/，还要一份 workflow.json），oaiimage 那边叫 `icon@v1`（住 presets/）。
    /// 只按资产类型给一个名字，域路由一旦失败转移到另一个候选，配方名当场对不上，
    /// 回的是「读预设失败：找不到预设文件」——真跑撞过这一脚。
    ///
    /// **查不到就是查不到**：这里绝不给一个「默认配方」兜底。拿图标的配方去出一张界面底图，
    /// 出来的东西既不对又花了钱，而人还以为链路是通的。查不到该让调用方说
    /// 「这类图还没有配方，先建一个」——那是句能照做的话。
    /// </summary>
    public sealed class AssetRecipeRouteTable
    {
        /// <summary>路由表里放映射的那一节。</summary>
        public const string RouteSectionKey = "配方路由";

        /// <summary>
        /// 构造一张路由表。
        /// </summary>
        /// <param name="byDriver">driver → （资产类型 → 配方名）。</param>
        /// <param name="loadFailureReason">加载失败原因；正常（含文件不存在）为空串。</param>
        public AssetRecipeRouteTable(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byDriver,
            string loadFailureReason)
        {
            ByDriver = byDriver ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>driver → （资产类型 → 配方名）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByDriver { get; }

        /// <summary>加载失败原因；正常为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>路由表文件路径：Tools/CreationPipeline/Config/asset-recipe.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string RouteFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "asset-recipe.json");
        }

        /// <summary>给人看的相对路径，用在提示文案里。</summary>
        public static string RelativeRoutePath()
        {
            return "Tools/CreationPipeline/Config/asset-recipe.json";
        }

        /// <summary>
        /// 读路由表。文件不存在给一张空表（不算失败——那只是还没配过）；
        /// JSON 坏掉给空表**并带上原因**，让调用方去说「先把文件修好」，
        /// 而不是把「坏了」当成「没配」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static AssetRecipeRouteTable Load(string repositoryRoot)
        {
            var filePath = RouteFile(repositoryRoot);
            var empty = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (!File.Exists(filePath))
            {
                return new AssetRecipeRouteTable(empty, "");
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new AssetRecipeRouteTable(empty, $"配方路由表不是合法 JSON：{exception.Message}（{RelativeRoutePath()}）");
            }

            if (node is not JsonObject root || root[RouteSectionKey] is not JsonObject routes)
            {
                return new AssetRecipeRouteTable(empty, "");
            }

            var byDriver = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var driverPair in routes)
            {
                if (driverPair.Key.StartsWith("_", StringComparison.Ordinal) || driverPair.Value is not JsonObject types)
                {
                    continue;
                }

                var byAssetType = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var typePair in types)
                {
                    if (typePair.Key.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (typePair.Value is JsonValue value && value.TryGetValue<string>(out var recipe) && recipe.Length > 0)
                    {
                        byAssetType[typePair.Key] = recipe;
                    }
                }

                byDriver[driverPair.Key] = byAssetType;
            }

            return new AssetRecipeRouteTable(byDriver, "");
        }

        /// <summary>
        /// 按 driver 与资产类型取配方名。查不到时给一句**能照做的话**，而不是回落到某个默认配方。
        /// </summary>
        /// <param name="driverName">要用哪个下游，如「comfyui」。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="recipeName">配方名；查不到时为空串。</param>
        /// <param name="reason">查不到的原因与该怎么办；查到时为空串。</param>
        public bool TryResolve(string driverName, string assetType, out string recipeName, out string reason)
        {
            recipeName = "";
            reason = "";

            if (LoadFailureReason.Length > 0)
            {
                reason = LoadFailureReason;
                return false;
            }

            if (string.IsNullOrWhiteSpace(driverName))
            {
                reason = "没说用哪个下游，配方无从查起";
                return false;
            }

            if (string.IsNullOrWhiteSpace(assetType))
            {
                reason = "没说要哪类资产，配方无从查起";
                return false;
            }

            if (!ByDriver.TryGetValue(driverName, out var byAssetType))
            {
                reason = "配方路由表里没有「" + driverName + "」这个下游那一节。"
                    + "在 " + RelativeRoutePath() + " 里给它加一节，写清它自己的配方名——"
                    + "配方名不通用，comfyui 与 oaiimage 同一类资产的配方叫法都不一样。";
                return false;
            }

            if (byAssetType.TryGetValue(assetType, out var found) && found.Length > 0)
            {
                recipeName = found;
                return true;
            }

            var known = byAssetType.Count == 0
                ? "（这个下游一条都没配）"
                : string.Join("、", byAssetType.Keys);
            reason = "「" + assetType + "」这类资产在「" + driverName + "」上还没有配方。它已配的是："
                + known + "。配方不是配置项，是一份要调的东西（提示词骨架 + 锚点槽 + workflow），"
                + "走 art-recipe 把它建出来，再把「" + assetType + "」这一条加进 " + RelativeRoutePath() + "。";
            return false;
        }
    }
}
