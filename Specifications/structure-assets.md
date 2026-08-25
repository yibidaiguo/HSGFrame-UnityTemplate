# 结构规范 · 资源

> 前置：《结构规范-总纲》。本份只讲 `UnityProject/Assets/` 下的资产怎么摆、怎么分组、怎么查。

## 一、目录树（目标态）

```
Assets/
  _Inbox/                     中转收件箱：外来资产先进这，asset.import 归位（保持现状机制）
  Game/                       业务正式区
    Art/                      源生美术：只被引用，不做加载入口
      Texture/ Model/ Material/ Animation/ Shader/ Audio/ Font/ Vfx/
                 每棵树下都是 <门类>/<模块>/，门类词表见 asset-layout.baseline.json
    ResourceArt/              成品资源单元：预制体等，代码按 key 加载的东西全在这
      Character/ Ui/ Level/ Effect/ Item/ Shared/   （功能层，按需增，增即配导入规则）
    Scenes/
      Boot/                   随包入口场景（静态）
      World/                  热更玩法场景（动态）
    Settings/                 工程配置资产：YooAsset 收集配置、InputActions、物理材质、
                              图形/质量设置、渲染管线资产（引入 SRP 时）、图集
    Scripts/  Tests/          见《结构规范-代码》
  Plugins/ StreamingAssets/ DialogueContent/ NodeEditorContent/ NodeEditorSettings/
                              第三方与生成物自留地：各自工具管理，业务资产禁入，业务也不改它们
```

### 层级公式：`Art/<类型>/<门类>/<模块>/<文件>`，三层目录一层都不许省

例：`Art/Texture/Ui/Inventory/T_背包格子.png`、`Art/Model/Vegetation/Grass/M_Grass01.fbx`、
`Art/Audio/Sound/Combat/A_SwordHit_01.wav`。

**「模块少时先平铺、涨了再开夹」这一档已经取消。** 那句话在上一版规范里存在过，
代价是真实工程跑几个月之后：`Art/Model/` 两百多个文件平铺在根上，
`Art/Material/` 只分出 `Character` 与 `Level` 两夹、其余全在根，`Art/Audio/` 一个夹都没有。
没人偷懒——是规则给了「以后再开」这个选项，而「以后」不会自己到。
**一个文件的目录不丑，两百个文件的根目录才丑。**

三层各是什么：

| 层 | 是什么 | 谁定 |
|---|---|---|
| `<类型>` | Texture / Model / Material / Animation / Shader / Audio / Font / Vfx | 固定八类 |
| `<门类>` | 这一类里的大分格（用途或主题） | [`asset-layout.baseline.json`](Baseline/asset-layout.baseline.json) 的词表 |
| `<模块>` | 这批资产属于谁：玩法模块名（`Inventory`）或具体主题（`Grass`、`StoneWall`） | 就近起名，PascalCase |

**模型、材质、贴图、动画四棵树共用一套门类词表**（`Character` `Vegetation` `Rock` `Terrain`
`Architecture` `Prop` `Weapon` …）。所以同一个东西的四种资产走同一条路径——
`Art/Model/Vegetation/Grass/M_Grass01.fbx` 对着 `Art/Material/Vegetation/Grass/Mat_Grass01.mat`，
把路径里的类型换掉就行，不用重新想它归哪类。

`Shader` 与 `Audio` **不用主题门类**，各有自己一套：shader 按它干什么分
（`Surface`/`Effect`/`Ui`/`PostProcess`），音频按用途分（`Music`/`Ambience`/`Sound`/`Voice`/`Ui`）——
音频这一层决定的是打包与本地化策略，语音要跟着语言分包，音乐要流式加载。

**每棵树只收自己那几种扩展名。** `Animation/` 下只许有 `.anim`/`.controller`/`.overrideController`/`.mask`；
带动画的模型属于 `Model/`，动画要从模型里提成 clip 再放进来。
（这一条是踩出来的：`Art/Animation/Character/` 里曾经躺着 `A_Idle.fbx`、`A_Walk.fbx`，
人点开只会看到一个模型。）

模块层禁用 `Misc`/`Other`/`Common`/`Temp`/`New` 这类名字——它们等于没分，
而且一旦出现就会变成新的垃圾堆。

**这一节由 `asset.layout` 门禁把关**（深度、门类、扩展名、模块层命名四项），
`gate-unity.ps1` 的资产门禁那一组里跑。词表是数据不是代码：宿主要加门类就往
`Specifications/Project/asset-layout.json` 里加，不改引擎。

