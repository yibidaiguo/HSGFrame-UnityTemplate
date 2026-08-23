using System;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 总设计层：一个项目一份、人主动写、不依赖任何业务需求的大方向陈述。
    ///
    /// **它是默认档里唯一被完整读进去的文档**（子文档 10 §三）。
    /// 设计库越全，「每次全读一遍」越贵，而 token 全花在每次都一样的那部分上——
    /// 所以默认只读这一份短的，设计记录、汇总、别的模块的东西只在人开口时才读。
    ///
    /// 短是硬要求：它要能被塞进每一次调用而不心疼。写不下说明在往里塞细节，
    /// 细节该去模块定稿或设计记录。
    /// </summary>
    public sealed class DesignDirection
    {
        /// <summary>构造一份总设计层。</summary>
        /// <param name="text">全文。</param>
        /// <param name="filePath">来源文件路径。</param>
        public DesignDirection(string text, string filePath)
        {
            Text = text ?? "";
            FilePath = filePath ?? "";
        }

        /// <summary>全文。</summary>
        public string Text { get; }

        /// <summary>来源文件路径。</summary>
        public string FilePath { get; }

        /// <summary>正文有没有实质内容。空文件与只有标题的文件都算没有。</summary>
        public bool HasContent
        {
            get { return Text.Trim().Length > 0; }
        }

        /// <summary>行数，给长度门禁用。</summary>
        public int LineCount
        {
            get { return Text.Length == 0 ? 0 : Text.Split('\n').Length; }
        }

        /// <summary>总设计层的落点：Pools/Designs/Direction.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string FilePathFor(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Pools", "Designs", "Direction.md");
        }

        /// <summary>
        /// 读总设计层。
        /// **文件不存在不算失败**——那是冷启动的正常入口（子文档 10 §四），
        /// 调用方拿到 null 之后该去跟人聊，不是报错。
        /// 读不动（权限、IO）才算失败，那和「还没写」是两回事。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="direction">读到的总设计层；文件不存在时为 null。</param>
        /// <param name="reason">读不动的原因；不存在或成功时为空串。</param>
        public static bool TryRead(string repositoryRoot, out DesignDirection direction, out string reason)
        {
            direction = null;
            reason = "";

            var path = FilePathFor(repositoryRoot);
            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                direction = new DesignDirection(File.ReadAllText(path), path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = "总设计层读不动：" + exception.Message;
                return false;
            }
        }
    }
}
