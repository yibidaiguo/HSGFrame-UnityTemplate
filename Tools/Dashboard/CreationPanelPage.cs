using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Template.Toolkit.Dashboard
{
    /// <summary>
    /// 创作管线面板页面：总览 / 需求池 / 任务 / 任务图 / 引擎 / 资产 / 设计池 / 门禁 / 审查 / 冲突 /
    /// 放行流水 / 规范 / 晋升 / 提案待批 / 供给对账 / 下游 十六页装在一份自包含 HTML 里，
    /// 零外部依赖、零 CDN。每页都是「拉一次 /api/panel/* 再渲染」，页面自己不存业务状态。
    ///
    /// 页面正文住在 Web/panel.html 与 Web/panel.js 两个真文件里，编译时嵌进程序集，
    /// 装配时把脚本填进 HTML 的占位处。从前这两样是拼在 C# verbatim 字符串里的：
    /// 一个写错的引号转义就会吐出半个字面量，整份脚本语法错、十六页一页都不渲染，
    /// 而 C# 编译、单元测试、全量门禁全是绿的——因为没人解析过那段 JS。
    /// 挪成真文件之后，那类雷从根上不存在了：JS 就是 JS，引号是什么就是什么。
    /// </summary>
    public static class CreationPanelPage
    {
        /// <summary>HTML 里等着被脚本正文替换掉的占位记号。</summary>
        private const string ScriptPlaceholder = "/*脚本占位*/";

        /// <summary>脚本里等着被白名单放行族替换掉的占位记号。</summary>
        private const string WhitelistPlaceholder = "/*白名单占位*/";

        /// <summary>面板的完整 HTML 文档：模板 HTML 装配上脚本正文。</summary>
        public static string Html { get; } = Assemble();

        private static string Assemble()
        {
            var document = ReadEmbeddedResource("panel.html");
            var script = ReadEmbeddedResource("panel.js");
            if (document.IndexOf(ScriptPlaceholder, StringComparison.Ordinal) < 0)
            {
                // 占位记号被人从 panel.html 里删掉了：这时装配出来的页面会是一份没有脚本的空壳，
                // 十六页全白——而编译与测试照样绿。宁可当场炸，也不交一份看着正常的死页面。
                throw new InvalidOperationException(
                    $"panel.html 里找不到脚本占位记号 {ScriptPlaceholder}，装配不出可用的面板页面");
            }

            if (script.IndexOf(WhitelistPlaceholder, StringComparison.Ordinal) < 0)
            {
                // 占位记号没了，脚本里的放行族就永远是空数组：下游页每个试跑按钮都会被判成
                // 「不在放行族里」而变灰。那是一份看着正常、其实什么都点不动的页面，照样得当场炸。
                throw new InvalidOperationException(
                    $"panel.js 里找不到白名单占位记号 {WhitelistPlaceholder}，放行族填不进去");
            }

            script = script.Replace(WhitelistPlaceholder + "[]", BuildWhitelistLiteral());
            return document.Replace(ScriptPlaceholder, script);
        }

        /// <summary>
        /// 把白名单放行族拼成一段 JS 数组字面量。真相只有 <see cref="PanelCommandWhitelist"/> 那一份，
        /// 页面照抄一份只为「跑不了的按钮不给」；在这里拼而不是在 JS 里写死，是为了它俩不会各说各话。
        /// </summary>
        private static string BuildWhitelistLiteral()
        {
            var quoted = new List<string>();
            foreach (var prefix in PanelCommandWhitelist.AllowedPrefixes)
            {
                // 放行族是我们自己写死的几个 ASCII 前缀，这里仍然逐个查一遍：
                // 哪天有人往里加了带引号的东西，宁可炸也不要吐出一段坏掉的 JS。
                if (prefix.IndexOf('"') >= 0 || prefix.IndexOf('\\') >= 0)
                {
                    throw new InvalidOperationException($"白名单前缀「{prefix}」含引号或反斜杠，拼不成 JS 字面量");
                }

                quoted.Add("\"" + prefix + "\"");
            }

            return "[" + string.Join(", ", quoted) + "]";
        }

        /// <summary>读嵌入到本程序集里的网页资源；找不到就抛，不返回空串。</summary>
        private static string ReadEmbeddedResource(string fileName)
        {
            var assembly = typeof(CreationPanelPage).GetTypeInfo().Assembly;
            var resourceName = "Template.Toolkit.Dashboard.Web." + fileName;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        $"程序集里没有嵌入资源 {resourceName}，检查 Dashboard.csproj 的 EmbeddedResource 配置");
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
