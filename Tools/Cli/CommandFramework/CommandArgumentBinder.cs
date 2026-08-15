using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 参数绑定器：把参数 JSON 反序列化成参数对象，并给 JSON 里没出现的属性填上
    /// <see cref="DefaultValueAttribute"/> 声明的值。
    /// 框架统一在这里填值，命令体就不必各写一遍兜底——「标了默认值却没兜底」这类缺陷
    /// 已经在两条命令上各栽过一次，都是端到端跑才发现。
    /// </summary>
    public static class CommandArgumentBinder
    {
        private static readonly JsonSerializerOptions DeserializeOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>按命令描述绑定一份参数 JSON，返回已填好默认值的参数对象。</summary>
        /// <param name="descriptor">命令描述。</param>
        /// <param name="argumentsJson">参数 JSON 原文，空文本按空对象处理。</param>
        public static object Bind(CommandDescriptor descriptor, string argumentsJson)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            var json = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
            var arguments = JsonSerializer.Deserialize(json, descriptor.ArgumentType, DeserializeOptions)
                ?? Activator.CreateInstance(descriptor.ArgumentType);

            ApplyDefaults(arguments, descriptor.ArgumentType, json);
            return arguments;
        }

        // 只给「JSON 里根本没写这一项」的属性填默认值。
        // 按「值等于类型默认值就覆盖」去判是错的：JSON 里显式写了 false 而默认值是 true 时，
        // 那种判法会把调用方写的 false 悄悄改成 true。
        private static void ApplyDefaults(object arguments, Type argumentType, string json)
        {
            var presentNames = ReadPresentPropertyNames(json);
            foreach (var property in argumentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                var attribute = property.GetCustomAttribute<DefaultValueAttribute>();
                if (attribute == null || presentNames.Contains(property.Name))
                {
                    continue;
                }

                property.SetValue(arguments, ConvertTo(attribute.Value, property.PropertyType));
            }
        }

        private static HashSet<string> ReadPresentPropertyNames(string json)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return names;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                names.Add(property.Name);
            }

            return names;
        }

        // 特性里的值是 object，声明成 int 的默认值遇到 long 类型的属性要换算，
        // 可空类型要拆出底层类型再换算，否则 SetValue 直接抛。
        private static object ConvertTo(object value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlyingType.IsEnum)
            {
                return Enum.Parse(underlyingType, value.ToString(), ignoreCase: true);
            }

            return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }
    }
}
