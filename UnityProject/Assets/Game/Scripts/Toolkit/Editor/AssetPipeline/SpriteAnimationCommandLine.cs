using System;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// <c>anim.build</c> 的 batchmode 入口：把创作管线出的图集切成精灵并建出 AnimationClip。
    ///
    /// 这是「产物两步走」的**第二步**：第一步（anim.compose）在纯 dotnet 侧出图集与 sheet.json，
    /// 那一步不需要 Unity；第二步必须进 Unity，因为切精灵与建 clip 是编辑器 API 的事，
    /// 手写 .meta 与 .anim 是明令禁止的（铁律 2）。
    /// </summary>
    public static class SpriteAnimationCommandLine
    {
        private const string ArgumentsFileArgumentName = "-argumentsFile";

        /// <summary>batchmode 入口：从 -argumentsFile 指的 json 读参数。</summary>
        public static void BuildFromCommandLine()
        {
            var arguments = ReadArguments();
            var result = SpriteAnimationBuilder.Build(
                arguments.SheetSourcePath,
                arguments.MetadataSourcePath,
                arguments.SheetAssetPath,
                arguments.ClipAssetPath,
                arguments.Loop,
                arguments.TargetComponent);

            foreach (var note in result.Notes)
            {
                Debug.Log("[anim.build] " + note);
            }

            if (!result.Succeeded)
            {
                // **非零退出码**：batchmode 里 Debug.LogError 不会让 Unity 失败退出，
                // 于是一条失败的构建在 CI 上长得跟成功一模一样。
                Debug.LogError("[anim.build] 失败：" + result.FailureReason);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[anim.build] 好了：图集 {result.SheetAssetPath}，"
                + $"clip {(result.ClipAssetPath.Length == 0 ? "（没建）" : result.ClipAssetPath)}，"
                + $"{result.FrameCount} 帧");
            EditorApplication.Exit(0);
        }

        /// <summary>读参数文件。</summary>
        private static BuildArguments ReadArguments()
        {
            var argumentsFile = ReadArgument(ArgumentsFileArgumentName);
            if (string.IsNullOrEmpty(argumentsFile))
            {
                throw new InvalidOperationException($"缺少 {ArgumentsFileArgumentName} 参数，无法读取切图与建 clip 的参数");
            }

            var json = File.ReadAllText(argumentsFile, Encoding.UTF8);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<BuildArguments>(json, options) ?? new BuildArguments();
        }

        /// <summary>从命令行里取一个具名参数的值。</summary>
        private static string ReadArgument(string name)
        {
            var all = Environment.GetCommandLineArgs();
            for (var index = 0; index < all.Length - 1; index++)
            {
                if (string.Equals(all[index], name, StringComparison.Ordinal))
                {
                    return all[index + 1];
                }
            }

            return null;
        }

        /// <summary>切图与建 clip 的参数。</summary>
        [Serializable]
        public sealed class BuildArguments
        {
            /// <summary>图集 PNG 的来源路径（工程外，创作管线的产出）。</summary>
            public string SheetSourcePath { get; set; } = "";

            /// <summary>sheet.json 的来源路径；空串时取图集旁边那份。</summary>
            public string MetadataSourcePath { get; set; } = "";

            /// <summary>图集要落在工程里的哪（Assets/… 开头）。</summary>
            public string SheetAssetPath { get; set; } = "";

            /// <summary>clip 要落在工程里的哪；空串表示只切图不建 clip。</summary>
            public string ClipAssetPath { get; set; } = "";

            /// <summary>clip 要不要循环。</summary>
            public bool Loop { get; set; } = true;

            /// <summary>clip 驱动谁的 Sprite：SpriteRenderer 或 Image。</summary>
            public string TargetComponent { get; set; } = "SpriteRenderer";
        }
    }
}
