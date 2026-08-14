namespace Template.Logic.Contracts
{
    // 占位接口：逻辑层日志输出点，由 Adapter.Unity 层实现
    public interface ILogSink
    {
        void Write(string 内容);
    }
}
