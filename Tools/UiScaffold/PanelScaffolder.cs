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
        // 相对模板根定位，而不是相对仓库根：模板树被生成脚本复制成别的项目名之后，
        // 写死 "Template" 这一段就找不到模板文件了。
        private static readonly string TemplatesDirectoryRelativePath = Path.Combine("Tools", "UiScaffold", "Templates");

        /// <summary>按面板标识名生成 UXML / USS / C# 三件套，返回写出的文件路径列表。</summary>
        /// <param name="templateRoot">模板根目录（模板树自身的根），模板文件以此为基准定位。</param>
        /// <param name="definition">面板定义模型。</param>
        /// <param name="outputDirectory">三件套输出目录，不存在时自动创建。</param>
        public static IReadOnlyList<string> Scaffold(string templateRoot, UiPanelDefinitionSource definition, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var templatesDirectory = Path.Combine(templateRoot, TemplatesDirectoryRelativePath);

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
