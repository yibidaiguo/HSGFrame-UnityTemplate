using System;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>实体 id 分配器：扫描现存编号取下一个可用值，按指定位数补零。</summary>
    public static class IdentifierAllocator
    {
        /// <summary>
        /// 返回目录中下一个可用的实体编号：现存最大编号加一，按指定位数补零。
        /// 目录不存在或没有匹配的编号时返回前缀加最小编号。
        /// </summary>
        /// <param name="directory">存放实体 JSON 的目录。</param>
        /// <param name="prefix">编号前缀，如「REQ-」。</param>
        /// <param name="digits">编号数字部分的位数。</param>
        public static string Next(string directory, string prefix, int digits)
        {
            if (!Directory.Exists(directory))
            {
                return Format(prefix, 1, digits);
            }

            var maxNumber = 0;
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var numberPart = fileName.Substring(prefix.Length);
                if (!IsAllDigits(numberPart))
                {
                    continue;
                }

                if (int.TryParse(numberPart, out var number) && number > maxNumber)
                {
                    maxNumber = number;
                }
            }

            return Format(prefix, maxNumber + 1, digits);
        }

        private static string Format(string prefix, int number, int digits)
        {
            return prefix + number.ToString().PadLeft(digits, '0');
        }

        private static bool IsAllDigits(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (var character in text)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
