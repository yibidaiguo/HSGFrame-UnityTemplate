using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 任务状态文本树渲染：task.status 命令与面板任务页同源的数据。
    /// 渲染不许抛异常，任何读不到的东西都降级成文字。
    /// </summary>
    public static class TaskStatusReport
    {
        /// <summary>
        /// 渲染单个需求的任务状态文本树。
        /// 需求文件缺失时只渲染一行「需求文件不存在」；任务状态文件缺失时只渲染两行；
        /// 需求文件的标题与状态取不到时分别写空串与「未知」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string RenderOne(string repositoryRoot, string poolRoot, string requirementIdentifier)
        {
            var requirementPath = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
            if (!File.Exists(requirementPath))
            {
                return $"{requirementIdentifier} 需求文件不存在";
            }

            var title = "";
            var status = "未知";
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(requirementPath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        title = ReadStringOrEmpty(root, "标题");
                        var rawStatus = ReadStringOrEmpty(root, "状态");
                        if (rawStatus.Length > 0)
                        {
                            status = rawStatus;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 需求文件读不了就当作没有标题与状态，继续渲染任务状态部分。
            }

            var header = $"{requirementIdentifier} {title} [{status}]";

            if (!TaskState.TryLoad(repositoryRoot, requirementIdentifier, out var state, out _))
            {
                return header + Environment.NewLine + "└─ 尚未开跑（任务状态文件不存在）";
            }

            var currentWorkItem = string.IsNullOrEmpty(state.CurrentWorkItem) ? "无" : state.CurrentWorkItem;
            var pendingGate = string.IsNullOrEmpty(state.PendingGate) ? "无" : state.PendingGate;

            return string.Join(Environment.NewLine,
                header,
                $"├─ 阶段：{state.Stage} / {state.SubState}",
                $"├─ 当前工作项：{currentWorkItem}",
                $"├─ 关卡待审：{pendingGate}",
                $"├─ 预算：llm {state.Budget.LanguageModelUsed}/{state.Budget.LanguageModelLimit} · 生图 {state.Budget.ImageUsed}/{state.Budget.ImageLimit}",
                $"└─ 产物：{state.ArtifactHashes.Count} 件");
        }

        /// <summary>
        /// 渲染 _Tasks/ 下全部任务的状态文本树：
        /// 目录名当需求 id，按序数序排序，逐个调 RenderOne，用空行隔开拼成一整段。
        /// _Tasks/ 目录不存在或没有任何子目录时返回「当前没有任务」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static string RenderAll(string repositoryRoot, string poolRoot)
        {
            var taskDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(taskDirectory))
            {
                return "当前没有任务";
            }

            var identifiers = Directory.GetDirectories(taskDirectory)
                .Select(directory => Path.GetFileName(directory))
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (identifiers.Count == 0)
            {
                return "当前没有任务";
            }

            var sections = new List<string>();
            foreach (var identifier in identifiers)
            {
                sections.Add(RenderOne(repositoryRoot, poolRoot, identifier));
            }

            return string.Join(Environment.NewLine + Environment.NewLine, sections);
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