`ResourceArt/` 同一条公式：`ResourceArt/<功能>/<模块>/P_史莱姆.prefab`。

## 二、Art 与 ResourceArt 的分界

| | `Art/` | `ResourceArt/` |
|---|---|---|
| 里面是什么 | 贴图、模型、动画、材质、Shader、音频、字体等源生资产 | 预制体、按 key 加载的数据资产（SO）等成品单元 |
| 谁用它 | 被 ResourceArt 的成品和场景**引用** | 被代码/配置按 key **加载** |
| YooAsset | 不是收集入口，作为依赖进包 | **收集入口**（连同 `Scenes/World/`） |

判定口诀：代码里写它的 key → ResourceArt；只在 Inspector 里被拖引用 → Art。
预制体一律住 ResourceArt（现状 `Art/Prefab/` 整夹迁走）。

## 三、静态 / 动态与加载分组（性能与热更的核心）

- **静态**：随包出、不经资源系统按 key 加载。就三处：`Scenes/Boot/`、`Settings/`、`StreamingAssets/`（首包生成物）。
- **动态**：YooAsset 收集、可热更。收集入口只有 `ResourceArt/` 与 `Scenes/World/`；
  `Art/` 永远作为依赖被拖进包，不直接收集——收集面收敛，热更差量才小。
- **加载分组写在既有的 `Tools/AssetPipeline/Config/bundle-group-rules.json` 的分组条目上**（新增「加载分组」字段），
  不另开文件——分组本来就按路径前缀定义，包边界与生命周期必须重合。取值三选一：
  - `常驻`：启动加载一次、全程不卸（UI 通用件、全局音效）。常驻组有总字节预算（R6）。
  - `按需`：随模块生命周期加载与释放（角色、道具、界面）。
  - `随场景`：跟 `Scenes/World/` 的场景走，场景卸载即释放。
- 两份配置各管一层，不重叠：`bundle-group-rules.json` 管**目录级**（分组名、路径前缀、是否共享组、加载分组），
  各目录 `import-rules.json` 管**文件级**（前缀、扩展名、大小、命名风格、图集）。
- **同一个包不许跨生命周期**：一个分组的加载分组只有一个值——一半常驻一半按需正是分包要避免的。
  现成的 `asset.bundlegroups` 已在查「共享资产未落共享组」，加载分组一致性与收集器对账由 R5 续上。
- UI 贴图按 `Art/Texture/Ui/<模块>/` 建同名图集（`SA_` 前缀，图集资产放 `Settings/Atlas/`），
  一个模块一图集、常驻通用件一图集，别把按需内容混进常驻图集。

## 四、通用资源（改一处、处处生效）

- 各类型/功能层下的 `Shared/` 放跨模块通用资产；判定门槛与代码同：被 ≥2 个模块引用就该在 Shared。
- 复用只走**引用**；预制体要定制差异用 **Prefab Variant** 指向 Shared 基体，禁止复制出第二份改。
- R4 重复内容检查：同哈希资产在仓库出现 ≥2 份即红。想「稍微改改」就从 Variant 或源文件分支，别复制成品。

## 五、文件名前缀表（导入规则的「文件名前缀」字段取值）

| 资产 | 前缀 | | 资产 | 前缀 |
|---|---|---|---|---|
| 贴图 | `T_` | | 动画片段 | `AN_` |
| 模型 | `M_` | | 动画控制器 | `AC_` |
| 音频 | `A_` | | 图集 | `SA_` |
| 预制体 | `P_` | | 物理材质 | `PM_` |
| 材质 | `Mat_` | | InputActions | `IA_` |
| Shader/ShaderGraph | `S_` | | Timeline | `TL_` |
| 字体 | `F_` | | RenderTexture | `RT_` |
| 场景 | 无前缀 | | 渲染管线资产 | `RP_`（引入 SRP 时） |

主干规则沿用 `AssetNameNormalizer` 现状：PascalCase、中文词与数字保留、扩展名小写。

### 渲染管线：模板走内置管线，不绑定 SRP

模板**不引入 URP/HDRP**，`Packages/manifest.json` 里没有它们，材质一律用内置管线的 `Standard`
（`runtime.scaffold` 给关卡实体预制体补的那批可视体就是这么建的）。

这是一条**有意为之的不决定**：管线选型取决于品类、目标平台与美术方向，模板三样都不知道，
替宿主拍板等于把每个新项目都锁进一个可能不合适的选择，而退出成本远高于进入成本。

