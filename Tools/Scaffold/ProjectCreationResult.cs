namespace Template.Toolkit.Scaffold
{
    /// <summary>新项目生成的执行结果。</summary>
    public sealed class ProjectCreationResult
    {
        /// <summary>是否生成成功。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>面向人的结果消息，失败时说明是哪一项不满足。</summary>
        public string Message { get; private set; }

        /// <summary>实际复制到目标的文件数，被跳过的生成目录不计入。</summary>
        public int CreatedFileCount { get; private set; }

        /// <summary>新项目的目标路径，等于 TargetDirectory/ProjectName。</summary>
        public string TargetPath { get; private set; }

        /// <summary>
        /// 构造一个成功结果。
        /// </summary>
        /// <param name="targetPath">新项目落点。</param>
        /// <param name="createdFileCount">实际复制的文件数。</param>
        /// <param name="message">结果消息。</param>
        public static ProjectCreationResult Success(string targetPath, int createdFileCount, string message)
        {
            return new ProjectCreationResult
            {
                IsSuccess = true,
                Message = message,
                CreatedFileCount = createdFileCount,
                TargetPath = targetPath
            };
        }

        /// <summary>
        /// 构造一个失败结果，文件数记零、不落任何目录。
        /// </summary>
        /// <param name="message">失败原因。</param>
        public static ProjectCreationResult Failure(string message)
        {
            return new ProjectCreationResult
            {
                IsSuccess = false,
                Message = message,
                CreatedFileCount = 0,
                TargetPath = null
            };
        }
    }
}
