using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Scriban;

namespace Template.Toolkit.UiScaffold
{
    /// <summary>从面板定义渲染 UXML / USS / C# 三件套。</summary>
    public static class PanelScaffolder
    {
        private static readonly string TemplatesDirectoryRelativePath = Path.Combine("Template", "Tools", "UiScaffold", "Templates");

        /// <summary>按面板标识名生成 UXML / USS / C# 三件套，返回写出的文件路径列表。</summary>
        /// <param name="repositoryRoot">仓库根目录，模板文件以此为基准定位。</param>
        /// <param name="definition">面板定义模型。</param>
        /// <param name="outputDirectory">三件套输出目录，不存在时自动创建。</param>
        public static IReadOnlyList<string> Scaffold(string repositoryRoot, UiPanelDefinitionSource definition, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var templatesDirectory = Path.Combine(repositoryRoot, TemplatesDirectoryRelativePath);

            var outputs = new List<string>
            {
                RenderToFile(templatesDirectory, "Panel.uxml.scriban", definition, Path.Combine(outputDirectory, definition.PanelIdentifierName + ".uxml")),
                RenderToFile(templatesDirectory, "Panel.uss.scriban", definition, Path.Combine(outputDirectory, definition.PanelIdentifierName + ".uss")),
                RenderToFile(templatesDirectory, "Panel.cs.scriban", definition, Path.Combine(outputDirectory, definition.PanelIdentifierName + ".cs")),
            };

            return outputs;
        }

        private static string RenderToFile(string templatesDirectory, string templateFileName, UiPanelDefinitionSource definition, string outputPath)
        {
            var templateText = NormalizeNewlines(File.ReadAllText(Path.Combine(templatesDirectory, templateFileName)));
            var template = Scriban.Template.Parse(templateText);
            var rendered = template.Render(new { definition });
            var content = NormalizeNewlines(rendered);
            content = content.TrimEnd('\n') + "\n";
            File.WriteAllText(outputPath, content, new UTF8Encoding(false));
            return outputPath;
        }

        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
