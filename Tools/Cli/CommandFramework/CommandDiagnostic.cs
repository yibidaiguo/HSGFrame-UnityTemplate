namespace Template.Toolkit.CommandFramework
{
    /// <summary>一条命令参数诊断：位置、原因、修复动作与参考示例，四要素齐全。</summary>
    public sealed class CommandDiagnostic
    {
        /// <summary>
        /// 构造一条命令参数诊断。
        /// </summary>
        /// <param name="location">出问题的位置，例如参数名或文件路径。</param>
        /// <param name="reason">为什么不通过。</param>
        /// <param name="fixAction">具体要做什么才能修好。</param>
        /// <param name="referenceExample">一个可以照抄的示例。</param>
        public CommandDiagnostic(string location, string reason, string fixAction, string referenceExample)
        {
            Location = location;
            Reason = reason;
            FixAction = fixAction;
            ReferenceExample = referenceExample;
        }

        /// <summary>出问题的位置（参数名、文件路径）。</summary>
        public string Location { get; }

        /// <summary>为什么不通过。</summary>
        public string Reason { get; }

        /// <summary>具体要做什么才能修好。</summary>
        public string FixAction { get; }

        /// <summary>一个可以照抄的示例（示例值或范本文件路径）。</summary>
        public string ReferenceExample { get; }

        /// <summary>把四要素拼成一行给人读的中文文本，与门禁发现同格式。</summary>
        public override string ToString()
        {
            return $"位置：{Location}；原因：{Reason}；修复：{FixAction}；参考：{ReferenceExample}";
        }
    }
}
