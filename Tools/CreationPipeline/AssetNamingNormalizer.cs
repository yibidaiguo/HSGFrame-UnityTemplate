using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次命名归一的结果：最终用哪个名字、动没动过、动了什么。</summary>
    public sealed class AssetNamingOutcome
    {
        /// <summary>构造一次归一结果。</summary>
        /// <param name="naming">最终用的名字。</param>
        /// <param name="changed">动过没有。</param>
        /// <param name="note">动了什么，一句人话；没动时为空串。</param>
        public AssetNamingOutcome(string naming, bool changed, string note)
        {
            Naming = naming ?? "";
            Changed = changed;
            Note = note ?? "";
        }

        /// <summary>最终用的名字。</summary>
        public string Naming { get; }

        /// <summary>动过没有。</summary>
        public bool Changed { get; }

        /// <summary>动了什么，一句人话；没动时为空串。</summary>
        public string Note { get; }
    }

    /// <summary>
    /// 按资产类型的命名模式把名字补成合规的。
    ///
    /// 为什么要有：命名模式是硬规则（图标 <c>^icon_[a-z0-9_]+$</c>、界面底图 <c>^ui_[a-z0-9_]+$</c>），
    /// 而名字是助手从聊天里起的——它给「bag_ui_effect」，规格门禁当场判红并把文件删掉。
    /// 让人为了一个前缀重说一遍需求，是把机器该干的活推给人。
    ///
    /// **补的只有确定的那部分**：从模式开头抠出**字面前缀**（`^` 之后到第一个正则元字符为止），
    /// 再把其余部分归一成小写下划线。抠不出前缀、或补完仍不匹配，就**原样返回并说清楚**——
    /// 那时该让规格门禁去判红，而不是在这里猜一个能过的名字。
    /// </summary>
    public static class AssetNamingNormalizer
    {
        /// <summary>
        /// 把名字补成匹配该模式的形状。
        /// </summary>
        /// <param name="namingText">助手或人给的名字。</param>
        /// <param name="namingPattern">该资产类型的命名模式，如 <c>^icon_[a-z0-9_]+$</c>。</param>
        public static AssetNamingOutcome Normalize(string namingText, string namingPattern)
        {
            var naming = (namingText ?? "").Trim();
            var pattern = (namingPattern ?? "").Trim();

            if (pattern.Length == 0)
            {
                return new AssetNamingOutcome(naming, false, "");
            }

            if (Matches(naming, pattern))
            {
                return new AssetNamingOutcome(naming, false, "");
            }

            var prefix = LiteralPrefix(pattern);
            if (prefix.Length == 0)
            {
                return new AssetNamingOutcome(
                    naming,
                    false,
                    "命名不匹配「" + pattern + "」，而这条模式里抠不出确定的前缀，没敢替你改");
            }

            // 风格由模式自己说了算：模式允许大写字母就拼 PascalCase，否则拼小写下划线。
            // 贴图那一类的模式是 ^T_[A-Za-z0-9]+$（《结构规范-资源》第五节的前缀表 + PascalCase），
            // 只会 slug 的话拼出来是 T_bag_panel，与模式不符，一路走「没敢替你改」——
            // 结果是每一张图都要人手工改名，而机器本来能算准。
            var pascal = AllowsUpperCase(pattern);
            var body = pascal ? Pascal(naming) : Slug(naming);

            // 名字里已经含着前缀那个词时先把它摘掉，免得补出「ui_bag_ui_effect」这种叠词。
            // 只摘**整段相等**的那一节，不做模糊匹配——模糊匹配会把「uiux」这种词也切了。
            var stem = prefix.TrimEnd('_');
            if (stem.Length > 0 && !pascal)
            {
                var kept = new List<string>();
                foreach (var part in body.Split('_'))
                {
                    if (part.Length > 0 && !string.Equals(part, stem, StringComparison.Ordinal))
                    {
                        kept.Add(part);
                    }
                }

                body = string.Join("_", kept);
            }

            if (body.Length == 0)
            {
                return new AssetNamingOutcome(naming, false, "命名去掉不合规的字符之后什么都不剩，没敢替你改");
            }

            var composed = prefix + body;
            if (!Matches(composed, pattern))
            {
                return new AssetNamingOutcome(
                    naming,
                    false,
                    "命名「" + naming + "」补成「" + composed + "」仍然不匹配「" + pattern + "」，没敢替你改");
            }

            return new AssetNamingOutcome(
                composed,
                true,
                "命名「" + naming + "」不合「" + pattern + "」，已补成「" + composed + "」");
        }

        /// <summary>
        /// 这条模式允不允许大写字母。允许就说明它要的是 PascalCase 那种风格。
        ///
        /// 判据只看**字符组里有没有大写字母范围**（`A-Z`），不看前缀里的大写：
        /// `^T_[a-z0-9_]+$` 的前缀虽是大写的 T，正文却只许小写，那仍旧是下划线风格。
        /// </summary>
        /// <param name="pattern">命名模式。</param>
        private static bool AllowsUpperCase(string pattern)
        {
            return pattern.Contains("A-Z", StringComparison.Ordinal);
        }

        /// <summary>
        /// 把一句话拼成 PascalCase：非字母数字当分词点，每一节首字母大写。
        ///
        /// 已经是驼峰的那种（`bagPanel`）**不再切**——只在分隔符处断词。
        /// 硬按大小写切的话，`UIBag` 会被切成 `U`、`I`、`Bag`，拼回来是 `UIBag` 还好，
        /// 但 `PCPanel` 会变成 `PCPanel`→`P`,`C`,`Panel`，缩写就散了。
        /// </summary>
        /// <param name="text">原始名字。</param>
        public static string Pascal(string text)
        {
            var builder = new StringBuilder();
            var atWordStart = true;
            foreach (var character in text ?? "")
            {
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9'))
                {
                    builder.Append(atWordStart ? char.ToUpperInvariant(character) : character);
                    atWordStart = false;
                }
                else
                {
                    atWordStart = true;
                }
            }

            return builder.ToString();
        }

        /// <summary>名字匹配这条模式没有；模式本身不合法时一律判不匹配（让调用方走「没敢改」那一支）。</summary>
        /// <param name="naming">名字。</param>
        /// <param name="pattern">命名模式。</param>
        private static bool Matches(string naming, string pattern)
        {
            if (naming.Length == 0)
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(naming, pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 从模式开头抠出字面前缀：<c>^</c> 之后一路取到第一个正则元字符为止。
        /// <c>^icon_[a-z0-9_]+$</c> 给 <c>icon_</c>；模式不以 <c>^</c> 开头、或开头就是元字符，给空串。
        /// </summary>
        /// <param name="pattern">命名模式。</param>
        public static string LiteralPrefix(string pattern)
        {
            if (pattern.Length == 0 || pattern[0] != '^')
            {
                return "";
            }

            var builder = new StringBuilder();
            for (var index = 1; index < pattern.Length; index++)
            {
                var character = pattern[index];
                if (MetaCharacters.IndexOf(character) >= 0)
                {
                    break;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>正则元字符：碰到它们就说明字面前缀到头了。</summary>
        private const string MetaCharacters = "[](){}.*+?|\\^$";

        /// <summary>把一段文本归一成小写下划线：非字母数字一律换下划线，连着的下划线并成一个，两头不留。</summary>
        /// <param name="text">原文。</param>
        public static string Slug(string text)
        {
            var builder = new StringBuilder();
            foreach (var character in (text ?? "").ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }
    }
}
