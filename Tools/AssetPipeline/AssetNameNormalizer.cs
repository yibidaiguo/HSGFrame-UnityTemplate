using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>按导入规则把资产文件名规范成「前缀 + PascalCase 主干 + 小写扩展名」。</summary>
    public static class AssetNameNormalizer
    {
        /// <summary>规范化单个文件名：分隔符归一、ASCII 词 PascalCase、中文与数字原样、补前缀、扩展名小写。</summary>
        /// <param name="originalFileName">原始文件名（可含扩展名）。</param>
        /// <param name="rule">目录导入规则，用于补文件名前缀。</param>
        public static string Normalize(string originalFileName, AssetImportRule rule)
        {
            var extension = Path.GetExtension(originalFileName);
            var stem = extension.Length == 0
                ? originalFileName
                : originalFileName.Substring(0, originalFileName.Length - extension.Length);

            var words = ReplaceSeparators(stem)
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(PascalCaseWord)
                .ToList();

            var normalizedStem = JoinWords(words);
            if (string.IsNullOrEmpty(normalizedStem))
            {
                normalizedStem = "未命名";
            }

            var prefix = rule?.FileNamePrefix ?? string.Empty;
            if (!string.IsNullOrEmpty(prefix) && !normalizedStem.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalizedStem = prefix + normalizedStem;
            }

            return normalizedStem + extension.ToLowerInvariant();
        }

        /// <summary>枚举目录下需改名的文件，产出重命名计划；已规范的跳过，同目录新名撞车时追加序号去重。</summary>
        /// <param name="directoryPath">要整理的目录。</param>
        /// <param name="rule">目录导入规则。</param>
        public static IReadOnlyList<AssetRenamePlan> PlanDirectory(string directoryPath, AssetImportRule rule)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<AssetRenamePlan>();
            }

            var occupiedNames = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<(string FileName, string Normalized)>();

            foreach (var filePath in Directory.EnumerateFiles(directoryPath))
            {
                var fileName = Path.GetFileName(filePath);
                occupiedNames.Add(fileName);

                if (ShouldSkip(fileName))
                {
                    continue;
                }

                var normalized = Normalize(fileName, rule);
                if (string.Equals(normalized, fileName, StringComparison.Ordinal))
                {
                    continue;
                }

                candidates.Add((fileName, normalized));
            }

            // 按原文件名升序，让同一批输入的重命名结果稳定可复现。
            candidates.Sort((left, right) => string.CompareOrdinal(left.FileName, right.FileName));

            var plans = new List<AssetRenamePlan>();
            foreach (var candidate in candidates)
            {
                var targetName = ResolveCollision(candidate.Normalized, occupiedNames);
                occupiedNames.Add(targetName);
                plans.Add(new AssetRenamePlan(
                    Path.Combine(directoryPath, candidate.FileName),
                    targetName,
                    "命名与目录规范不一致"));
            }

            return plans;
        }

        private static bool ShouldSkip(string fileName)
        {
            return fileName.EndsWith(".meta", StringComparison.Ordinal)
                || string.Equals(fileName, "导入规则.json", StringComparison.Ordinal);
        }

        // 撞车去重：两个乱名可能归一后同名（例如「a b.png」与「a_b.png」都归一成 AB.png），
        // 直接落同一目标会互相覆盖，所以后分配的往后缀追加 _2、_3，直到不再撞已占用名。
        private static string ResolveCollision(string normalized, HashSet<string> occupiedNames)
        {
            if (!occupiedNames.Contains(normalized))
            {
                return normalized;
            }

            var extension = Path.GetExtension(normalized);
            var stem = extension.Length == 0
                ? normalized
                : normalized.Substring(0, normalized.Length - extension.Length);

            var counter = 2;
            while (true)
            {
                var candidate = stem + "_" + counter + extension;
                if (!occupiedNames.Contains(candidate))
                {
                    return candidate;
                }

                counter++;
            }
        }

        // PascalCase 只作用于 ASCII 字母：对词首字符转大写、其余转小写。
        // 中文字符与数字经 ToUpper/ToLower 不变，因此中文词与纯数字词天然原样保留。
        private static string PascalCaseWord(string word)
        {
            if (word.Length == 0)
            {
                return word;
            }

            var builder = new StringBuilder(word.Length);
            builder.Append(char.ToUpperInvariant(word[0]));
            for (var index = 1; index < word.Length; index++)
            {
                builder.Append(char.ToLowerInvariant(word[index]));
            }

            return builder.ToString();
        }

        // 相邻两个纯 ASCII 字母词直接驼峰拼接（hero_texture → HeroTexture）；
        // 一旦遇到中文词或数字词就用下划线隔开（英雄_贴图_01 保持分段可读）。
        private static string JoinWords(IReadOnlyList<string> words)
        {
            var builder = new StringBuilder();
            var previousWasAsciiLetterWord = false;

            foreach (var word in words)
            {
                if (IsAsciiLetterWord(word))
                {
                    if (builder.Length > 0 && !previousWasAsciiLetterWord)
                    {
                        builder.Append('_');
                    }

                    builder.Append(word);
                    previousWasAsciiLetterWord = true;
                }
                else
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('_');
                    }

                    builder.Append(word);
                    previousWasAsciiLetterWord = false;
                }
            }

            return builder.ToString();
        }

        private static bool IsAsciiLetterWord(string word)
        {
            foreach (var character in word)
            {
                if (!IsAsciiLetter(character))
                {
                    return false;
                }
            }

            return word.Length > 0;
        }

        private static bool IsAsciiLetter(char character)
        {
            return (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z');
        }

        // 空格、连字符、点、加号、半角圆括号、全角圆括号统一折成下划线，作为后续切词的分隔符。
        private static string ReplaceSeparators(string stem)
        {
            var builder = new StringBuilder(stem.Length);
            foreach (var character in stem)
            {
                builder.Append(IsSeparator(character) ? '_' : character);
            }

            return builder.ToString();
        }

        private static bool IsSeparator(char character)
        {
            return character == ' ' || character == '-' || character == '.'
                || character == '+' || character == '(' || character == ')'
                || character == '（' || character == '）';
        }
    }
}
