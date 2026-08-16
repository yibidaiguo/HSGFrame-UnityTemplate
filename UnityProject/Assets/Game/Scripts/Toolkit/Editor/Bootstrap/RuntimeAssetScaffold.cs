using System.Collections.Generic;
using System.IO;
using System.Text;
using Template.Boot;
using Template.Level.View;
using Template.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 运行时资产脚手架：把「按 Play 能跑起来」所需的那几件资产一次性落盘。
    /// </summary>
    /// <remarks>
    /// 落的是四样：关卡实体预制体的可视体、UI 的 PanelSettings 与主题、
    /// 实体类别到资源地址的映射资产、以及启动场景。
    /// 这些都是 AI 按铁律 2 必须经命令层落的资产——手写 YAML 既不可靠也不可审。
    /// 幂等：每一件都是「有就更新、没有就建」，重复跑不会产生第二份。
    /// </remarks>
    public static class RuntimeAssetScaffold
    {
        private const string UiSettingsDirectory = "Assets/Game/Settings/Ui";
        private const string LevelSettingsDirectory = "Assets/Game/Settings/Level";
        private const string InputSettingsDirectory = "Assets/Game/Settings/Input";

        // 与 IA_默认输入.inputactions 里那张 map 同名；改名要两边一起改。
        private const string DefaultActionMapName = "游戏";
        private const string BootSceneDirectory = "Assets/Game/Scenes/Boot";
        private const string EntityPrefabDirectory = "Assets/Game/ResourceArt/Level";

        // 材质进 Art/ 而不是跟预制体同夹：Art 是「被引用的源生资产」、ResourceArt 是「按 key 加载的成品」，
        // 这条分界是《结构规范-资源》第二节的硬规矩，而且 ResourceArt/Level 的导入规则只放行 .prefab。
        private const string EntityMaterialDirectory = "Assets/Game/Art/Material/Level";

        private const string ThemeAssetPath = UiSettingsDirectory + "/默认主题.tss";
        private const string PanelSettingsAssetPath = UiSettingsDirectory + "/主面板设置.asset";
        private const string ResourceMapAssetPath = LevelSettingsDirectory + "/实体资源映射.asset";
        private const string InputActionsAssetPath = InputSettingsDirectory + "/IA_默认输入.inputactions";
        private const string BootScenePath = BootSceneDirectory + "/启动.unity";

        // 六个类别取自 Levels/村庄/区块_*.json 里「类别」字段的实际取值。地址就是预制体文件名
        //（收集器用 AddressByFileName），所以这张表同时也是「有哪些预制体」的清单。
        private static readonly (string EntityKind, string ResourceAddress, PrimitiveType Shape, Color Color)[] EntityKinds =
        {
            ("NPC", "P_Npc", PrimitiveType.Capsule, new Color(0.30f, 0.65f, 0.95f)),
            ("可交互物", "P_可交互物", PrimitiveType.Cube, new Color(0.95f, 0.78f, 0.30f)),
            ("传送点", "P_传送点", PrimitiveType.Cylinder, new Color(0.55f, 0.40f, 0.95f)),
            ("刷怪点", "P_刷怪点", PrimitiveType.Sphere, new Color(0.90f, 0.35f, 0.35f)),
            ("触发器", "P_触发器", PrimitiveType.Cube, new Color(0.40f, 0.90f, 0.55f)),
            ("任务物件", "P_任务物件", PrimitiveType.Sphere, new Color(0.95f, 0.55f, 0.80f)),
        };

        /// <summary>把全部运行时资产落一遍，返回逐行中文摘要。</summary>
        public static IReadOnlyList<string> ScaffoldAll()
        {
            var report = new List<string>();

            report.Add(ScaffoldEntityPrefabs());
            report.Add(ScaffoldUiTheme());
            report.Add(ScaffoldPanelSettings());
            report.Add(ScaffoldResourceMap());
            report.Add(ScaffoldInputActions());

            // 启动场景排在最后：它要按路径把前面几件资产引用进去，前面没落盘就只能引到 null。
            report.Add(ScaffoldBootScene());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Add(SceneBuildSettingsSync.Sync());
            return report;
        }

        /// <summary>给六个关卡实体预制体补上可视体（基元网格 + 内置管线材质）。</summary>
        public static string ScaffoldEntityPrefabs()
        {
            var updatedCount = 0;
            foreach (var kind in EntityKinds)
            {
                var prefabPath = $"{EntityPrefabDirectory}/{kind.ResourceAddress}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    if (EnsureVisualBody(root, kind.Shape, kind.Color, kind.ResourceAddress))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        updatedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return $"关卡实体预制体：{EntityKinds.Length} 个里补了可视体的 {updatedCount} 个";
        }

        /// <summary>建 UI 的默认运行时主题（.tss），已存在则原样保留。</summary>
        public static string ScaffoldUiTheme()
        {
            EnsureDirectory(UiSettingsDirectory);
            EnsureImportRule(UiSettingsDirectory, "工程配置-界面", new[] { ".asset", ".tss", ".uss", ".uxml" });

            var absolutePath = ToAbsolutePath(ThemeAssetPath);
            if (File.Exists(absolutePath))
            {
                return $"UI 主题：{ThemeAssetPath} 已存在，保持原样";
            }

            // Unity 自己生成的 UnityDefaultRuntimeTheme.tss 内容就是这一行 import。
            // 自己写一份而不是去引用包里那份：包路径随 Unity 版本变，写死了迟早断。
            File.WriteAllText(absolutePath, "@import url(\"unity-theme://default\");\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ThemeAssetPath);
            return $"UI 主题：已建 {ThemeAssetPath}";
        }

        /// <summary>建 UIDocument 用的 PanelSettings，并把主题挂上去。</summary>
        public static string ScaffoldPanelSettings()
        {
            EnsureDirectory(UiSettingsDirectory);

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath);
            var isNew = settings == null;
            if (isNew)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsAssetPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeAssetPath);
            if (theme == null)
            {
                return $"位置：{PanelSettingsAssetPath}；原因：主题 {ThemeAssetPath} 还没导入成 ThemeStyleSheet；修复：重跑一次本命令；参考：Assets/Game/Settings/Ui/";
            }

            settings.themeStyleSheet = theme;

            // 按高度缩放 + 1080p 参考分辨率：模板不知道宿主项目做什么品类，
            // 取一个「大多数 2D/3D 项目改一个数就能用」的起点，而不是留 Constant Pixel Size 那种一换分辨率就崩的默认值。
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 1f;

            EditorUtility.SetDirty(settings);

            // 中文动词先落到局部变量再进插值：命名检查器把插值洞里的内容当标识符读，
            // 直接写 {(isNew ? "已建" : "已更新")} 会被判成「标识符含中文」。
            var actionText = isNew ? "已建" : "已更新";
            return $"UI 面板设置：{actionText} {PanelSettingsAssetPath}";
        }

        /// <summary>建实体类别到资源地址的映射资产，按六个类别写满。</summary>
        public static string ScaffoldResourceMap()
        {
            EnsureDirectory(LevelSettingsDirectory);
            EnsureImportRule(LevelSettingsDirectory, "工程配置-关卡", new[] { ".asset" });

            var asset = AssetDatabase.LoadAssetAtPath<LevelEntityResourceMapAsset>(ResourceMapAssetPath);
            var isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<LevelEntityResourceMapAsset>();
                AssetDatabase.CreateAsset(asset, ResourceMapAssetPath);
            }

            var entries = new List<LevelEntityResourceEntry>();
            foreach (var kind in EntityKinds)
            {
                entries.Add(new LevelEntityResourceEntry
                {
                    EntityKind = kind.EntityKind,
                    ResourceAddress = kind.ResourceAddress,
                });
            }

            var serialized = new SerializedObject(asset);
            var entriesProperty = serialized.FindProperty("_entries");
            entriesProperty.arraySize = entries.Count;
            for (var index = 0; index < entries.Count; index++)
            {
                var element = entriesProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("_entityKind").stringValue = entries[index].EntityKind;
                element.FindPropertyRelative("_resourceAddress").stringValue = entries[index].ResourceAddress;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            var actionText = isNew ? "已建" : "已更新";
            return $"实体资源映射：{actionText} {ResourceMapAssetPath}，共 {entries.Count} 条";
        }

        /// <summary>确认工程级输入动作资产就位。</summary>
        /// <remarks>
        /// 这一步**不再从代码生成绑定**：工程走新版 Input System 之后，绑定的唯一事实源是
        /// 随仓库提交的 <c>IA_默认输入.inputactions</c>，由 Unity 的 Input Actions 编辑器维护。
        /// 再让脚手架按代码里的一张默认表生成一次，就是把事实源变回两个。
        /// 所以这里只保证目录与导入规则在，并如实报告资产在不在。
        /// </remarks>
        public static string ScaffoldInputActions()
        {
            EnsureDirectory(InputSettingsDirectory);
            EnsureImportRule(InputSettingsDirectory, "工程配置-输入", new[] { ".json", ".inputactions" });

            if (!File.Exists(ToAbsolutePath(InputActionsAssetPath)))
            {
                return $"位置：{InputActionsAssetPath}；原因：输入动作资产不在，输入驱动会拿不到绑定；修复：从版本库恢复它，或在 Project 窗口右键 Create → Input Actions 重建后改成这个名字；参考：《结构规范-资源》第五节";
            }

            return $"输入动作：{InputActionsAssetPath} 就位";
        }

        /// <summary>建启动场景：装配入口 + 常驻根（相机、灯光、UI 根、输入驱动、关卡装配器）。</summary>
        public static string ScaffoldBootScene()
        {
            EnsureDirectory(BootSceneDirectory);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrapObject = new GameObject("启动装配");
            SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
            bootstrapObject.AddComponent<GameBootstrap>();

            var persistentRoot = new GameObject("常驻根");
            SceneManager.MoveGameObjectToScene(persistentRoot, scene);
            persistentRoot.AddComponent<PersistentRootBehaviour>();

            var cameraObject = new GameObject("主相机");
            cameraObject.transform.SetParent(persistentRoot.transform, worldPositionStays: false);
            cameraObject.transform.localPosition = new Vector3(0f, 12f, -14f);
            cameraObject.transform.localEulerAngles = new Vector3(35f, 0f, 0f);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("主光源");
            lightObject.transform.SetParent(persistentRoot.transform, worldPositionStays: false);
            lightObject.transform.localEulerAngles = new Vector3(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            var uiRootObject = new GameObject("UI根");
            uiRootObject.transform.SetParent(persistentRoot.transform, worldPositionStays: false);
            var document = uiRootObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsAssetPath);
            uiRootObject.AddComponent<UiRootBehaviour>();

            var inputObject = new GameObject("输入驱动");
            inputObject.transform.SetParent(persistentRoot.transform, worldPositionStays: false);
            var inputDriver = inputObject.AddComponent<InputDriverBehaviour>();
            var inputSerialized = new SerializedObject(inputDriver);
            inputSerialized.FindProperty("_actionAsset").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsAssetPath);
            inputSerialized.FindProperty("_actionMapName").stringValue = DefaultActionMapName;
            inputSerialized.ApplyModifiedPropertiesWithoutUndo();

            var spawnerObject = new GameObject("关卡实体装配器");
            spawnerObject.transform.SetParent(persistentRoot.transform, worldPositionStays: false);
            var spawner = spawnerObject.AddComponent<LevelEntitySpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("_resourceMapAsset").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<LevelEntityResourceMapAsset>(ResourceMapAssetPath);
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, BootScenePath))
            {
                return $"位置：{BootScenePath}；原因：启动场景保存失败；修复：确认目录可写后重跑本命令；参考：Assets/Game/Scenes/Boot/";
            }

            return $"启动场景：已建 {BootScenePath}（装配入口 + 常驻根 4 件）";
        }

        // 可视体是「有就不动、没有就补」：预制体一旦被美术接手替换成真模型，
        // 重跑脚手架不该把人家的成果盖掉。判据是「根下有没有带 MeshRenderer 的子物体」。
        private static bool EnsureVisualBody(GameObject root, PrimitiveType shape, Color color, string prefabName)
        {
            if (root.GetComponentInChildren<MeshRenderer>(includeInactive: true) != null)
            {
                return false;
            }

            var primitive = GameObject.CreatePrimitive(shape);
            primitive.name = "可视体";
            primitive.transform.SetParent(root.transform, worldPositionStays: false);

            // 碰撞体不留：这批可视体只是「看得见」，碰撞按玩法各自加，
            // 留着基元自带的 Collider 会让触发器类实体莫名其妙挡住角色。
            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            EnsureDirectory(EntityMaterialDirectory);

            // 去掉预制体的 P_ 前缀再套 Mat_：材质的前缀是 Mat_（《结构规范-资源》第五节的前缀表），
            // 叠成 Mat_P_Npc 会同时带两个类型前缀，命名规范上是错的。
            var materialName = prefabName.StartsWith("P_", System.StringComparison.Ordinal)
                ? prefabName.Substring(2)
                : prefabName;
            var materialPath = $"{EntityMaterialDirectory}/Mat_{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                // 内置管线的 Standard：模板不绑定 SRP（见《结构规范-资源》第五节的管线说明），
                // 宿主项目上 URP 那天连这批材质一起换。
                material = new Material(Shader.Find("Standard")) { color = color };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            return true;
        }

        private static void EnsureDirectory(string assetDirectory)
        {
            Directory.CreateDirectory(ToAbsolutePath(assetDirectory));
        }

        // 每个正式资产目录都要被一份导入规则覆盖，否则 R5 会红（《结构规范-资源》第六节）。
        private static void EnsureImportRule(string assetDirectory, string purpose, IReadOnlyList<string> extensions)
        {
            var rulePath = ToAbsolutePath(assetDirectory + "/导入规则.json");
            if (File.Exists(rulePath))
            {
                return;
            }

            var quotedExtensions = new List<string>();
            foreach (var extension in extensions)
            {
                quotedExtensions.Add($"\"{extension}\"");
            }

            var json = "{\n" +
                       $"  \"目录用途\": \"{purpose}\",\n" +
                       "  \"文件名前缀\": \"\",\n" +
                       $"  \"允许扩展名\": [{string.Join(", ", quotedExtensions)}],\n" +
                       "  \"命名风格\": \"PascalCase\",\n" +
                       "  \"最大文件字节\": 4194304\n" +
                       "}\n";
            File.WriteAllText(rulePath, json, new UTF8Encoding(false));
        }

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
