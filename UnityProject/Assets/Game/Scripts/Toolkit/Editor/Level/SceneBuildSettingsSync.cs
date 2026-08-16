using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>把场景目录下的场景同步进构建设置。出包与 PlayMode 都只认构建设置里的场景，漏登记的场景在打出来的包里根本进不去。</summary>
    public static class SceneBuildSettingsSync
    {
        private const string SyncMenuPath = "工具链/关卡/同步场景到构建设置";

        /// <summary>场景目录，相对 Unity 工程写。下面按 Boot（随包入口）与 World（热更玩法）分区。</summary>
        public const string SceneDirectory = "Assets/Game/Scenes";

        /// <summary>菜单入口：同步并把结论打出来。</summary>
        [MenuItem(SyncMenuPath)]
        public static void SyncFromMenu()
        {
            Debug.Log(Sync());
        }

        /// <summary>
        /// 把场景目录下的全部场景同步进构建设置，返回一行中文摘要。
        /// 只追加与更新，不动构建设置里本来就有的其他场景——那些可能是别的模块登记的。
        /// </summary>
        public static string Sync()
        {
            var scenePaths = FindScenePaths();
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            var addedCount = 0;
            foreach (var scenePath in scenePaths)
            {
                var index = buildScenes.FindIndex(entry => entry.path == scenePath);
                if (index >= 0)
                {
                    if (!buildScenes[index].enabled)
                    {
                        buildScenes[index] = new EditorBuildSettingsScene(scenePath, true);
                    }

                    continue;
                }

                buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
                addedCount++;
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();

            return $"场景已同步进构建设置：目录 {SceneDirectory} 下 {scenePaths.Count} 个场景，" +
                   $"新登记 {addedCount} 个，构建设置里现共 {buildScenes.Count} 个";
        }

        /// <summary>列出场景目录下的全部场景路径，按序数序排列。</summary>
        public static IReadOnlyList<string> FindScenePaths()
        {
            var absoluteDirectory = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                SceneDirectory.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(absoluteDirectory))
            {
                return new List<string>();
            }

            // 拼回相对子路径而不是只取文件名：场景分区之后，Boot/ 与 World/ 下的场景
            // 只取文件名会拼出 Assets/Game/Scenes/村庄.unity 这种不存在的路径，构建设置里就成了死条目。
            return Directory.GetFiles(absoluteDirectory, "*.unity", SearchOption.AllDirectories)
                .Select(path => SceneDirectory + "/"
                    + Path.GetRelativePath(absoluteDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToList();
        }
    }
}
