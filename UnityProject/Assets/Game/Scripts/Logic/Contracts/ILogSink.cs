namespace Template.Logic.Contracts
{
    /// <summary>逻辑层的日志输出点，由 Adapter.Unity 层实现。</summary>
    public interface ILogSink
    {
        /// <summary>写一行日志。</summary>
        void Write(string content);
    }
}
