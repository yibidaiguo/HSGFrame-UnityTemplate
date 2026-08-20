using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Template.Bridges.Comfyui
{
    /// <summary>
    /// 配方骨架翻译器：把中文骨架（节点 id → {类型, 参数, 连线}）翻成下游 API 形状
    /// （节点 id → {class_type, inputs}）。连线在 inputs 里写成 ["上游节点id", 输出下标]。
    /// 纯函数，不碰网络、不碰磁盘——翻译器必须能脱离下游单独测。
    /// 规则：先搬「参数」，再按「连线」把对应参数名替换成连接引用，最后按参数覆盖
    /// （映射填进来的值）逐键覆盖。连线指向不存在的节点是硬错误，绝不静默产一张断图。
    /// </summary>
    public static class WorkflowTranslator
    {
        /// <summary>
        /// 翻译一份配方骨架。
        /// </summary>
        /// <param name="workflow">workflow.json 的顶层对象：节点 id → {类型, 参数, 连线}。</param>
        /// <param name="overrides">参数覆盖：节点 id → 参数名 → 值；映射填进来的值走这里。</param>
        /// <returns>下游 API 形状的顶层对象：节点 id → {class_type, inputs}。</returns>
        /// <exception cref="InvalidOperationException">节点缺「类型」、连线指向不存在的节点或输出下标不是数字时抛出。</exception>
        public static JsonObject Translate(JsonObject workflow, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode>> overrides)
        {
            if (workflow == null)
            {
                throw new ArgumentException("workflow 不能为 null");
            }

            var result = new JsonObject();
            ValidateOverrideTargets(workflow, overrides);
            foreach (var nodeProperty in workflow)
            {
                var nodeIdentifier = nodeProperty.Key;
                var nodeObject = nodeProperty.Value as JsonObject;
                if (nodeObject == null)
                {
                    throw new InvalidOperationException($"节点「{nodeIdentifier}」不是对象，翻译不了");
                }

                var classType = ReadClassType(nodeIdentifier, nodeObject);

                var inputs = new JsonObject();
                CopyParameters(nodeIdentifier, nodeObject, inputs);
                ApplyConnections(nodeIdentifier, nodeObject, workflow, inputs);
                ApplyOverrides(nodeIdentifier, overrides, inputs);

                result[nodeIdentifier] = new JsonObject
                {
                    ["class_type"] = classType,
                    ["inputs"] = inputs
                };
            }

            return result;
        }

        /// <summary>校验参数覆盖的节点 id 都真实存在于 workflow；指向不存在的节点是静默失效，报错。</summary>
        private static void ValidateOverrideTargets(JsonObject workflow, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode>> overrides)
        {
            if (overrides == null)
            {
                return;
            }

            foreach (var nodeIdentifier in overrides.Keys)
            {
                if (!workflow.ContainsKey(nodeIdentifier))
                {
                    throw new InvalidOperationException($"参数覆盖指向了不存在的节点「{nodeIdentifier}」");
                }
            }
        }

        /// <summary>读节点对象的「类型」；缺失或不是字符串报错。</summary>
        private static string ReadClassType(string nodeIdentifier, JsonObject nodeObject)
        {
            if (!nodeObject.TryGetPropertyValue("类型", out var classTypeNode)
                || classTypeNode is not JsonValue classTypeValue
                || !classTypeValue.TryGetValue<string>(out var classType)
                || string.IsNullOrWhiteSpace(classType))
            {
                throw new InvalidOperationException($"节点「{nodeIdentifier}」缺「类型」或它不是字符串");
            }

            return classType;
        }

        /// <summary>把「参数」对象的键值原样搬进 inputs。</summary>
        private static void CopyParameters(string nodeIdentifier, JsonObject nodeObject, JsonObject inputs)
        {
            if (!nodeObject.TryGetPropertyValue("参数", out var parametersNode) || parametersNode is not JsonObject parameters)
            {
                throw new InvalidOperationException($"节点「{nodeIdentifier}」缺「参数」或它不是对象");
            }

            foreach (var parameter in parameters)
            {
                inputs[parameter.Key] = parameter.Value?.DeepClone();
            }
        }

        /// <summary>按「连线」把对应参数名替换成连接引用；上游节点不存在或输出下标不是数字即报错。</summary>
        private static void ApplyConnections(string nodeIdentifier, JsonObject nodeObject, JsonObject workflow, JsonObject inputs)
        {
            if (!nodeObject.TryGetPropertyValue("连线", out var connectionsNode))
            {
                return;
            }

            if (connectionsNode is not JsonObject connections)
            {
                throw new InvalidOperationException($"节点「{nodeIdentifier}」的「连线」不是对象");
            }

            foreach (var connection in connections)
            {
                var parameterName = connection.Key;
                if (connection.Value is not JsonArray reference || reference.Count < 2
                    || reference[0] is not JsonValue upstreamValue
                    || !upstreamValue.TryGetValue<string>(out var upstreamIdentifier)
                    || string.IsNullOrWhiteSpace(upstreamIdentifier)
                    || reference[1] is not JsonValue slotValue
                    || !slotValue.TryGetValue<int>(out var slotIndex))
                {
                    throw new InvalidOperationException($"节点「{nodeIdentifier}」的参数「{parameterName}」的连线必须是 [\"上游节点id\", 输出下标] 形状");
                }

                if (!workflow.ContainsKey(upstreamIdentifier))
                {
                    throw new InvalidOperationException($"节点「{nodeIdentifier}」的参数「{parameterName}」连到了不存在的节点「{upstreamIdentifier}」");
                }

                inputs[parameterName] = reference.DeepClone();
            }
        }

        /// <summary>按参数覆盖逐键覆盖 inputs；覆盖的节点 id 必须真实存在，否则报错。</summary>
        private static void ApplyOverrides(string nodeIdentifier, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode>> overrides, JsonObject inputs)
        {
            if (overrides == null)
            {
                return;
            }

            if (!overrides.TryGetValue(nodeIdentifier, out var nodeOverrides))
            {
                return;
            }

            foreach (var pair in nodeOverrides)
            {
                inputs[pair.Key] = pair.Value?.DeepClone();
            }
        }
    }
}
