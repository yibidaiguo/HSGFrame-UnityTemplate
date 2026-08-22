namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次配置写入的结局：成没成、一句人话、以及落到哪个文件。
    /// 写失败一律给原因，不静默吞——面板上点了「保存」什么都没发生，比报错糟得多。
    ///
    /// 密钥红线（决策 5、78）：这个类型的「消息」里绝不许出现任何密钥的值。
    /// 写入器本身就拒绝写密钥字段，所以这里也不会有机会拿到它。
    /// </summary>
    public sealed class ConfigWriteOutcome
    {
        /// <summary>
        /// 构造一次写入结局。
        /// </summary>
        /// <param name="succeeded">写成了没有。</param>
        /// <param name="message">一句人话：成了说改了什么，没成说为什么。</param>
        /// <param name="filePath">落盘的文件路径；没写成时是打算写的那个文件。</param>
        public ConfigWriteOutcome(bool succeeded, string message, string filePath)
        {
            Succeeded = succeeded;
            Message = message ?? "";
            FilePath = filePath ?? "";
        }

        /// <summary>写成了没有。</summary>
        public bool Succeeded { get; }

        /// <summary>一句人话：成了说改了什么，没成说为什么。</summary>
        public string Message { get; }

        /// <summary>落盘的文件路径；没写成时是打算写的那个文件。</summary>
        public string FilePath { get; }

        /// <summary>造一个失败结局。</summary>
        /// <param name="message">失败原因。</param>
        /// <param name="filePath">打算写的文件。</param>
        public static ConfigWriteOutcome Failure(string message, string filePath)
        {
            return new ConfigWriteOutcome(false, message, filePath);
        }

        /// <summary>造一个成功结局。</summary>
        /// <param name="message">改了什么。</param>
        /// <param name="filePath">落盘的文件。</param>
        public static ConfigWriteOutcome Success(string message, string filePath)
        {
            return new ConfigWriteOutcome(true, message, filePath);
        }
    }
}
