using System.ComponentModel;
using System.IO;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>运行时资产脚手架命令的参数。</summary>
    public sealed class RuntimeScaffoldArguments
    {
        /// <summary>模板根目录。</summary>
        [Summary("模板根目录")]
        [DefaultValue(".")]
        public string TemplateRoot { get; set; }

        /// <summary>Unity 侧的超时分钟数。</summary>
        [Summary("Unity 侧的超时分钟数")]
        [DefaultValue(15)]
        public int TimeoutMinutes { get; set; }
    }

    /// <summary>
    /// 运行时资产脚手架命令：把「按 Play 能跑起来」所需的资产交给 Unity 一次性落盘。
    /// </summary>
    /// <remarks>
    /// 落的是关卡实体预制体的可视体、UI 的 PanelSettings 与主题、实体类别到资源地址的映射、启动场景。
    /// 铁律 2 要求 AI 经命令层落资产，这条命令就是这几件资产的那条路——手写 .prefab / .unity 的 YAML
    /// 既不可靠也不可审。命令是幂等的，重复跑不会多出第二份。
    /// </remarks>
    public static class RuntimeScaffoldCommand
    {
        private const string EntryMethod = "Template.Toolkit.Editor.RuntimeAssetScaffoldCommandLine.ScaffoldFromCommandLine";

        /// <summary>生成运行时资产。</summary>
        /// <param name="arguments">脚手架参数。</param>
        [EditorCommand("runtime.scaffold")]
        [Summary("生成运行时资产：实体可视体、UI 面板设置、实体资源映射、启动场景")]
        public static CommandResult Execute(RuntimeScaffoldArguments arguments)
        {
            var timeoutProblem = SceneCommandSupport.CheckTimeout(arguments.TimeoutMinutes);
            if (timeoutProblem != null)
            {
                return timeoutProblem;
            }

            var templateRoot = SceneCommandSupport.ResolveTemplateRoot(arguments.TemplateRoot);
            var assetsRoot = Path.Combine(templateRoot, "UnityProject", "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                return CommandResult.Failure(SceneCommandSupport.ComposeError(
                    assetsRoot,
                    "找不到 Unity 工程的 Assets 目录",
                    "确认 TemplateRoot 指向模板根（其下应有 UnityProject/Assets）",
                    "UnityProject/Assets"));
            }

            var argumentsFilePath = SceneCommandSupport.WriteArgumentsFile(
                templateRoot,
                "runtime-scaffold-arguments.json",
                arguments);

            return SceneCommandSupport.RunUnity(
                templateRoot,
                argumentsFilePath,
                EntryMethod,
                arguments.TimeoutMinutes,
                "运行时资产已交给 Unity 生成");
        }
    }
}
