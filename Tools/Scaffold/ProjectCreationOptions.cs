namespace Template.Toolkit.Scaffold
{
    /// <summary>新项目生成的输入参数。</summary>
    public sealed class ProjectCreationOptions
    {
        /// <summary>模板根目录（通常是仓库里的 Template）。</summary>
        public string TemplateRoot { get; set; }

        /// <summary>新项目要落在哪个目录。</summary>
        public string TargetDirectory { get; set; }

        /// <summary>新项目名，同时也是模板树复制过去之后的目录名。</summary>
        public string ProjectName { get; set; }

        /// <summary>新的 UPM 包前缀，形如 com.example.（结尾带点）。</summary>
        public string PackagePrefix { get; set; }
    }
}
