using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>ValidationMessageCatalog 与 ValidationMessageEntry 的行为测试。</summary>
    public class ValidationMessageCatalogTests
    {
        /// <summary>条目总数恰好 14 条，且规则 id 互不重复。</summary>
        [Fact]
        public void EntriesCountIsFourteenAndRuleIdentifiersAreUnique()
        {
            var entries = ValidationMessageCatalog.Entries;

            Assert.Equal(14, entries.Count);
            Assert.Equal(entries.Count, entries.Select(e => e.RuleIdentifier).Distinct().Count());
        }

        /// <summary>每个条目的消息模板与修复模板都非空白。</summary>
        [Fact]
        public void EveryEntryHasNonBlankTemplates()
        {
            Assert.All(
                ValidationMessageCatalog.Entries,
                entry =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(entry.MessageTemplate));
                    Assert.False(string.IsNullOrWhiteSpace(entry.FixTemplate));
                });
        }

        /// <summary>Find 传不存在的规则 id 时抛 KeyNotFoundException，异常消息里带那个 id。</summary>
        [Fact]
        public void FindOnUnknownRuleThrowsKeyNotFoundExceptionWithId()
        {
            const string unknownId = "需求.不存在的规则";

            var exception = Assert.Throws<KeyNotFoundException>(() => ValidationMessageCatalog.Find(unknownId));

            Assert.Contains(unknownId, exception.Message);
        }

        /// <summary>Format 按索引占位符填入值，原文中的反斜杠与花括号照原样保留。</summary>
        [Fact]
        public void FormatFillsIndexPlaceholders()
        {
            var result = ValidationMessageCatalog.Format("需求.id模式", "REQ-x", "^REQ-\\d{4}$");

            Assert.Equal("字段「id」的值「REQ-x」不匹配 id 模式「^REQ-\\d{4}$」", result);
        }

        /// <summary>FormatFix 取该规则对应的修复模板。</summary>
        [Fact]
        public void FormatFixFillsFixTemplate()
        {
            var result = ValidationMessageCatalog.FormatFix("需求.id缺失");

            Assert.Equal("补上 id 字段", result);
        }

        /// <summary>构造 ValidationMessageEntry 时任一参数为 null 或空白都抛 ArgumentException。</summary>
        [Fact]
        public void ConstructorRejectsBlankOrNullArguments()
        {
            Assert.Throws<ArgumentException>(() => new ValidationMessageEntry("", "模板", "修复"));
            Assert.Throws<ArgumentException>(() => new ValidationMessageEntry("规则", "   ", "修复"));
            Assert.Throws<ArgumentException>(() => new ValidationMessageEntry("规则", "模板", null));
        }
    }
}
