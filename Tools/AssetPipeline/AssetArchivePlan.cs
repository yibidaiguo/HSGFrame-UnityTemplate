using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条归档计划：某个收件箱文件将被移到哪个目录、改成什么名字。</summary>
    public sealed class AssetArchivePlan
    {
        /// <summary>构造一条归档计划。</summary>
        /// <param name="sourcePath">收件箱里的源文件完整路径。</param>
        /// <param name="targetDirectory">归档目标目录完整路径。</param>
        /// <param name="targetFileName">归档后的目标文件名（含扩展名）。</param>
        public AssetArchivePlan(string sourcePath, string targetDirectory, string targetFileName)
        {
            SourcePath = sourcePath;
            TargetDirectory = targetDirectory;
            TargetFileName = targetFileName;
            TargetPath = Path.Combine(targetDirectory, targetFileName);
        }

        /// <summary>收件箱里的源文件完整路径。</summary>
        public string SourcePath { get; }

        /// <summary>归档目标目录完整路径。</summary>
        public string TargetDirectory { get; }

        /// <summary>归档后的目标文件名（含扩展名）。</summary>
        public string TargetFileName { get; }

        /// <summary>目标完整路径。</summary>
        public string TargetPath { get; }
    }
}
