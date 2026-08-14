using System;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 把某个静态方法标记为编辑器命令，供命令宿主反射扫描并调用。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class EditorCommandAttribute : Attribute
    {
        /// <summary>
        /// 用命令名标记一个静态方法为编辑器命令。
        /// </summary>
        /// <param name="commandName">命令名，例如 compile.check。</param>
        public EditorCommandAttribute(string commandName)
        {
            CommandName = commandName;
        }

        /// <summary>命令名，例如 compile.check。</summary>
        public string CommandName { get; }
    }
}
