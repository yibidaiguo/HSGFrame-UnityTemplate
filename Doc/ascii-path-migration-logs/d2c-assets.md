# d2c 批 · 关卡样例、UI 定义与主题样式表（2026-08-21）

| 旧 | 新 |
|---|---|
| `Levels/村庄/关卡.json` | `Levels/Village/level.json`（**`关卡.json` 是代码常量**，不是随便一个文件名） |
| `Levels/村庄/区块_{村口,广场}.json` | `Levels/Village/block-{gate,square}.json` |
| `Solutions/Logic.Tests/TestData/村庄/` | `TestData/Village/`（同样三份） |
| `UI/Definitions/主界面.uidef.json` | `UI/Definitions/MainPanel.uidef.json` |
| `Solutions/UiFramework.Tests/TestData/{主界面,嵌套面板}.uidef.json` | `{MainPanel,SettingsPanel}.uidef.json` |
| `Packages/com.hsgframe.uiframework/Theme/主题变量.uss`（含 `.meta`） | `theme-variables.uss` |

## 这一批的三个耦合点

**一、区块清单里的条目就是文件名。** `level.json` 的 `区块清单: ["block-gate","block-square"]`
直接决定去读哪两个文件——改文件名就得同时改这个数组，改一半的表现是
「关卡加载报某某区块缺失」。

**二、`关卡.json` 是 `LevelRepository` 里的常量**，不是约定俗成。
它住在 `UnityProject/Assets/Game/Scripts/`，所以这一批**必须跑分钟级门禁**（铁律 4）。

**三、UI 三件套是生成物，改定义文件名要重新生成。**
`ui.scaffold` 的幂等校验当场判红（`产物与定义不一致：MainPanel.cs`）——
生成物的头注释里写着定义文件的路径。**这道校验是这一批唯一自动发现问题的地方**，
它比测试更早、也更准。

## 又一次「目录名与展示名共用一个常量」

Unity 的 EditMode 测试里有 `private const string LevelName = "村庄"`，
**一个常量同时当目录名与关卡展示名用**。目录改成 `Village` 之后它两头都对不上：
按目录找找不到，按展示名断言又该是「村庄」。拆成 `LevelDirectoryName` 与 `LevelName` 两个。

这是这一轮第三次撞见同一个形状（前两次：设计池分类、配置表名）。
**规律很清楚：一个字符串同时当路径和展示名用，去中文化那一刻必然两头对不上。**

## 验收

- `dotnet test` 全绿、`gate.ps1` PASS 全绿。
- **`gate-unity.ps1` PASS**：Unity 编译绿、EditMode 27/27、`.meta` 完整性 0 问题。
  第一次跑是**红的**（9 条 EditMode 因为上面那个常量），修完才绿——
  这正是铁律 4 存在的理由：那 9 条红，秒级与十秒级门禁一条都看不见。

## 留给 e 批的一条

`SceneManager.GetSceneByName("村庄")` 与 `_firstWorldSceneName = "村庄"` 指的是
**Unity 场景资产**（`UnityProject/Assets/` 下那 76 个之一）。
那一批改名时，这两处要跟着改。
