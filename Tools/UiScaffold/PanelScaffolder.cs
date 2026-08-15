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

            var outputs = new List<string>();
            foreach (var (templateFileName, outputFileName) in OutputFiles(definition))
            {
                outputs.Add(RenderToFile(templatesDirectory, templateFileName, definition, Path.Combine(outputDirectory, outputFileName)));
            }

            return outputs;
        }

        /// <summary>逐个渲染三件套并与磁盘现有产物比对，返回缺失或不一致的说明文本。</summary>
        /// <param name="templateRoot">模板根目录（模板树自身的根），模板文件以此为基准定位。</param>
        /// <param name="definition">面板定义模型。</param>
        /// <param name="outputDirectory">三件套输出目录。</param>
        public static IReadOnlyList<string> Verify(string templateRoot, UiPanelDefinitionSource definition, string outputDirectory)
        {
            var templatesDirectory = Path.Combine(templateRoot, TemplatesDirectoryRelativePath);
            var problems = new List<string>();

            foreach (var (templateFileName, outputFileName) in OutputFiles(definition))
            {
                var outputPath = Path.Combine(outputDirectory, outputFileName);
                if (!File.Exists(outputPath))
                {
                    problems.Add($"产物尚未生成：{outputFileName}");
                    continue;
                }

                var rendered = Render(templatesDirectory, templateFileName, definition);
                var existing = NormalizeNewlines(File.ReadAllText(outputPath)).TrimEnd('\n') + "\n";
                if (existing != rendered)
                {
                    problems.Add($"产物与定义不一致：{outputFileName}，跑 ui.scaffold 重新生成");
                }
            }

            return problems;
        }

        private static string RenderToFile(string templatesDirectory, string templateFileName, UiPanelDefinitionSource definition, string outputPath)
        {
            var content = Render(templatesDirectory, templateFileName, definition);
            File.WriteAllText(outputPath, content, new UTF8Encoding(false));
            return outputPath;
        }

        // 渲染出内容与落盘是两件事：Verify 只比内容不落盘，落盘路径与校验路径共用这一段，
        // 避免两处各写一份渲染逻辑日后慢慢分叉。
        private static string Render(string templatesDirectory, string templateFileName, UiPanelDefinitionSource definition)
        {
            var templateText = NormalizeNewlines(File.ReadAllText(Path.Combine(templatesDirectory, templateFileName)));
            var template = Scriban.Template.Parse(templateText);
            var rendered = template.Render(new { definition });
            var content = NormalizeNewlines(rendered);
            return content.TrimEnd('\n') + "\n";
        }

        private static (string TemplateFileName, string OutputFileName)[] OutputFiles(UiPanelDefinitionSource definition)
        {
            return new[]
            {
                ("Panel.uxml.scriban", definition.PanelIdentifierName + ".uxml"),
                ("Panel.uss.scriban", definition.PanelIdentifierName + ".uss"),
                ("Panel.cs.scriban", definition.PanelIdentifierName + ".cs"),
            };
        }

        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
