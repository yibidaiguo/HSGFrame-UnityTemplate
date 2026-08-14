using System;
using System.Collections.Generic;
using System.Reflection;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 一条命令的完整描述：命令名、说明、参数类型、可反射调用的方法以及参数 schema 列表。
    /// </summary>
    public sealed class CommandDescriptor
    {
        /// <summary>
        /// 构造一条命令描述。
        /// </summary>
        /// <param name="commandName">命令名。</param>
        /// <param name="description">来自方法上 <see cref="SummaryAttribute"/> 的说明。</param>
        /// <param name="argumentType">命令方法的参数类型（强类型参数类）。</param>
        /// <param name="method">被 <see cref="EditorCommandAttribute"/> 标记的静态方法。</param>
        /// <param name="parameterSchemas">从参数类推导出的参数 schema 列表。</param>
        public CommandDescriptor(
            string commandName,
            string description,
            Type argumentType,
            MethodInfo method,
            IReadOnlyList<CommandParameterSchema> parameterSchemas)
        {
            CommandName = commandName;
            Description = description;
            ArgumentType = argumentType;
            Method = method;
            ParameterSchemas = parameterSchemas;
        }

        /// <summary>命令名，例如 compile.check。</summary>
        public string CommandName { get; }

        /// <summary>来自方法上 <see cref="SummaryAttribute"/> 的中文说明。</summary>
        public string Description { get; }

        /// <summary>命令方法的参数类型（强类型参数类）。</summary>
        public Type ArgumentType { get; }

        /// <summary>被 <see cref="EditorCommandAttribute"/> 标记的静态方法。</summary>
        public MethodInfo Method { get; }

        /// <summary>从参数类推导出的参数 schema 列表。</summary>
        public IReadOnlyList<CommandParameterSchema> ParameterSchemas { get; }

        /// <summary>
        /// 直接反射调用命令方法。
        /// </summary>
        /// <param name="arguments">已经反序列化好的参数对象。</param>
        public CommandResult Invoke(object arguments)
        {
            return (CommandResult)Method.Invoke(null, new[] { arguments });
        }
    }
}
