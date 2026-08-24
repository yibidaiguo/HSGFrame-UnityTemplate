using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>提交资产命令 asset.submit 的参数。</summary>
    public sealed class AssetSubmitArguments
    {
        /// <summary>本机源文件：人在飞书里丢过来、桥取回本地的那个附件。</summary>
        [Summary("本机源文件（附件取回来的那个）")]
        public string SourcePath { get; set; }

        /// <summary>资产类型，照资产规格里的名字；留空表示没推出来，命令会回来问。</summary>
        [Summary("资产类型，照资产规格里的名字；留空表示没推出来")]
        [DefaultValue("")]
        public string AssetType { get; set; }

        /// <summary>模块名；留空表示没推出来，命令会回来问。</summary>
        [Summary("模块名；留空表示没推出来")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>落地叫什么（不带扩展名），要匹配这一类的命名模式。</summary>
        [Summary("落地叫什么（不带扩展名），要匹配这一类的命名模式")]
        [DefaultValue("")]
        public string Naming { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>Unity 工程目录，落点相对它解析。</summary>
        [Summary("Unity 工程目录，落点相对它解析")]
        [DefaultValue("UnityProject")]
        public string UnityProjectRoot { get; set; }

        /// <summary>
        /// 人确认过了没有。**缺省 false**：这一步会写正式环境（`UnityProject/Assets/`），
        /// 而任务书那条判据说得很清楚——会毁东西的一步留确认。
        /// </summary>
        [Summary("人确认过了没有；缺省 false，只算不落")]
        [DefaultValue(false)]
        public bool Confirmed { get; set; }
    }

    /// <summary>
    /// 提交资产：人在飞书里丢一个图 / 模型 / 音频，AI 把能推的都推出来，
    /// 算清「落到哪、叫什么」，**人点头之后**才写进正式资产目录。
    ///
    /// 分成「算」与「落」两趟不是流程洁癖：写 `UnityProject/Assets/` 是写正式环境，
    /// 落错目录之后要人手工去挪，而挪完 .meta 的 guid 又会跟着变。
    /// 算那一趟不碰任何文件，人看清了再点。
    /// </summary>
    public static class AssetSubmitCommands
    {
        /// <summary>
        /// 算一次提交（或在确认之后真落盘）。
        /// </summary>
        /// <param name="arguments">提交资产命令参数。</param>
        [EditorCommand("asset.submit")]
        [Summary("提交资产：推出类型/落点/命名，人确认之后落进正式资产目录")]
        public static CommandResult Submit(AssetSubmitArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.SourcePath))
            {
                return CommandResult.Failure("必须给 --SourcePath（附件取回来的那个本机文件）");
            }

            var repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var sourcePath = Path.GetFullPath(arguments.SourcePath);
            var lines = new List<string>();

            var plan = AssetSubmission.Plan(repositoryRoot, sourcePath, arguments.AssetType, arguments.Module, arguments.Naming);

            foreach (var blocker in plan.Blockers)
            {
                lines.Add("拦下：" + blocker);
            }

            foreach (var question in plan.Questions)
            {
                lines.Add("要问：" + question);
            }

            if (plan.Questions.Count > 0)
            {
                // 有要问的就停在这儿：一轮最多两条，回话里就这两条。
                return CommandResult.Failure(
                    $"还差 {plan.Questions.Count} 条推不出来，先问人", lines);
            }

            if (plan.Blockers.Count > 0)
            {
                return CommandResult.Failure("这次提交过不去", lines);
            }

            var unityRoot = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                string.IsNullOrWhiteSpace(arguments.UnityProjectRoot) ? "UnityProject" : arguments.UnityProjectRoot));
            var destinationDirectory = Path.GetFullPath(Path.Combine(unityRoot, plan.DestinationDirectory));
            var destinationPath = Path.Combine(destinationDirectory, plan.FileName);

            lines.Add($"类型：{plan.AssetType}");
            lines.Add($"落点：{plan.DestinationPath}");
            lines.Add($"命名模式：{plan.NamingPattern}");

            if (File.Exists(destinationPath))
            {
                // 覆盖已有资产是**毁东西**那一档，不许静默做。
                return CommandResult.Failure(
                    $"那个落点已经有一份了：{plan.DestinationPath}。换个命名，或者先确认要不要替换它", lines);
            }

            if (!arguments.Confirmed)
            {
                lines.Add("");
                lines.Add("这一趟没有写任何文件。确认无误再跑一次并带 --Confirmed true。");
                return CommandResult.Success($"算好了：{plan.AssetType} → {plan.DestinationPath}（等确认）", lines);
            }

            try
            {
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure("落盘失败：" + exception.Message, lines);
            }

            lines.Add("已落盘：" + plan.DestinationPath);
            lines.Add("");
            lines.Add("**.meta 还没有**：Unity 下次打开工程时才会生成它。"
                + "在那之前 gate.meta 会把这一份报成缺 meta——那不是提交错了，是还没让 Unity 见过它。");
            lines.Add("接着跑：gate.assetspec、gate.naming、gate.meta；没过的那几道会逐条说怎么改。");

            return CommandResult.Success($"落好了：{plan.DestinationPath}", lines);
        }
    }
}
