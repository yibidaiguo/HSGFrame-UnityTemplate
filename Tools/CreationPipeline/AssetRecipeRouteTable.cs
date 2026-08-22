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
    /// **为什么要按 driver 分**：配方名不通用。同一类资产在本地形态与线上形态那两家
    /// 叫法就不一样，连配方目录都不同（一边 recipes/ 还要一份节点图，一边 presets/）。
    /// 只按资产类型给一个名字，域路由一旦失败转移到另一个候选，配方名当场对不上，
    /// 回的是「读预设失败：找不到预设文件」——真跑撞过这一脚。
    ///
    /// **查不到就是查不到**：这里绝不给一个「默认配方」兜底。拿图标的配方去出一张界面底图，
    /// 出来的东西既不对又花了钱，而人还以为链路是通的。查不到该让调用方说
    /// 「这类图还没有配方，先建一个」——那是句能照做的话。
    /// </summary>
    public sealed class AssetRecipeRouteTable
    {
        /// <summary>
        /// 一类资产在某个下游上的配方：文生图一份，图生图一份。
        ///
        /// **两份不能混用**：图生图那份走的是另一个接口（要把参考图当入参传上去），
        /// 拿文生图的配方去跑一次带参考图的请求，参考图会被**悄悄丢掉**——
        /// 图照样出得来、照样花钱，只是跟人给的那张一点关系都没有，
        /// 而人只会觉得「这模型怎么不听话」。
        /// </summary>
        /// <param name="TextToImage">文生图配方名；没配为空串。</param>
        /// <param name="ImageToImage">图生图配方名；没配为空串。</param>
        public sealed record AssetRecipeRoute(string TextToImage, string ImageToImage);

        /// <summary>路由表里放映射的那一节。</summary>
        public const string RouteSectionKey = "配方路由";

        /// <summary>
        /// 构造一张路由表。
        /// </summary>
        /// <param name="byDriver">driver → （资产类型 → 配方）。</param>
        /// <param name="loadFailureReason">加载失败原因；正常（含文件不存在）为空串。</param>
        public AssetRecipeRouteTable(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssetRecipeRoute>> byDriver,
            string loadFailureReason)
        {
            ByDriver = byDriver ?? new Dictionary<string, IReadOnlyDictionary<string, AssetRecipeRoute>>(StringComparer.Ordinal);
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>driver → （资产类型 → 配方）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssetRecipeRoute>> ByDriver { get; }

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
            var empty = new Dictionary<string, IReadOnlyDictionary<string, AssetRecipeRoute>>(StringComparer.Ordinal);
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

            var byDriver = new Dictionary<string, IReadOnlyDictionary<string, AssetRecipeRoute>>(StringComparer.Ordinal);
            foreach (var driverPair in routes)
            {
                if (driverPair.Key.StartsWith("_", StringComparison.Ordinal) || driverPair.Value is not JsonObject types)
                {
                    continue;
                }

                var byAssetType = new Dictionary<string, AssetRecipeRoute>(StringComparer.Ordinal);
                foreach (var typePair in types)
                {
                    if (typePair.Key.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var route = ReadRoute(typePair.Value);
                    if (route != null)
                    {
                        byAssetType[typePair.Key] = route;
                    }
                }

                byDriver[driverPair.Key] = byAssetType;
            }

            return new AssetRecipeRouteTable(byDriver, "");
        }

        /// <summary>
        /// 读一条资产类型的配方。两种写法都认：
        /// 一个字符串（只配了文生图），或一个对象（「文生图」与「图生图」各一份）。
        /// 两种都读不出就给 null——那一条跳过，不拿空配方顶上去。
        /// </summary>
        /// <param name="value">这条资产类型在 JSON 里的值。</param>
        private static AssetRecipeRoute ReadRoute(JsonNode value)
        {
            if (value is JsonValue plain && plain.TryGetValue<string>(out var single) && single.Length > 0)
            {
                return new AssetRecipeRoute(single, "");
            }

            if (value is JsonObject pair)
            {
                var textToImage = ReadName(pair, "文生图");
                var imageToImage = ReadName(pair, "图生图");
                if (textToImage.Length > 0 || imageToImage.Length > 0)
                {
                    return new AssetRecipeRoute(textToImage, imageToImage);
                }
            }

            return null;
        }

        /// <summary>读对象里的一个配方名；缺失或类型不对给空串。</summary>
        /// <param name="holder">这条资产类型的对象。</param>
        /// <param name="key">「文生图」或「图生图」。</param>
        private static string ReadName(JsonObject holder, string key)
        {
            return holder[key] is JsonValue value && value.TryGetValue<string>(out var name) ? name : "";
        }

        /// <summary>
        /// 按 driver 与资产类型取配方名。查不到时给一句**能照做的话**，而不是回落到某个默认配方。
        /// </summary>
        /// <param name="driverName">要用哪个下游，值来自域路由表，不在这里写死。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="recipeName">配方名；查不到时为空串。</param>
        /// <param name="reason">查不到的原因与该怎么办；查到时为空串。</param>
        public bool TryResolve(string driverName, string assetType, out string recipeName, out string reason)
        {
            return TryResolve(driverName, assetType, withReferenceImage: false, out recipeName, out reason);
        }

        /// <summary>
        /// 按 driver、资产类型、要不要参考图取配方名。
        ///
        /// **带参考图时查不到图生图配方就是查不到**，不许退回文生图那份：
        /// 退回去的话参考图会被悄悄丢掉，图照出、钱照花，只是跟人给的那张没关系，
        /// 而人只会觉得「这模型怎么不听话」。
        /// </summary>
        /// <param name="driverName">要用哪个下游，值来自域路由表，不在这里写死。</param>
        /// <param name="assetType">资产类型，如「图标」。</param>
        /// <param name="withReferenceImage">这次带不带参考图。</param>
        /// <param name="recipeName">配方名；查不到时为空串。</param>
        /// <param name="reason">查不到的原因与该怎么办；查到时为空串。</param>
        public bool TryResolve(string driverName, string assetType, bool withReferenceImage, out string recipeName, out string reason)
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
                    + "配方名不通用，同一类资产在不同下游的配方叫法都不一样。";
                return false;
            }

            if (byAssetType.TryGetValue(assetType, out var found))
            {
                var wanted = withReferenceImage ? found.ImageToImage : found.TextToImage;
                if (wanted.Length > 0)
                {
                    recipeName = wanted;
                    return true;
                }

                reason = withReferenceImage
                    ? "「" + assetType + "」在「" + driverName + "」上只配了文生图配方，没有图生图那份。"
                        + "人给了参考图，用文生图的配方跑会把那张图悄悄丢掉——图照出、钱照花，"
                        + "跟他给的那张却没关系。走 art-recipe 建一份图生图配方（接口 edits，锚点槽要有「参考图」），"
                        + "再把它填进 " + RelativeRoutePath() + " 里这一条的「图生图」。"
                    : "「" + assetType + "」在「" + driverName + "」上只配了图生图配方，没有文生图那份。"
                        + "这次没有参考图，跑不了。在 " + RelativeRoutePath() + " 里补上这一条的「文生图」。";
                return false;
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
