using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scriban;
using Scriban.Runtime;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.CodeGen
{
    /// <summary>代码生成器：按目标渲染模板并落盘，产物幂等。</summary>
    public static class CodeGenerator
    {
        private static readonly IReadOnlyDictionary<string, string> ClrTypeNamesByFieldType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Int32"] = "int",
                ["Int64"] = "long",
                ["Single"] = "float",
                ["Boolean"] = "bool",
                ["String"] = "string"
            };

        /// <summary>渲染一条生成目标，返回产物文本。</summary>
        /// <param name="templateRoot">模板根目录，输入与模板都以此为基准。</param>
        /// <param name="target">生成目标。</param>
        public static string Render(string templateRoot, CodeGenerationTarget target)
        {
            if (!string.Equals(target.TargetKind, "TableAccess", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"暂不支持的生成种类：{target.TargetKind}");
            }

            var schema = SchemaLoader.LoadFromFile(Path.Combine(templateRoot, target.InputPath));
            var primaryKey = schema.Fields.FirstOrDefault(field => field.IsPrimaryKey)
                ?? throw new InvalidOperationException($"表「{schema.TableName}」没有标记主键字段");

            var model = new ScriptObject
            {
                ["输入路径"] = target.InputPath.Replace('\\', '/'),
                ["表名"] = schema.TableName,
                ["类名"] = schema.TableIdentifierName,
                ["主键类型"] = ToClrTypeName(primaryKey.TypeName),
                ["主键标识名"] = primaryKey.IdentifierName,
                ["主键参数名"] = ToCamelCase(primaryKey.IdentifierName),
                ["字段清单"] = schema.Fields
                    .Select(field => new ScriptObject
                    {
                        ["显示名"] = field.DisplayName,
                        ["标识名"] = field.IdentifierName,
                        ["类型"] = ToClrTypeName(field.TypeName)
                    })
                    .ToList()
            };

            var templatePath = Path.Combine(templateRoot, "Tools", "CodeGen", "Templates", target.TargetKind + ".scriban");

            // 模型的键是中文，MemberRenamer 保持原名，否则 Scriban 会按它的默认规则改写成 snake_case。
            // Scriban.Template 与本命名空间的 Template.* 同名，这里用全名消歧。
            var scribanTemplate = Scriban.Template.Parse(File.ReadAllText(templatePath));
            var context = new TemplateContext { MemberRenamer = member => member.Name };
            context.PushGlobal(model);
            var rendered = scribanTemplate.Render(context);

            // 换行统一并保证末尾正好一个换行：不同平台的换行差异会让「连跑两次一字不差」失效。
            return rendered.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        }

        /// <summary>渲染后与磁盘比对，内容有变才落盘；返回是否真的写了。</summary>
        /// <param name="templateRoot">模板根目录。</param>
        /// <param name="target">生成目标。</param>
        public static bool WriteIfChanged(string templateRoot, CodeGenerationTarget target)
        {
            var rendered = Render(templateRoot, target);
            var outputPath = Path.Combine(templateRoot, target.OutputPath);

            if (File.Exists(outputPath) && ReadNormalized(outputPath) == rendered)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, rendered);
            return true;
        }

        /// <summary>逐个目标比对磁盘产物，返回缺失或过期的说明文本。</summary>
        /// <param name="templateRoot">模板根目录。</param>
        /// <param name="configuration">生成目标清单。</param>
        public static IReadOnlyList<string> Verify(string templateRoot, CodeGenerationConfiguration configuration)
        {
            var problems = new List<string>();
            foreach (var target in configuration.Targets)
            {
                var outputPath = Path.Combine(templateRoot, target.OutputPath);
                if (!File.Exists(outputPath))
                {
                    problems.Add($"生成目标「{target.TargetName}」的产物尚未生成：{target.OutputPath}");
                    continue;
                }

                if (ReadNormalized(outputPath) != Render(templateRoot, target))
                {
                    problems.Add($"生成目标「{target.TargetName}」的产物与模板输出不一致：{target.OutputPath}，请重跑 codegen.run");
                }
            }

            return problems;
        }

        private static string ReadNormalized(string filePath)
        {
            return File.ReadAllText(filePath).Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        }

        private static string ToClrTypeName(string fieldTypeName)
        {
            if (ClrTypeNamesByFieldType.TryGetValue(fieldTypeName, out var clrTypeName))
            {
                return clrTypeName;
            }

            throw new NotSupportedException($"暂不支持的字段类型：{fieldTypeName}");
        }

        private static string ToCamelCase(string identifierName)
        {
            return char.ToLowerInvariant(identifierName[0]) + identifierName.Substring(1);
        }
    }
}
