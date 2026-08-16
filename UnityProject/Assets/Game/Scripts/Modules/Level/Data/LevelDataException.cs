using System;

namespace Template.Level.Data
{
    /// <summary>关卡数据读取或解析失败时抛出，消息按四要素书写。</summary>
    public sealed class LevelDataException : Exception
    {
        /// <summary>用一条四要素消息构造异常。</summary>
        public LevelDataException(string message) : base(message)
        {
        }

        /// <summary>用一条四要素消息与内层异常构造异常。</summary>
        public LevelDataException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
