using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一条需求动了之后，把它所属模块的策划案重渲染一遍。
    ///
    /// **为什么这一步必须自动**：需求走到「已完成」的那一刻，模块策划案就过期了——
    /// 它的「需求」那一节还写着进行中，「界面与交互」还是上一版。
    /// 而过期的正本比没有正本更糟：人会照着它做决定，还以为自己看的是现状。
    /// 靠人记得去点一下，等于把正本的时效性押在纪律上，而纪律一定会漏。
    ///
    /// 渲不动**不抛异常**：调用它的都是别的事情的收尾（推出站、出功能图），
    /// 那些事情已经成了，不该因为一份投影没渲出来就整体判失败。
    /// </summary>
    public static class ModulePlanRefresher
    {
        /// <summary>
        /// 照一条需求的「专项」找到模块，重渲染那份策划案。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="notes">这一趟做了什么、为什么跳过，一句一条。</param>
        /// <param name="alsoPush">渲完顺手推知识库；干跑或只读模式下给 false。</param>
        /// <param name="timeoutSeconds">推知识库的超时秒数。</param>
        /// <returns>真渲了就回 true；跳过或失败回 false。</returns>
        public static bool RefreshForRequirement(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            out IReadOnlyList<string> notes,
            bool alsoPush = false,
            int timeoutSeconds = 60)
        {
            var lines = new List<string>();
            notes = lines;

            var moduleName = ReadEpic(poolRoot, requirementIdentifier);
            if (moduleName.Length == 0)
            {
                // 没挂专项就没有模块可渲。**如实说而不是静默跳过**——
                // 静默的后果是人以为策划案更新了，其实一次都没渲过。
                lines.Add($"{requirementIdentifier} 没挂专项，模块策划案没得渲");
                return false;
            }

            return Refresh(repositoryRoot, poolRoot, moduleName, lines, alsoPush, timeoutSeconds);
        }

        /// <summary>
        /// 重渲染一个模块的策划案。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="notes">这一趟做了什么，一句一条。</param>
        /// <param name="alsoPush">渲完顺手推知识库。</param>
        /// <param name="timeoutSeconds">推知识库的超时秒数。</param>
        public static bool Refresh(
            string repositoryRoot,
            string poolRoot,
            string moduleName,
            List<string> notes,
            bool alsoPush = false,
            int timeoutSeconds = 60)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                notes.Add("模块名是空的，模块策划案没得渲");
                return false;
            }

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                notes.Add("模块策划案规范读不动，这次没渲：" + exception.Message);
                return false;
            }

            try
            {
                var outcome = PlanningDocumentRenderer.Render(
                    repositoryRoot, poolRoot, moduleName, specification, isDryRun: false);

                notes.Add($"模块策划案（{moduleName}）："
                    + (outcome.IsCreated ? "新建" : outcome.IsChanged ? "刷新" : "无变化"));
                foreach (var note in outcome.Notes)
                {
                    notes.Add("  " + note);
                }

                // 推那一步**只在真渲出变化时才走**，而且推之前还会再比一次正文哈希
                // （ModulePlanPusher 自己比）。两道判据看着重复，其实管的是两件事：
                // 这里挡的是「什么都没变还去调下游」，那里挡的是「渲变了但正文与上次推的一样」
                // ——后者会发生，因为同步账本身就写在 frontmatter 里，不进正文哈希。
                if (alsoPush && outcome.IsChanged)
                {
                    var pushOutcome = ModulePlanPusher.PushOne(
                        repositoryRoot, poolRoot, moduleName, specification,
                        isDryRun: false, isForced: false, timeoutSeconds: timeoutSeconds);
                    notes.Add("  " + pushOutcome.Note);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                notes.Add($"模块策划案（{moduleName}）渲不动：{exception.Message}");
                return false;
            }
        }

        /// <summary>读一条需求的「专项」——这个项目里它就是模块名。读不出给空串，不瞎猜一个。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static string ReadEpic(string poolRoot, string requirementIdentifier)
        {
            try
            {
                var file = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
                if (!File.Exists(file))
                {
                    return "";
                }

                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("专项", out var element)
                    && element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? ""
                    : "";
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                return "";
            }
        }
    }
}
