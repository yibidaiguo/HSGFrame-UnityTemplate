using System.Collections.Generic;

namespace GameTemplateForAgent.Localization
{
    /// <summary>按键查译文的本地化查表，查不到时原样返回键。</summary>
    public sealed class LocalizationTable
    {
        private readonly Dictionary<string, string> _entries = new Dictionary<string, string>();

        /// <summary>登记一条键到译文的映射。</summary>
        public void Add(string key, string translation) => _entries[key] = translation;

        /// <summary>查键对应的译文，查不到时返回键本身。</summary>
        public string Translate(string key) => _entries.TryGetValue(key, out var translation) ? translation : key;
    }
}
