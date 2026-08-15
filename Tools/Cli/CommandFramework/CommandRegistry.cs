using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
                foreach (var type in SafeGetTypes(assembly))
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
        /// 扫描一个目录下全部 <c>*.dll</c> 里的命令，返回按命令名升序排序的描述列表。
        /// 宿主用它扫自己的输出目录：工具库只要把 dll 放在宿主旁边就会被发现，
        /// 不必为了让某个库自带命令而去改宿主的工程文件。
        /// </summary>
        /// <param name="directoryPath">要扫描的目录。</param>
        public static IReadOnlyList<CommandDescriptor> ScanDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return Array.Empty<CommandDescriptor>();
            }

            var assemblies = new List<Assembly>();

            // 按文件名排序遍历，扫描顺序才不随文件系统的枚举顺序变——
            // 顺序一变，「命令名重复」那条报错里列出的名字顺序也跟着变，日志就对不上。
            foreach (var filePath in Directory.GetFiles(directoryPath, "*.dll").OrderBy(path => path, StringComparer.Ordinal))
            {
                var assembly = TryLoad(filePath);
                if (assembly != null)
                {
                    assemblies.Add(assembly);
                }
            }

            return ScanAssemblies(assemblies.ToArray());
        }

        // 输出目录里躺着一堆与命令无关的 dll（第三方库、原生互操作壳、资源程序集）。
        // 加载不了的一律跳过：为了一个本来就不带命令的 dll 让整条命令层起不来，代价不对等。
        private static Assembly TryLoad(string filePath)
        {
            try
            {
                return Assembly.LoadFrom(filePath);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
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

        // 依赖缺失时 GetTypes() 会抛，但异常里的 Types 数组仍然带着能加载的那部分。
        // 命令类型通常正是能加载的那部分，所以取残片继续，而不是整个程序集放弃。
        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }
    }
}
