using System;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 门禁报告的结论：绿 / 红 / 未跑。
    ///
    /// **这个类存在的唯一理由是「只算一处」。** 结论不是报告里的一个字段——
    /// `_Generated/gate-report.json` 只有「时间」与「条目」两个键，绿红是**推出来的**
    /// （每一道都成功才绿）。谁要用结论就自己推一遍的话，两处推法只要差一点，
    /// 面板上就会出现同一件事两个答案：总览页写「绿」而进度页写「未跑」，
    /// 而人没有任何办法知道该信哪一页。真踩过一次（进度页照着一个不存在的「结论」键读）。
    /// </summary>
    public static class GateReportConclusion
    {
        /// <summary>结论：全绿。</summary>
        public const string Green = "绿";

        /// <summary>结论：有道次不过。</summary>
        public const string Red = "红";

        /// <summary>
        /// 结论：还没跑过。
        /// **报告读不了、形状不对也算「未跑」**——它们与「跑过且全绿」是两件事，
        /// 而与「还没跑」在人要做的下一步上是同一件事：去跑一次门禁。
        /// </summary>
        public const string NotRun = "未跑";

        /// <summary>报告里放各道门禁的那个数组键。</summary>
        public const string EntriesKey = "条目";

        /// <summary>一道门禁的结果键。</summary>
        public const string ResultKey = "结果";

        /// <summary>一道门禁「过了」的结果值。</summary>
        public const string SucceededResult = "成功";

        /// <summary>
        /// 逐道结果算不算「过」。抽成方法是为了让面板也能问同一个问题——
        /// 页面自己认一套词的时候，三十道全成功的报告在页面上显示成 0 / 30 通过。
        /// </summary>
        /// <param name="result">报告里那一道的「结果」值。</param>
        public static bool IsPassed(string result)
        {
            return string.Equals(result, SucceededResult, StringComparison.Ordinal);
        }

        /// <summary>门禁报告路径：_Generated/gate-report.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ReportFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "_Generated", "gate-report.json");
        }

        /// <summary>
        /// 推这一份报告的结论。文件不在、读不动、顶层不是对象、没有「条目」数组，
        /// 四种都给 <see cref="NotRun"/>；有条目则**每一道都成功才绿**，
        /// 一道不成功就红（空数组算绿：跑过了，零道不过）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string Read(string repositoryRoot)
        {
            var reportPath = ReportFile(repositoryRoot);
            if (!File.Exists(reportPath))
            {
                return NotRun;
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(reportPath)))
                {
                    return FromDocument(document.RootElement);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return NotRun;
            }
        }

        /// <summary>
        /// 从一份已经解析好的报告里推结论。抽出来是给已经把 JSON 读在手里的调用方用的
        /// （面板的门禁页要顺便把每一道摊成行，不该为了拿结论再读一遍文件）。
        /// </summary>
        /// <param name="root">报告顶层对象。</param>
        public static string FromDocument(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(EntriesKey, out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return NotRun;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var result = entry.TryGetProperty(ResultKey, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
                if (!string.Equals(result, SucceededResult, StringComparison.Ordinal))
                {
                    return Red;
                }
            }

            return Green;
        }
    }
}
