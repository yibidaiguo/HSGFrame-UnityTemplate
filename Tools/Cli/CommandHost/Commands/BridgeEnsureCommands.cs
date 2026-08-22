using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>确保下游对象在（bridge.ensure）的参数。</summary>
    public sealed class BridgeEnsureArguments
    {
        /// <summary>要确保对象的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要确保对象的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不建：列出缺哪几样、要建什么，一个写请求都不发。</summary>
        [Summary("只算不建：列出缺哪几样、要建什么。默认 true，要真建显式传 false")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>要建知识空间时叫什么；留空用下游的缺省名。</summary>
        [Summary("要建知识空间时叫什么；留空用缺省名")]
        [DefaultValue("")]
        public string SpaceTitle { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// `bridge.ensure`：确保这个项目要用的下游对象都在，缺的建出来，**把 id 回填进台账**。
    ///
    /// 这条命令解掉两件反复咬人的事：
    /// 1. **权限**。人手工建的表与节点，应用默认只有读，建表回 403、建节点回 131006。
    ///    链路自己建出来的对象，应用就是所有者，那道门根本不存在。
    /// 2. **换设备**。id 落进 <see cref="DownstreamObjectLedger"/>，台账进 git；
    ///    换台机器 clone 下来还是同一批对象，不会又建一套出来把数据劈成两半。
    ///
    /// 回填**只在真跑时做**：干跑那一轮下游什么都没建，把「&lt;干跑未建&gt;」写进台账
    /// 等于给了一个假 id，下一次真跑会拿着它去验、验不过再建，账面上却多了一次莫名其妙的重建。
    /// </summary>
    public static class BridgeEnsureCommands
    {
        /// <summary>
        /// 跑一次「确保对象在」：调下游的 ensure 动作，再把交回来的 id 写进台账。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("bridge.ensure")]
        [Summary("确保下游对象（空间/表/节点）都在，缺的建出来并把 id 回填进台账；默认只算不建")]
        public static CommandResult Ensure(BridgeEnsureArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var payload = new JsonObject { ["干跑"] = arguments.DryRun };
            if (!string.IsNullOrWhiteSpace(arguments.SpaceTitle))
            {
                payload["空间标题"] = arguments.SpaceTitle;
            }

            var call = BridgeInvoker.Invoke(
                repositoryRoot,
                arguments.Driver,
                "ensure",
                JsonSerializer.SerializeToElement(payload),
                arguments.TimeoutSeconds);
            if (!call.Succeeded)
            {
                return CommandResult.Failure(call.HumanText, new[] { $"错误码：{call.ErrorCode}" });
            }

            var lines = new List<string>();
            AppendList(lines, call.Payload, "沿用", "沿用");
            AppendList(lines, call.Payload, "重建", "重建（旧的已经不在了）");
            AppendList(lines, call.Payload, "新建", "新建");
            AppendList(lines, call.Payload, "建不出来的列", "建不出来");

            var objects = ReadObjects(call.Payload);
            if (arguments.DryRun)
            {
                lines.Add("干跑：下游什么都没建，台账也没动");
                return CommandResult.Success($"干跑完成：{objects.Count} 样对象", lines);
            }

            if (objects.Count == 0)
            {
                return CommandResult.Failure("下游没交回任何对象 id，台账没动", lines);
            }

            var write = DownstreamObjectLedger.Write(repositoryRoot, arguments.Driver, objects);
            lines.Add(write.Succeeded ? $"台账：{write.Message}" : $"台账没写成：{write.Message}");
            lines.Add($"台账文件：{write.FilePath}");

            // 台账写不下去时不许报成功：对象在下游真建出来了，而账上没有——
            // 下一次跑会再建一批，两套表就是这么来的。
            return write.Succeeded
                ? CommandResult.Success($"对象齐了：{objects.Count} 样，已回填台账", lines)
                : CommandResult.Failure("对象建出来了但台账没写成，下次会重建一批，先把台账修好", lines);
        }

        /// <summary>把响应里的「对象」读成 键→值；空值的键跳过（那是干跑的占位，不许进台账）。</summary>
        private static Dictionary<string, string> ReadObjects(JsonElement payload)
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
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString() ?? "";
                if (value.Length == 0 || value.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                objects[property.Name] = value;
            }

            return objects;
        }

        /// <summary>把响应里的一个字符串数组摊成日志行；空数组什么都不加。</summary>
        private static void AppendList(List<string> lines, JsonElement payload, string key, string label)
        {
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    lines.Add(label + "：" + item.GetString());
                }
            }
        }
    }
}
