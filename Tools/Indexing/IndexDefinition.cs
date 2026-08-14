namespace Template.Toolkit.Indexing
{
    /// <summary>一类索引的定义：名字、扫描根、文件通配与输出路径。</summary>
    public sealed class IndexDefinition
    {
        /// <summary>索引名，例如「技能索引」。</summary>
        public string IndexName { get; set; }

        /// <summary>扫描根目录，相对仓库根。</summary>
        public string SourceRoot { get; set; }

        /// <summary>要命中的文件通配，例如 *.json。</summary>
        public string FilePattern { get; set; }

        /// <summary>索引输出路径，相对仓库根。</summary>
        public string OutputPath { get; set; }
    }
}
