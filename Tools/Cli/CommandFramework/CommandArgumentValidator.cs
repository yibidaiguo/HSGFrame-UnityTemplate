using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>命令参数校验器：把参数 JSON 的问题一次性收齐，产出四要素诊断。</summary>
    public static class CommandArgumentValidator
    {
        /// <summary>
        /// 校验参数 JSON，返回全部诊断；空列表表示可以安全反序列化。
        /// </summary>
        /// <param name="descriptor">命令描述。</param>
        /// <param name="argumentsJson">参数 JSON 原文。</param>
        public static IReadOnlyList<CommandDiagnostic> Validate(CommandDescriptor descriptor, string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return new[]
                {
                    new CommandDiagnostic(
                        descriptor.CommandName,
                        "参数 JSON 为空",
                        "用 --arguments-file 指向一个 JSON 对象文件",
                        BuildMinimalExample(descriptor))
                };
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(argumentsJson);
            }
            catch (JsonException exception)
            {
                return new[]
                {
                    new CommandDiagnostic(
                        descriptor.CommandName,
                        "参数 JSON 语法不合法：" + exception.Message,
                        "按报错的行列修正 JSON 语法",
                        BuildMinimalExample(descriptor))
                };
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new[]
                    {
                        new CommandDiagnostic(
                            descriptor.CommandName,
                            "参数 JSON 的顶层需要是对象",
                            "把参数 JSON 改成对象形式",
                            BuildMinimalExample(descriptor))
                    };
                }

                var root = document.RootElement;
                var diagnostics = new List<CommandDiagnostic>();

                // 认不出来的参数键**必须报错**，不许静默忽略。
                // 静默忽略的代价是真金白银：driver 自述里的试跑写的是 `--dry-run`，
                // 而属性名是 `DryRun`，键对不上就被吞掉、命令按默认值跑完、退出码 0，
                // 没有任何人会知道那次「干跑」其实是真跑（P8 批次 13 真踩过，
                // 一次点击执行了一次真的供给）。同一个坑长在花积分的命令上就是烧钱。
                var knownNames = new HashSet<string>(
                    descriptor.ParameterSchemas.Select(parameter => parameter.ParameterName),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var property in root.EnumerateObject())
                {
                    if (knownNames.Contains(property.Name))
                    {
                        continue;
                    }

                    diagnostics.Add(new CommandDiagnostic(
                        property.Name,
                        "不认识这个参数名——参数键按 CLR 属性名匹配（大小写不敏感），认不出来的键一律不许静默忽略",
                        $"改成这条命令认的参数名之一：{string.Join(" / ", descriptor.ParameterSchemas.Select(parameter => parameter.ParameterName))}",
                        BuildMinimalExample(descriptor)));
                }

                foreach (var parameter in descriptor.ParameterSchemas.Where(parameter => parameter.IsRequired))
                {
                    var property = FindProperty(root, parameter.ParameterName);
                    if (!property.HasValue
                        || property.Value.ValueKind == JsonValueKind.Null
                        || (property.Value.ValueKind == JsonValueKind.String
                            && string.IsNullOrWhiteSpace(property.Value.GetString())))
                    {
                        diagnostics.Add(new CommandDiagnostic(
                            parameter.ParameterName,
                            "必填参数缺失或为空",
                            $"在参数 JSON 里补上 {parameter.ParameterName}",
                            BuildMinimalExample(descriptor)));
                    }
                }

                foreach (var parameter in descriptor.ParameterSchemas)
                {
                    var property = FindProperty(root, parameter.ParameterName);
                    if (!property.HasValue || property.Value.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    var kind = property.Value.ValueKind;
                    if (!TypeMatches(parameter.ParameterTypeName, kind))
                    {
                        diagnostics.Add(new CommandDiagnostic(
                            parameter.ParameterName,
                            $"参数类型不符：期望 {parameter.ParameterTypeName}，实际 {DescribeKind(kind)}",
                            $"把 {parameter.ParameterName} 的值改成 {parameter.ParameterTypeName} 类型",
                            BuildMinimalExample(descriptor)));
                    }
                }

                return diagnostics;
            }
        }

        private static JsonElement? FindProperty(JsonElement root, string parameterName)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static bool TypeMatches(string parameterTypeName, JsonValueKind kind)
        {
            switch (parameterTypeName)
            {
                case "String":
                    return kind == JsonValueKind.String;
                case "Boolean":
                    return kind == JsonValueKind.True || kind == JsonValueKind.False;
                case "Int32":
                case "Int64":
                    return kind == JsonValueKind.Number;
                default:
                    // 不在 String / Boolean / 整数三类里的类型不做类型检查，不产出诊断。
                    return true;
            }
        }

        private static string DescribeKind(JsonValueKind kind)
        {
            switch (kind)
            {
                case JsonValueKind.String: return "字符串";
                case JsonValueKind.Number: return "数字";
                case JsonValueKind.True:
                case JsonValueKind.False: return "布尔";
                case JsonValueKind.Array: return "数组";
                case JsonValueKind.Object: return "对象";
                case JsonValueKind.Null: return "空";
                default: return kind.ToString();
            }
        }

        private static string BuildMinimalExample(CommandDescriptor descriptor)
        {
            var payload = new Dictionary<string, object>();
            foreach (var parameter in descriptor.ParameterSchemas.Where(parameter => parameter.IsRequired))
            {
                payload[parameter.ParameterName] = PlaceholderFor(parameter.ParameterTypeName);
            }

            var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            return JsonSerializer.Serialize(payload, options);
        }

        private static object PlaceholderFor(string parameterTypeName)
        {
            switch (parameterTypeName)
            {
                case "String": return "<字符串>";
                case "Boolean": return false;
                case "Int32": return 0;
                default: return null;
            }
        }
    }
}
