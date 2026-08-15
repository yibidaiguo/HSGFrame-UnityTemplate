using System;
using System.Collections.Generic;

namespace GameTemplateForAgent.Localization
{
    /// <summary>
    /// 本地化目录：按语言与键查文案，查不到时按回退链逐级降级。
    /// 查不到最终返回键本身而不是空串——空串会让界面上出现一片空白，
    /// 返回键本身则一眼能看出「这条没翻译」：可见的缺失优于静默的缺失。
    /// </summary>
    public sealed class LocalizationCatalog
    {
        private readonly Dictionary<string, Dictionary<LanguageType, string>> _contentByKey =
            new Dictionary<string, Dictionary<LanguageType, string>>();

        private LanguageType _currentLanguage;

        /// <summary>用回退语言构造，查不到当前语言的文案时降级到它。</summary>
        public LocalizationCatalog(LanguageType fallbackLanguage = LanguageType.English)
        {
            FallbackLanguage = fallbackLanguage;
            _currentLanguage = fallbackLanguage;
        }

        /// <summary>当前语言。</summary>
        public LanguageType CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage == value)
                {
                    return;
                }

                _currentLanguage = value;
                LanguageChanged?.Invoke(value);
            }
        }

        /// <summary>回退语言。</summary>
        public LanguageType FallbackLanguage { get; }

        /// <summary>已登记的键数量。</summary>
        public int KeyCount => _contentByKey.Count;

        /// <summary>给某个语言登记一条文案，同一语言同一键重复登记时后者覆盖前者。</summary>
        public void Register(LanguageType language, string key, string content)
        {
            ValidateKey(key);

            if (!_contentByKey.TryGetValue(key, out var contentByLanguage))
            {
                contentByLanguage = new Dictionary<LanguageType, string>();
                _contentByKey.Add(key, contentByLanguage);
            }

            contentByLanguage[language] = content;
        }

        /// <summary>按当前语言取文案：当前语言没有就降级到回退语言，还没有就返回键本身。</summary>
        public string Resolve(string key)
        {
            return Resolve(_currentLanguage, key);
        }

        /// <summary>按指定语言取文案，语义同按当前语言取。</summary>
        public string Resolve(LanguageType language, string key)
        {
            ValidateKey(key);

            if (!_contentByKey.TryGetValue(key, out var contentByLanguage))
            {
                return key;
            }

            if (contentByLanguage.TryGetValue(language, out var content))
            {
                return content;
            }

            if (contentByLanguage.TryGetValue(FallbackLanguage, out content))
            {
                return content;
            }

            return key;
        }

        /// <summary>某个键在某个语言下有没有文案。</summary>
        public bool Contains(LanguageType language, string key)
        {
            ValidateKey(key);

            return _contentByKey.TryGetValue(key, out var contentByLanguage)
                && contentByLanguage.ContainsKey(language);
        }

        /// <summary>当前语言变化时触发。</summary>
        public event Action<LanguageType> LanguageChanged;

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException(
                    "位置：LocalizationCatalog；原因：键是空串或 null；修复：传入非空的键字符串；参考：参见 LocalizationCatalog.Register 的 key 参数说明");
            }
        }
    }
}
