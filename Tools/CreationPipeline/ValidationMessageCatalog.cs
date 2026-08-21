using System;
using System.Collections.Generic;
using System.Globalization;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条校验错误文案：规则 id、消息模板与修复模板三件套。</summary>
    public sealed class ValidationMessageEntry
    {
        /// <summary>
        /// 构造一条校验错误文案条目。
        /// </summary>
        /// <param name="ruleIdentifier">规则 id，与校验器里报错处一一对应。</param>
        /// <param name="messageTemplate">消息模板，用 `{0}` `{1}` 这类索引占位符。</param>
        /// <param name="fixTemplate">修复建议模板，占位符规则同消息模板。</param>
        /// <exception cref="ArgumentException">任一参数为 null 或空白时抛出。</exception>
        public ValidationMessageEntry(string ruleIdentifier, string messageTemplate, string fixTemplate)
        {
            if (string.IsNullOrWhiteSpace(ruleIdentifier))
            {
                throw new ArgumentException("规则 id 不能为 null 或空白。", nameof(ruleIdentifier));
            }

            if (string.IsNullOrWhiteSpace(messageTemplate))
            {
                throw new ArgumentException("消息模板不能为 null 或空白。", nameof(messageTemplate));
            }

            if (string.IsNullOrWhiteSpace(fixTemplate))
            {
                throw new ArgumentException("修复模板不能为 null 或空白。", nameof(fixTemplate));
            }

            RuleIdentifier = ruleIdentifier;
            MessageTemplate = messageTemplate;
            FixTemplate = fixTemplate;
        }

        /// <summary>规则 id，与校验器里报错处一一对应。</summary>
        public string RuleIdentifier { get; }

        /// <summary>消息模板，用 `{0}` `{1}` 这类索引占位符。</summary>
        public string MessageTemplate { get; }

        /// <summary>修复建议模板，占位符规则同消息模板。</summary>
        public string FixTemplate { get; }
    }

    /// <summary>需求校验错误文案的唯一来源：规则 id → 中文文案模板，pool.pull 拒收与助手提示共用。</summary>
    public static class ValidationMessageCatalog
    {
        /// <summary>规则 id → 文案模板的条目表，顺序即导出顺序。</summary>
        public static IReadOnlyList<ValidationMessageEntry> Entries { get; } = new[]
        {
            new ValidationMessageEntry(
                "需求.JSON语法",
                "JSON 语法错误：{0}",
                "修复 JSON 语法后重新校验"),
            new ValidationMessageEntry(
                "需求.id缺失",
                "缺少字段「id」",
                "补上 id 字段"),
            new ValidationMessageEntry(
                "需求.id模式",
                "字段「id」的值「{0}」不匹配 id 模式「{1}」",
                "把 id 改成匹配 id 模式的格式"),
            new ValidationMessageEntry(
                "需求.id与目录名",
                "字段「id」的值「{0}」与所在目录名「{1}」不一致",
                "让目录名与字段 id 保持一致"),
            new ValidationMessageEntry(
                "需求.骨架缺失",
                "需求目录「{0}」里没有 requirement.json",
                "补上 requirement.json，或把这个不是需求的目录挪走"),
            new ValidationMessageEntry(
                "需求.必填缺失",
                "必填字段「{0}」缺失或为 null",
                "补上该字段并给一个非 null 的值"),
            new ValidationMessageEntry(
                "需求.必填空串",
                "必填字段「{0}」是空字符串",
                "填上实际内容"),
            new ValidationMessageEntry(
                "需求.枚举越界",
                "字段「{0}」的值不在枚举「{1}」里",
                "改成合法的枚举值"),
            new ValidationMessageEntry(
                "需求.非数组",
                "字段「{0}」的值不是数组",
                "把该字段的值改成 JSON 数组"),
            new ValidationMessageEntry(
                "需求.数组过短",
                "字段「{0}」的数组条数 {1} 少于最少条数 {2}",
                "补足数组条数"),
            new ValidationMessageEntry(
                "需求.非对象",
                "字段「{0}」的值不是对象",
                "把该字段的值改成 JSON 对象"),
            new ValidationMessageEntry(
                "需求.非布尔",
                "字段「{0}」的值不是 true/false",
                "把该字段的值改成 true 或 false"),
            new ValidationMessageEntry(
                "需求.分类型必填",
                "类型「{0}」的必填字段「{1}」缺失或为空",
                "补上该类型要求的字段"),
            new ValidationMessageEntry(
                "需求.未声明字段",
                "字段「{0}」未在合并 schema 中声明",
                "删掉该字段，或在项目扩展 schema 里声明它"),
        };

        /// <summary>
        /// 按规则 id 查文案条目。
        /// </summary>
        /// <param name="ruleIdentifier">规则 id。</param>
        /// <returns>匹配的文案条目。</returns>
        /// <exception cref="KeyNotFoundException">找不到该规则 id 时抛出，异常消息里带上传入的 id。</exception>
        public static ValidationMessageEntry Find(string ruleIdentifier)
        {
            foreach (var entry in Entries)
            {
                if (entry.RuleIdentifier == ruleIdentifier)
                {
                    return entry;
                }
            }

            throw new KeyNotFoundException($"找不到规则「{ruleIdentifier}」的校验文案条目。");
        }

        /// <summary>
        /// 取消息模板并按不变区域设置格式化。
        /// </summary>
        /// <param name="ruleIdentifier">规则 id。</param>
        /// <param name="values">填入模板占位符的值。</param>
        /// <returns>格式化后的中文提示。</returns>
        public static string Format(string ruleIdentifier, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, Find(ruleIdentifier).MessageTemplate, values);
        }

        /// <summary>
        /// 取修复模板并按不变区域设置格式化。
        /// </summary>
        /// <param name="ruleIdentifier">规则 id。</param>
        /// <param name="values">填入模板占位符的值。</param>
        /// <returns>格式化后的修复建议。</returns>
        public static string FormatFix(string ruleIdentifier, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, Find(ruleIdentifier).FixTemplate, values);
        }
    }
}
