using System.Collections.Generic;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.Scaffold;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>摘除可选功能命令的参数。</summary>
    public sealed class FeatureRemoveArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        public string TemplateRoot { get; set; }

        /// <summary>要摘掉的可选功能名，例如 hotfix。</summary>
        [Summary("要摘掉的可选功能名，例如 hotfix")]
        public string Name { get; set; }
    }

    /// <summary>摘除可选功能命令：把一个可选功能占的位置一次清干净。</summary>
    public static class FeatureRemoveCommand
    {
        // 命令宿主的 bin/ 里可能还留着上一轮编出来的可选工程 dll，那时命令清单还没变。
        // 不说这一句，人会以为摘除没生效。
        private const string RebuildNotice =
            "知会：命令宿主的 bin/ 里还留着上一轮编出来的可选工程 dll，重新 dotnet build 之后它带的命令才会从 list 里消失";

        /// <summary>
        /// 摘掉一个可选功能：删包与工程、摘 manifest 与解决方案条目、清门禁配置、摘文档标记段。
        /// </summary>
        /// <param name="arguments">摘除参数。</param>
        [EditorCommand("feature.remove")]
        [Summary("把一个可选功能整块摘掉：删包与工程、摘 manifest 与解决方案条目、清门禁配置、摘文档标记段")]
        public static CommandResult Execute(FeatureRemoveArguments arguments)
        {
            var result = FeatureRemover.Remove(arguments.TemplateRoot, arguments.Name);
            if (!result.IsSuccess)
            {
                return CommandResult.Failure(result.Message);
            }

            var lines = new List<string>(result.ChangedPaths) { RebuildNotice };
            return CommandResult.Success(result.Message, lines);
        }
    }
}
