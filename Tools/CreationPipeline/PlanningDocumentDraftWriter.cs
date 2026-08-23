using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把冷启动草案的人写区落成一份 `index.md`。
    ///
    /// **只写人写区**：生成区留给 <see cref="PlanningDocumentRenderer"/> 紧接着补上。
    /// 分开是因为两者的来源根本不同——人写区来自模型的一次性草案（往后由人接手维护），
    /// 生成区来自正本、每次都能重算。混在一处写，下次重渲染时分不清哪些该保留。
    /// </summary>
    public static class PlanningDocumentDraftWriter
    {
        /// <summary>没给正文时摆的占位符。</summary>
        private const string PlaceholderLine = "（待补）";

        /// <summary>
        /// 写一份草案。**文件已存在时直接抛**——这条路只管冷启动，不覆盖任何人写过的东西。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="sections">各小节正文，键是小节标题。</param>
        /// <param name="specification">模块策划案规范，决定小节顺序与 frontmatter 取值。</param>
        /// <exception cref="IOException">目标已存在或写不下去时抛出。</exception>
        public static string Write(
            string poolRoot,
            string moduleName,
            IReadOnlyDictionary<string, string> sections,
            PlanningDocumentSpec specification)
        {
            var path = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
            if (File.Exists(path))
            {
                throw new IOException($"{path} 已经存在，冷启动不覆盖已有的策划案");
            }

            var title = sections != null && sections.TryGetValue("标题", out var declaredTitle)
                && declaredTitle.Trim().Length > 0
                ? declaredTitle.Trim()
                : moduleName;

            var builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("模块: ").Append(moduleName).Append('\n');
            builder.Append("标题: ").Append(title).Append('\n');
            builder.Append("状态: ")
                .Append(specification.StatusValues.Count > 0 ? specification.StatusValues[0] : "生效").Append('\n');
            builder.Append("文档版本: 1\n");
            builder.Append("权威侧: 项目\n");
            builder.Append("配置表: []\n");
            builder.Append("---\n\n");
            builder.Append("# ").Append(title).Append("\n\n");

            foreach (var section in specification.RequiredSections)
            {
                var body = sections != null && sections.TryGetValue(section, out var value)
                    && value.Trim().Length > 0
                    ? value.Trim()
                    : PlaceholderLine;

                builder.Append("## ").Append(section).Append('\n').Append(body).Append("\n\n");
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return path;
        }
    }
}
