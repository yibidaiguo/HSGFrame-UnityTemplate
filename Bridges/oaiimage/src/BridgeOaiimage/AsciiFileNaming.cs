using System.Text;

namespace Template.Bridges.Oaiimage
{
    /// <summary>
    /// 把任意文本压成能当文件名用的 ASCII 词干。
    ///
    /// **为什么必须有这一道**：<c>gate.pathascii</c> 是 block 级——全仓的目录名与文件名一律只许 ASCII。
    /// 而资产请求的「命名」字段按 schema 是给人看的名字，实际项目里就是中文（「主菜单图标」）。
    /// 拿它直接当出图文件名，变体一落盘门禁当场红，而且红的是一道跟生图毫无关系的门禁，
    /// 看的人对不上因果。所以落盘前一律过这里。
    /// </summary>
    public static class AsciiFileNaming
    {
        /// <summary>清干净之后什么都不剩时用的名字。</summary>
        public const string FallbackStem = "variant";

        /// <summary>
        /// 把文本压成 <c>[A-Za-z0-9._-]</c> 的词干：合法字符原样留下，其余一律换成 <c>-</c>，
        /// 连续的 <c>-</c> 收成一个，首尾的 <c>-</c> 去掉；清空了就退回 <see cref="FallbackStem"/>。
        /// </summary>
        /// <param name="text">原始文本，可能是中文。</param>
        public static string ToAsciiStem(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return FallbackStem;
            }

            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if ((character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '.' || character == '_' || character == '-')
                {
                    builder.Append(character);
                    continue;
                }

                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }

            var stem = builder.ToString().Trim('-');
            return stem.Length == 0 ? FallbackStem : stem;
        }
    }
}
