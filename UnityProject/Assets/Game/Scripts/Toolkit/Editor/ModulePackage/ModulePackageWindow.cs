using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 模块管理面板：把 <c>Packages/</c> 下的框架模块包连同它们在 manifest.json 里的装卸状态列出来，
    /// 一处安装、卸载、装回全部。
    /// 卸载只摘清单条目，包目录一直留在盘上——所以「卸载了不知道从哪里装回来」在这里不成立，
    /// 卸掉的包会排到列表最上面等着被装回去。
    /// 真把包目录整个删掉的是 <c>feature.remove</c>，那条命令的产物只能用 git 还原，
    /// 面板对这种情况只能显示「缺目录」并允许摘掉悬空条目。
    /// </summary>
    public sealed class ModulePackageWindow : EditorWindow
    {
        private const string MenuPath = "工具链/模块管理";
        private const string WindowTitle = "模块管理";

        private static readonly string[] FilterLabels = { "全部", "已安装", "未安装" };

        [SerializeField]
        private string searchText = string.Empty;

        [SerializeField]
        private int filterIndex;

        [SerializeField]
        private string statusMessage = string.Empty;

        [SerializeField]
        private bool statusIsError;

        [SerializeField]
        private Vector2 scrollPosition;

        private string templateRoot;
        private List<ModulePackageInfo> packages = new List<ModulePackageInfo>();
        private HashSet<string> installedKeys = new HashSet<string>(StringComparer.Ordinal);
        private GUIStyle wrappedLabelStyle;

        /// <summary>菜单入口。</summary>
        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<ModulePackageWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(540f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        // 有人直接手改了清单、跑了 feature.remove、或者切了分支——切回这个窗口就该看见新状态，
        // 而不是照着一份过期的列表按按钮。
        private void OnFocus()
        {
            Refresh();
        }

        private void OnGUI()
        {
            if (wrappedLabelStyle == null)
            {
                wrappedLabelStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            }

            DrawHeader();

            if (string.IsNullOrEmpty(templateRoot))
            {
                EditorGUILayout.HelpBox(
                    "找不到模板根目录（带 Tools/Gates/Config/gate-config.json 的那一级），列不出模块包。",
                    MessageType.Error);
                return;
            }

            DrawFilters();
            DrawList();
            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            var installedCount = packages.Count(package => package.IsInstalled);
            EditorGUILayout.LabelField(
                $"共 {packages.Count} 个模块包 · 已安装 {installedCount} · 未安装 {packages.Count - installedCount}",
                EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            {
                Refresh();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                $"清单：{ModulePackageCatalog.ManifestRelativePath}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusIsError ? MessageType.Error : MessageType.Info);
            }
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginHorizontal();
            filterIndex = GUILayout.Toolbar(filterIndex, FilterLabels, GUILayout.Width(210f));
            GUILayout.Space(8f);
            searchText = EditorGUILayout.TextField(searchText ?? string.Empty);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            var visible = packages.Where(IsVisible).ToList();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox("这个筛选下没有模块包。", MessageType.Info);
            }

            foreach (var package in visible)
            {
                DrawPackage(package);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPackage(ModulePackageInfo package)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DescribeState(package), EditorStyles.miniLabel, GUILayout.Width(52f));
            EditorGUILayout.LabelField(package.DisplayName, EditorStyles.boldLabel, GUILayout.Width(170f));
            EditorGUILayout.LabelField(package.PackageName, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(package.Version))
            {
                EditorGUILayout.LabelField(package.Version, EditorStyles.miniLabel, GUILayout.Width(44f));
            }

            DrawActionButton(package);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(package.Description))
            {
                EditorGUILayout.LabelField(package.Description, wrappedLabelStyle);
            }

            if (!package.IsOnDisk)
            {
                EditorGUILayout.HelpBox(
                    $"清单里写着它，但 {ModulePackageCatalog.PackagesDirectoryName}/ 下找不到包目录。"
                    + "摘掉这条悬空条目，或者用 git 把包目录还原回来。",
                    MessageType.Warning);
            }
            else
            {
                DrawDependencyNote(package);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionButton(ModulePackageInfo package)
        {
            if (!package.IsOnDisk)
            {
                if (GUILayout.Button("摘掉条目", GUILayout.Width(76f)))
                {
                    Uninstall(package);
                }

                return;
            }

            if (package.IsInstalled)
            {
                if (GUILayout.Button("卸载", GUILayout.Width(76f)))
                {
                    Uninstall(package);
                }

                return;
            }

            if (GUILayout.Button("安装", GUILayout.Width(76f)))
            {
                Install(package);
            }
        }

        private void DrawDependencyNote(ModulePackageInfo package)
        {
            if (package.Dependencies == null || package.Dependencies.Count == 0)
            {
                return;
            }

            var descriptions = package.Dependencies.Select(dependency =>
                installedKeys.Contains(dependency.PackageName)
                    ? dependency.PackageName
                    : dependency.PackageName + "（清单里没有）");

            EditorGUILayout.LabelField("依赖：" + string.Join("、", descriptions), EditorStyles.miniLabel);
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var missing = packages.Where(package => package.IsOnDisk && !package.IsInstalled).ToList();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(missing.Count == 0))
            {
                if (GUILayout.Button($"把未安装的 {missing.Count} 个全装回来", GUILayout.Height(24f)))
                {
                    InstallAll(missing);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "安装与卸载只改清单里的一行；包目录一直留在盘上，随时能装回来。"
                + "包目录本身被删掉（feature.remove 干的）只能用 git 还原。",
                EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private bool IsVisible(ModulePackageInfo package)
        {
            if (filterIndex == 1 && !package.IsInstalled)
            {
                return false;
            }

            if (filterIndex == 2 && package.IsInstalled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return Contains(package.PackageName, searchText)
                   || Contains(package.DisplayName, searchText)
                   || Contains(package.Description, searchText);
        }

        private static bool Contains(string text, string keyword)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeState(ModulePackageInfo package)
        {
            if (!package.IsOnDisk)
            {
                return "缺目录";
            }

            return package.IsInstalled ? "已安装" : "未安装";
        }

        private void Refresh()
        {
            templateRoot = TemplateRootLocator.Find();
            if (string.IsNullOrEmpty(templateRoot))
            {
                packages = new List<ModulePackageInfo>();
                installedKeys = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            packages = ModulePackageCatalog.Scan(templateRoot).ToList();
            installedKeys = new HashSet<string>(
                ModulePackageManifest.ReadDependencies(GetManifestPath()).Keys, StringComparer.Ordinal);
        }

        private string GetManifestPath()
        {
            return Path.Combine(
                templateRoot,
                ModulePackageCatalog.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void Install(ModulePackageInfo package)
        {
            var notes = new List<string>();
            if (TryInstall(package, notes))
            {
                ApplyChange(notes);
            }
        }

        private void InstallAll(IReadOnlyList<ModulePackageInfo> targets)
        {
            var notes = new List<string>();
            foreach (var package in targets)
            {
                if (!TryInstall(package, notes))
                {
                    return;
                }
            }

            ApplyChange(notes);
        }

        /// <summary>装一个包，连它声明的依赖一起补进清单；有一处写不进去就整个停下并把原因报出来。</summary>
        private bool TryInstall(ModulePackageInfo package, List<string> notes)
        {
            var manifestPath = GetManifestPath();
            var pending = new List<ModulePackageDependency>
            {
                new ModulePackageDependency
                {
                    PackageName = package.PackageName,
                    VersionExpression = package.InstallExpression,
                },
            };
            var handled = new HashSet<string>(StringComparer.Ordinal);

            while (pending.Count > 0)
            {
                var current = pending[0];
                pending.RemoveAt(0);

                if (!handled.Add(current.PackageName) || installedKeys.Contains(current.PackageName))
                {
                    continue;
                }

                // 依赖也是本地包时，版本表达式以盘上那份为准：package.json 里写的版本号
                // 对 file: 包没有意义，写进去 Unity 会去注册表找一个根本不存在的包。
                var local = packages.FirstOrDefault(candidate =>
                    candidate.IsOnDisk && string.Equals(candidate.PackageName, current.PackageName, StringComparison.Ordinal));
                var expression = local != null ? local.InstallExpression : current.VersionExpression;

                if (string.IsNullOrEmpty(expression))
                {
                    notes.Add($"跳过 {current.PackageName}：不知道该写哪个版本，请自己补进清单");
                    continue;
                }

                if (!ModulePackageManifest.TryAddDependency(manifestPath, current.PackageName, expression, out var message))
                {
                    Report(message, true);
                    return false;
                }

                notes.Add(message);
                installedKeys.Add(current.PackageName);

                if (local?.Dependencies != null)
                {
                    pending.AddRange(local.Dependencies);
                }
            }

            return true;
        }

        private void Uninstall(ModulePackageInfo package)
        {
            var dependents = packages.Where(candidate =>
                    candidate.IsInstalled
                    && !string.Equals(candidate.PackageName, package.PackageName, StringComparison.Ordinal)
                    && candidate.Dependencies != null
                    && candidate.Dependencies.Any(dependency =>
                        string.Equals(dependency.PackageName, package.PackageName, StringComparison.Ordinal)))
                .ToList();

            if (dependents.Count > 0)
            {
                var names = string.Join(
                    "\n", dependents.Select(item => $"· {item.DisplayName}（{item.PackageName}）"));
                var goAhead = EditorUtility.DisplayDialog(
                    WindowTitle,
                    $"这些已安装的模块声明了对 {package.PackageName} 的依赖：\n\n{names}\n\n"
                    + "摘掉之后它们会解析失败。仍然继续？",
                    "仍然卸载",
                    "算了");
                if (!goAhead)
                {
                    return;
                }
            }

            if (!ModulePackageManifest.TryRemoveDependency(GetManifestPath(), package.PackageName, out var message))
            {
                Report(message, true);
                return;
            }

            var notes = new List<string> { message };
            notes.AddRange(DescribeLeftovers(package));
            ApplyChange(notes);
        }

        /// <summary>
        /// 报出这次卸载之后没人再要、但仍留在清单里的第三方依赖。
        /// 只报不删：它们可能是别处手工加的，替人做主删掉别人的依赖太过分。
        /// </summary>
        private IEnumerable<string> DescribeLeftovers(ModulePackageInfo removed)
        {
            if (removed.Dependencies == null)
            {
                yield break;
            }

            foreach (var dependency in removed.Dependencies)
            {
                if (!installedKeys.Contains(dependency.PackageName))
                {
                    continue;
                }

                var isLocalPackage = packages.Any(candidate =>
                    candidate.IsOnDisk
                    && string.Equals(candidate.PackageName, dependency.PackageName, StringComparison.Ordinal));
                if (isLocalPackage)
                {
                    continue;
                }

                var stillNeeded = packages.Any(candidate =>
                    candidate.IsInstalled
                    && !string.Equals(candidate.PackageName, removed.PackageName, StringComparison.Ordinal)
                    && candidate.Dependencies != null
                    && candidate.Dependencies.Any(item =>
                        string.Equals(item.PackageName, dependency.PackageName, StringComparison.Ordinal)));
                if (!stillNeeded)
                {
                    yield return $"知会：{dependency.PackageName} 现在没人再依赖了，仍留在清单里，确认不用可以自己摘掉";
                }
            }
        }

        private void ApplyChange(IReadOnlyList<string> notes)
        {
            Refresh();
            AssetDatabase.Refresh();

            try
            {
                Client.Resolve();
            }
            catch (Exception exception)
            {
                notes = notes.Concat(new[]
                {
                    $"知会：让包管理器重新解析时抛了 {exception.GetType().Name}，"
                    + "清单已经改好了，重开一次工程即可生效",
                }).ToList();
            }

            Report(notes.Count > 0 ? string.Join("；", notes) : "没有需要改的地方", false);
        }

        private void Report(string message, bool isError)
        {
            statusMessage = message;
            statusIsError = isError;
            Repaint();
        }
    }
}