宿主项目要上 URP 时，这是一次单独的迁移轮，范围是可预期的：

1. `Packages/manifest.json` 加 `com.unity.render-pipelines.universal`；
2. 按 `RP_` 前缀在 `Game/Settings/Rendering/` 建管线资产，挂进 Graphics 与 Quality 设置；
3. 把现有材质的 Shader 从 `Standard` 换成 `URP/Lit`（Unity 自带的升级器能过一遍）；
4. 引入贴图时连带配 `Art/Texture/<功能>/import-rules.json`，过 R4 重复检查与 R8 图集对齐。

**别两套并行**：一旦上了 SRP，就把内置管线的材质与 Shader 全部换掉，不留「一半一半」的中间态。

### 输入：工程走新版 Input System

工程的 `activeInputHandler` 是 **1（新版 Input System）**，旧输入管理器已停用——
写 `UnityEngine.Input.GetKey` 这类旧 API 会在运行时抛异常，不是编译期报错，格外要留意。

绑定的**唯一事实源**是 `Game/Settings/Input/` 下按 `IA_` 前缀命名的 InputActions 资产
（现为 `IA_DefaultInput.inputactions`）。改键、手柄、重映射都改它，
**不要再另建一张按键映射表**——两套并行正是这条要消除的东西。

业务侧读的是**动作名**（「前进」）与 `InputActionPhase` 这样的纯 C# 相位，
不是按键：引擎那一半隔在 `Scripts/View/InputDriverBehaviour.cs` 之内，
相位判定留在 `HSGFrame.Input` 里，能在 `dotnet test` 秒级验。

## 六、`import-rules.json` v2（每个正式资产目录一份）

```json
{
  "目录用途": "贴图-UI",
  "文件名前缀": "T_",
  "允许扩展名": [".png"],
  "命名风格": "PascalCase",
  "最大文件字节": 8388608,
  "图集": "SA_Inventory"
}
```

- 新增字段只有 `图集`（可选，只给 UI 贴图目录）。加载分组不写在这里——它是目录级的事，
  住 `bundle-group-rules.json`（见第三节），一处定义一处消费。
- **每个正式资产目录必须被一份导入规则覆盖**（自己有一份，或继承最近祖先的），
  没规则的目录里出现资产即红（R5）。`Scenes/` 两个子夹也各配一份（场景无前缀、限 `.unity`）。

## 七、依赖方向（`dependency-direction-rules.json` 目标表）

| 引用方 | 禁止引用 | 理由 |
|---|---|---|
| `Game/` | `_Inbox/` | 未定名定参的中转品不入正式内容（现状规则改前缀） |
| `Game/Art/` | `Game/ResourceArt/` | 源生资产反向引成品，依赖倒挂 |
| `Game/ResourceArt/`、`Game/Scenes/` | `Scripts/Editor`、`Scripts/Toolkit`、`Tests/` | 编辑器与测试内容不进包，出包必空引 |
| `Game/Scenes/Boot/` | `Game/Scenes/World/`、加载分组为`按需/随场景`的目录 | 入口场景只许直引常驻，其余按 key 动态加载 |
| 任何正式区 | `StreamingAssets/` | 生成物只被构建流程消费 |

## 八、其他资产落点速查

| 资产 | 落点 |
|---|---|
| YooAsset 收集配置（BundleCollectorSetting，现裸在 Assets 根） | `Game/Settings/Resource/` |
| InputActions（`IA_`）/ 物理材质 / 图形质量配置 / 管线资产 | `Game/Settings/` 按类分夹：`Ui/`、`Level/`、`Input/`、`Resource/` 已在用 |
| 图集 | `Game/Settings/Atlas/`，与贴图目录的「图集」字段对齐 |
| UI 面板 UXML/USS/C# | 生成物，源在模板根 `UI/Definitions/*.uidef.json`，产物落 `Scripts/View/_Generated/`（归 `Game.View`，跟着工程编译） |
| 配置表运行时数据 | 生成物，随热更走（RawFile），落点由 codegen/构建命令管 |
| Timeline / VFX | `ResourceArt/<功能>/<模块>/`（成品）；引用的曲线贴图等进 `Art/` |
| 第三方样例、对话与节点编辑器数据 | 各自留地目录，不迁不动 |
| `ProjectSettings/`、`Packages/manifest.json` | 由编辑器与命令管理，手改要走单独提交说明 |
