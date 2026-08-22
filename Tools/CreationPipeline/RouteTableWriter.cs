using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 域路由的写入器：把「这个域现在用哪几个下游、首选是谁、挂了换不换人」变成一条能点的动作。
    ///
    /// 三条规矩，跟 <see cref="LocalSettingsWriter"/> 同源：
    /// 1. **读改写，不重建**。文件里其余内容（实现表、各处 _说明）原样保留；
    ///    JSON 坏掉时拒绝写，绝不用一份干净骨架把人写了一半的文件盖掉。
    /// 2. **只认真实存在、且真的声明了这个 port 的 driver**。凭空写一个名字进去，
    ///    调用时才在子进程那一层炸，报的是「驱动自述缺失」——那句话指不到「路由表写错了」上。
    /// 3. **策略只认两个值**。写一个第三种进去，整份路由表会判坏，连带别的域一起不能用。
    ///
    /// 与 local.json 的分工：**这份文件进 git**，改它是改整个项目的选择；
    /// 各人机器上的地址、密钥、可执行文件路径在 local.json，那份永不入库。
    /// </summary>
    public static class RouteTableWriter
    {
        /// <summary>路由表里放域路由的那一节。</summary>
        private const string PortSectionName = "域路由";

        /// <summary>写盘用序列化选项：中文键原样输出、带缩进（这份文件是人要读要改的）。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        /// <summary>
        /// 改一个 port 的路由：候选清单（按优先级）与策略。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="portName">port 名，如「生图」。</param>
        /// <param name="candidateNames">按优先级排好的候选 driver 名；第一个是首选。空列表表示不改候选。</param>
        /// <param name="strategy">策略；空串表示不改策略（新建时按「首选固定」）。</param>
        public static ConfigWriteOutcome SetPortRoute(
            string repositoryRoot,
            string portName,
            IReadOnlyList<string> candidateNames,
            string strategy)
        {
            var filePath = BridgeRouteTable.RouteTableFile(repositoryRoot);
            portName = (portName ?? "").Trim();
            if (portName.Length == 0)
            {
                return ConfigWriteOutcome.Failure("必须指定 port 名", filePath);
            }

            if (portName.StartsWith("_", StringComparison.Ordinal))
            {
                return ConfigWriteOutcome.Failure("port 名不能以下划线开头——下划线开头的是说明字段，不是路由项", filePath);
            }

            strategy = (strategy ?? "").Trim();
            if (strategy.Length > 0 && !PortRoute.IsKnownStrategy(strategy))
            {
                return ConfigWriteOutcome.Failure(
                    $"策略「{strategy}」不认；只有「{PortRoute.FixedPreferredStrategy}」与「{PortRoute.FailoverStrategy}」两个值",
                    filePath);
            }

            var candidates = (candidateNames ?? Array.Empty<string>())
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .ToList();

            var duplicate = candidates.GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                // 同一个 driver 排两遍，失败转移会把它试两次——白等一轮超时。
                return ConfigWriteOutcome.Failure($"候选里「{duplicate.Key}」出现了不止一次", filePath);
            }

            foreach (var candidate in candidates)
            {
                if (!TryValidateCandidate(repositoryRoot, candidate, portName, out var validationReason))
                {
                    return ConfigWriteOutcome.Failure(validationReason, filePath);
                }
            }

            if (!File.Exists(filePath))
            {
                return ConfigWriteOutcome.Failure($"下游路由表不存在：{filePath}", filePath);
            }

            JsonNode rootNode;
            try
            {
                rootNode = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return ConfigWriteOutcome.Failure($"下游路由表不是合法 JSON，拒绝写入（先把它修好）：{exception.Message}", filePath);
            }

            if (rootNode is not JsonObject root)
            {
                return ConfigWriteOutcome.Failure("下游路由表的顶层不是对象，拒绝写入", filePath);
            }

            if (root[PortSectionName] is not JsonObject portSection)
            {
                return ConfigWriteOutcome.Failure($"下游路由表缺「{PortSectionName}」或它不是对象，拒绝写入", filePath);
            }

            // 没给候选就沿用现有的：只改策略是最常见的动作（「这个域挂了要不要自动换人」）。
            if (candidates.Count == 0)
            {
                if (!TryReadExistingCandidates(portSection[portName], out candidates, out var readReason))
                {
                    return ConfigWriteOutcome.Failure($"没给候选，而「{portName}」现有的路由读不出来：{readReason}", filePath);
                }
            }

            if (candidates.Count == 0)
            {
                return ConfigWriteOutcome.Failure($"「{portName}」的候选是空的，这个域等于没有下游", filePath);
            }

            if (strategy.Length == 0)
            {
                strategy = ReadExistingStrategy(portSection[portName]);
            }

            portSection[portName] = new JsonObject
            {
                ["候选"] = new JsonArray(candidates.Select(name => (JsonNode)JsonValue.Create(name)).ToArray()),
                ["策略"] = strategy
            };

            try
            {
                File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return ConfigWriteOutcome.Failure($"写下游路由表失败：{exception.Message}", filePath);
            }

            var summary = new StringBuilder();
            summary.Append("已把「").Append(portName).Append("」的候选改成：").Append(string.Join(" → ", candidates));
            summary.Append("；策略：").Append(strategy);
            if (candidates.Count > 1 && string.Equals(strategy, PortRoute.FixedPreferredStrategy, StringComparison.Ordinal))
            {
                summary.Append("（有多个候选但策略是首选固定，首选挂了不会自动换人）");
            }

            return ConfigWriteOutcome.Success(summary.ToString(), filePath);
        }

        /// <summary>
        /// 校验一个候选：目录里得真有这份 driver 自述，而且它得真的声明了这个 port。
        /// 只查名字存不存在是不够的——把一个只做模型加工的 driver 排进「生图」的候选，
        /// 名字是对的、目录也在，但它这辈子不会响应 generate。
        /// </summary>
        private static bool TryValidateCandidate(string repositoryRoot, string candidateName, string portName, out string reason)
        {
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, candidateName);
            }
            catch (InvalidOperationException exception)
            {
                reason = $"候选「{candidateName}」不是一个可用的 driver：{exception.Message}";
                return false;
            }

            if (!descriptor.Ports.Contains(portName, StringComparer.Ordinal))
            {
                reason = descriptor.Ports.Count == 0
                    ? $"候选「{candidateName}」的自述没有声明任何 port，接不了「{portName}」"
                    : $"候选「{candidateName}」没有声明「{portName}」这个 port，它声明的是：{string.Join("、", descriptor.Ports)}";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>读某一项现有的候选清单；两种形状都认。</summary>
        private static bool TryReadExistingCandidates(JsonNode node, out List<string> candidates, out string reason)
        {
            candidates = new List<string>();
            reason = "";

            if (node == null)
            {
                reason = "这个 port 现在还没有路由";
                return false;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var single))
            {
                candidates.Add(single);
                return true;
            }

            if (node is JsonObject entry && entry["候选"] is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out var name) && name.Length > 0)
                    {
                        candidates.Add(name);
                    }
                }

                return true;
            }

            reason = "现有的路由既不是 driver 名字符串，也不是带「候选」数组的对象";
            return false;
        }

        /// <summary>读某一项现有的策略；读不出来按「首选固定」。</summary>
        private static string ReadExistingStrategy(JsonNode node)
        {
            if (node is JsonObject entry
                && entry["策略"] is JsonValue value
                && value.TryGetValue<string>(out var strategy)
                && PortRoute.IsKnownStrategy(strategy))
            {
                return strategy;
            }

            return PortRoute.FixedPreferredStrategy;
        }
    }
}
