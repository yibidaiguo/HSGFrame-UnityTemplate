using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 「要用某个下游对象时，台账上没有就去建一个」——需求与任务那条链一直是这么做的，
    /// 这里把它抽出来给别的链路共用。
    ///
    /// **为什么不能只报「取不到」**：台账里没有那个键时，桥读到的是空串，
    /// 而空串在下游那边多半有个「合理」的默认行为（比如把节点建成一级节点）。
    /// 于是这条链既不报错也没挂对地方——**静默挂错比报错难查得多**：
    /// 人在知识库里找不到那份文档，会先怀疑自己记错了路径。
    ///
    /// 已经有值就**一次下游调用都不发**：ensure 本身是幂等的，但它要跑一趟完整的
    /// 对象核对，而这条路会在每次推文档前经过。
    /// </summary>
    public static class DownstreamObjectEnsurer
    {
        /// <summary>
        /// 确保台账里某一格有值：有就直接回 true，没有就跑一次 ensure 把它建出来。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="objectKey">台账里的对象键，如「模块策划案父节点」。</param>
        /// <param name="timeoutSeconds">下游调用超时秒数。</param>
        /// <param name="notes">这一趟做了什么，一句一条。</param>
        public static bool EnsureKey(
            string repositoryRoot,
            string driverName,
            string objectKey,
            int timeoutSeconds,
            List<string> notes)
        {
            var ledger = DownstreamObjectLedger.Load(repositoryRoot);
            if (ledger.LoadFailureReason.Length > 0)
            {
                notes?.Add("下游对象台账读不动：" + ledger.LoadFailureReason);
                return false;
            }

            if (ledger.Read(driverName, objectKey).Length > 0)
            {
                return true;
            }

            notes?.Add($"台账里还没有「{objectKey}」，先跑一次 ensure 把它建出来");

            var payload = JsonSerializer.SerializeToElement(new JsonObject { ["干跑"] = false });
            var call = BridgeInvoker.Invoke(repositoryRoot, driverName, "ensure", payload, timeoutSeconds);
            if (!call.Succeeded)
            {
                notes?.Add($"补建下游对象失败（{call.ErrorCode}）：{call.HumanText}");
                return false;
            }

            var objects = ReadObjects(call.Payload);
            if (objects.Count == 0)
            {
                notes?.Add("下游没交回任何对象 id，台账没动");
                return false;
            }

            var write = DownstreamObjectLedger.Write(repositoryRoot, driverName, objects);
            if (!write.Succeeded)
            {
                // 台账写不下去时**不许报成功**：对象在下游真建出来了而账上没有，
                // 下一次跑会再建一批——两套表就是这么来的。
                notes?.Add("台账没写成：" + write.Message);
                return false;
            }

            notes?.Add($"下游对象已补建并回填台账（{objects.Count} 样）");
            return objects.TryGetValue(objectKey, out var value) && value.Length > 0;
        }

        /// <summary>从 ensure 的响应里读「对象」那一坨。</summary>
        /// <param name="payload">桥的响应载荷。</param>
        private static IReadOnlyDictionary<string, string> ReadObjects(JsonElement payload)
        {
            var objects = new Dictionary<string, string>(StringComparer.Ordinal);
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("对象", out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                return objects;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    objects[property.Name] = property.Value.GetString() ?? "";
                }
            }

            return objects;
        }
    }
}
