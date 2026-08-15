using System;
using GameTemplateForAgent.Localization;
using Xunit;

namespace GameTemplateForAgent.Localization.Tests
{
    /// <summary>本地化目录的登记、回退降级与语言变化事件测试。</summary>
    public class LocalizationCatalogTests
    {
        [Fact]
        public void RegisterThenResolveReturnsContentForCurrentLanguage()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");

            Assert.Equal("Hello", catalog.Resolve("greeting"));
        }

        [Fact]
        public void ResolveReturnsOtherLanguageAfterSwitching()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");
            catalog.Register(LanguageType.SimplifiedChinese, "greeting", "你好");

            catalog.CurrentLanguage = LanguageType.SimplifiedChinese;

            Assert.Equal("你好", catalog.Resolve("greeting"));
        }

        [Fact]
        public void ResolveFallsBackWhenCurrentLanguageMissing()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");
            catalog.CurrentLanguage = LanguageType.SimplifiedChinese;

            Assert.Equal("Hello", catalog.Resolve("greeting"));
        }

        [Fact]
        public void ResolveReturnsKeyWhenBothLanguagesMissing()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.French, "greeting", "Bonjour");
            catalog.CurrentLanguage = LanguageType.German;

            Assert.Equal("greeting", catalog.Resolve("greeting"));
        }

        [Fact]
        public void ResolveReturnsKeyWhenFallbackLanguageAlsoMissing()
        {
            var catalog = new LocalizationCatalog(LanguageType.English);
            catalog.Register(LanguageType.SimplifiedChinese, "greeting", "你好");

            Assert.Equal("greeting", catalog.Resolve(LanguageType.English, "greeting"));
        }

        [Fact]
        public void ContainsReturnsTrueForRegisteredLanguage()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");

            Assert.True(catalog.Contains(LanguageType.English, "greeting"));
        }

        [Fact]
        public void ContainsReturnsFalseForMissingLanguage()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");

            Assert.False(catalog.Contains(LanguageType.French, "greeting"));
            Assert.False(catalog.Contains(LanguageType.English, "missing"));
        }

        [Fact]
        public void KeyCountReflectsDistinctKeys()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");
            catalog.Register(LanguageType.SimplifiedChinese, "greeting", "你好");
            catalog.Register(LanguageType.English, "farewell", "Bye");

            Assert.Equal(2, catalog.KeyCount);
        }

        [Fact]
        public void DuplicateRegisterOverwrites()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");
            catalog.Register(LanguageType.English, "greeting", "Hi");

            Assert.Equal("Hi", catalog.Resolve("greeting"));
        }

        [Fact]
        public void LanguageChangedFiresWhenLanguageChanges()
        {
            var catalog = new LocalizationCatalog();
            var received = default(LanguageType);
            catalog.LanguageChanged += language => received = language;

            catalog.CurrentLanguage = LanguageType.Japanese;

            Assert.Equal(LanguageType.Japanese, received);
        }

        [Fact]
        public void LanguageChangedDoesNotFireWhenLanguageUnchanged()
        {
            var catalog = new LocalizationCatalog();
            catalog.CurrentLanguage = LanguageType.English;
            var firedCount = 0;
            catalog.LanguageChanged += _ => firedCount++;

            catalog.CurrentLanguage = LanguageType.English;

            Assert.Equal(0, firedCount);
        }

        [Fact]
        public void ResolveWithExplicitLanguageIgnoresCurrentLanguage()
        {
            var catalog = new LocalizationCatalog();
            catalog.Register(LanguageType.English, "greeting", "Hello");
            catalog.Register(LanguageType.SimplifiedChinese, "greeting", "你好");
            catalog.CurrentLanguage = LanguageType.SimplifiedChinese;

            Assert.Equal("Hello", catalog.Resolve(LanguageType.English, "greeting"));
        }

        [Fact]
        public void EmptyKeyThrowsArgumentException()
        {
            var catalog = new LocalizationCatalog();

            var exception = Assert.Throws<ArgumentException>(() => catalog.Register(LanguageType.English, "", "内容"));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void NullKeyThrowsArgumentException()
        {
            var catalog = new LocalizationCatalog();

            var exception = Assert.Throws<ArgumentException>(() => catalog.Resolve(null));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }
    }
}
