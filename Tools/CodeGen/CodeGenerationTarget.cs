namespace Template.Toolkit.CodeGen
{
    /// <summary>一条代码生成目标：名字、种类、输入与产物路径。</summary>
    public sealed class CodeGenerationTarget
    {
        /// <summary>目标名，用于报错时点名是哪一条。</summary>
        public string TargetName { get; set; }

        /// <summary>目标种类，决定用哪个模板，例如 TableAccess。</summary>
        public string TargetKind { get; set; }

        /// <summary>输入文件路径，相对模板根。</summary>
        public string InputPath { get; set; }

        /// <summary>产物路径，相对模板根。</summary>
        public string OutputPath { get; set; }
    }
}
