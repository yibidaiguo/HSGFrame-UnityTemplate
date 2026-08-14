using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 命令注册表：反射扫描程序集，把带 <see cref="EditorCommandAttribute"/> 标记的静态方法
    /// 收集成 <see cref="CommandDescriptor"/>，并从参数类推导出参数 schema。
    /// </summary>
    public static class CommandRegistry
    {
        /// <summary>
        /// 扫描给定程序集里的全部命令，返回按命令名升序排序的描述列表。
        /// </summary>
        /// <param name="assemblies">要扫描的程序集。</param>
        public static IReadOnlyList<CommandDescriptor> ScanAssemblies(params Assembly[] assemblies)
        {
            var descriptors = new List<CommandDescriptor>();

            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        var attribute = method.GetCustomAttribute<EditorCommandAttribute>();
                        if (attribute == null)
                        {
                            continue;
                        }

                        descriptors.Add(BuildDescriptor(attribute.CommandName, method));
                    }
                }
            }

            // 命令名重复会让后续 dispatch 产生歧义，直接失败而不是静默覆盖。
            var duplicates = descriptors
                .GroupBy(descriptor => descriptor.CommandName)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException("命令名重复：" + string.Join("、", duplicates));
            }

            return descriptors
                .OrderBy(descriptor => descriptor.CommandName)
                .ToList();
        }

        /// <summary>
        /// 把一条命令描述序列化成可读的 JSON。
        /// </summary>
        /// <param name="descriptor">命令描述。</param>
        public static string DescribeAsJson(CommandDescriptor descriptor)
        {
            var payload = new
            {
                descriptor.CommandName,
                descriptor.Description,
                Parameters = descriptor.ParameterSchemas
                    .Select(parameter => new
                    {
                        parameter.ParameterName,
                        parameter.ParameterTypeName,
                        parameter.IsRequired,
                        parameter.Description
                    })
                    .ToArray()
            };

            // UnsafeRelaxedJsonEscaping 让中文说明与键名原样输出，而不是转义成 \uXXXX。
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(payload, options);
        }

        private static CommandDescriptor BuildDescriptor(string commandName, MethodInfo method)
        {
            var parameters = method.GetParameters();

            // 命令方法的形状必须固定：返回 CommandResult，且参数正好一个引用类型参数类。
            var shapeInvalid = method.ReturnType != typeof(CommandResult)
                || parameters.Length != 1
                || parameters[0].ParameterType.IsValueType;
            if (shapeInvalid)
            {
                throw new InvalidOperationException(
                    "命令方法形状不合法：" + method.DeclaringType.FullName + "." + method.Name +
                    "，期望返回 CommandResult 且带一个引用类型参数类。");
            }

            var argumentType = parameters[0].ParameterType;
            var schemas = argumentType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(BuildParameterSchema)
                .ToList();

            var description = method.GetCustomAttribute<SummaryAttribute>()?.Description ?? string.Empty;

            return new CommandDescriptor(commandName, description, argumentType, method, schemas);
        }

        private static CommandParameterSchema BuildParameterSchema(PropertyInfo property)
        {
            var summary = property.GetCustomAttribute<SummaryAttribute>();
            var hasDefaultValue = property.GetCustomAttribute<DefaultValueAttribute>() != null;

            return new CommandParameterSchema(
                property.Name,
                property.PropertyType.Name,
                isRequired: !hasDefaultValue,
                description: summary?.Description ?? string.Empty);
        }
    }
}
