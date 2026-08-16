# 结构规范 · 代码

> 前置：同目录《结构规范-总纲》。本份只讲代码怎么摆、程序集怎么装、引用怎么限。

## 一、框架层（UPM 包）

```
Packages/com.hsgframe.<模块>/
  package.json
  Runtime/   HSGFrame.<模块>.asmdef + 源码
  Editor/    HSGFrame.<模块>.Editor.asmdef（有编辑器面才建）
  Tests/     或链接进 Solutions/<模块>.Tests 走 xunit（现状做法，保持）
```

- 包名 `com.hsgframe.<模块>`，程序集与命名空间 `HSGFrame.<模块>`。它是独立框架，
  不跟宿主项目改名，`project.create` 不替换它——生成器因此没有「包前缀」参数。
  历史上的 `com.gametemplateforagent.*` 九个包已于 2026-08-16 全部并入此规则。
- 框架引用方向：框架包之间可以互引（在 package.json 里声明），**框架永远不引业务**；
  框架里出现宿主项目名由 `gate.generic` 拦（既有）。
- 纯逻辑框架包（事件、对象池、计时器这类）保持零 UnityEngine，才能被 Solutions 链接测试。

## 二、业务层目录（`Assets/Game/Scripts/`）

```
Scripts/
  Boot/                 启动装配（AOT 常驻；不引用任何可选功能的程序集——既有铁律）
  Modules/              每个玩法系统一夹
    <模块名>/
      <模块名>Service.cs     服务——模块根目录
      <模块名>State.cs       状态——模块根目录
      Contracts/             接口与纯契约类型
      Events/                事件定义（纯 C# 数据类型）
      Data/                  数据结构与表访问；生成物进 Data/_Generated/
      View/                  视觉：UI、表现、MonoBehaviour（asmref → Game.View）
      Utilities/             模块内工具
      Editor/                模块专属编辑器工具（asmref → Game.Editor）
  Shared/               跨模块通用，内部按同样的职责夹分：Contracts/ Events/ Data/ Utilities/ View/ Editor/
  View/                 Game.View.asmdef 落点 + 跨模块共享视觉基类
  Editor/               Game.Editor.asmdef 落点 + 跨模块共享编辑器工具
  Toolkit/Editor/       工具链驻 Unity 的编辑器入口（Toolkit.Editor 程序集，见第五节）
Tests/EditMode|PlayMode 保持现状位置与装配
```

要点：

- **状态与服务住模块根**，六个职责夹按需建，空夹不建。
- 模块内文件少于 5 个时允许先不开职责夹、全放模块根；超过再分。目录是给人和 AI 导航的，不是仪式。
- **Shared 的进入门槛**：被 ≥2 个模块引用才进 `Shared/`；只有一个使用者的东西留在那个模块里。
  从模块上提到 Shared 是单独一次提交。
- View 夹与 Editor 夹靠 **asmref** 归并进各自的公共程序集（见下节），
  这样模块文件夹完整，程序集数量又不膨胀（程序集数量本身就是编译与加载的成本）。

## 三、程序集装配

| 程序集 | 覆盖 | 平台 | 热更 | 允许引用 |
|---|---|---|---|---|
| `Game.Boot` | `Scripts/Boot/` | 运行时 | 否（AOT） | HSGFrame.*、YooAsset；禁引可选功能的程序集 |
| `Game.Logic` | `Scripts/Modules/` + `Scripts/Shared/`（View/、Editor/ 夹被 asmref 挖走） | 运行时 | 是 | 仅 `Unity.Mathematics`。零 UnityEngine、零第三方、零 async（既有铁律） |
| `Game.View` | `Scripts/View/` 为 asmdef 落点；每个模块的 `View/` 放一个指向它的 `.asmref` | 运行时 | 是 | UnityEngine、Game.Logic、HSGFrame.*、UI Toolkit |
| `Game.Editor` | `Scripts/Editor/` 为落点；每个模块的 `Editor/` 放 `.asmref` | 仅编辑器 | — | 不限 |
| `Toolkit.Editor` | `Scripts/Toolkit/Editor/` | 仅编辑器 | — | 不限；程序集名与命名空间保持 `Template.Toolkit.Editor` 不动 |

- 事件总线（`HSGFrame.Event`）在 Logic 层不直接引用：模块的事件类型是 `Events/` 里的纯 C# 类型，
  发布订阅由 Boot/View 侧桥接。这保住 Logic 的零依赖与 dotnet 可测。
- `Solutions/Logic.Core.csproj` 链接 `Scripts/Modules/**` 与 `Scripts/Shared/**`，
  排除 `**/View/**` 与 `**/Editor/**`——链接范围就是 Game.Logic 的定义，两处必须一致（R3 对账）。
<!-- feature:hotfix 开始 -->
- 热更程序集清单 = `Game.Logic`、`Game.View`、`HSGFrame.Hotfix.Probe`（HybridCLR 设置里维护，命令校验）；
  框架包与 Boot 走 AOT，框架改动走版本发布，业务改动走热更。
- 热更自身的程序集全部关在 `Packages/com.hsgframe.hotfix/` 内（`HSGFrame.Hotfix` /
  `.HybridCLR` / `.Probe` / `.Editor`），常驻程序集一处都不许引它们——第十三道门禁把关。
  摘掉热更走 `feature.remove --name hotfix`。
<!-- feature:hotfix 结束 -->
- 程序集名（`Game.*`）不含模板身份也不含宿主名，不参与 `project.create` 替换，
  跨生成项目保持稳定——csproj 引用因此不用跟着项目名变。
- 被 CLI/门禁用 FQN 调用的编辑器入口（`CompileCheckEntry`、`PlayerBuildCommandLine`、
  场景构建入口等）属于 `Toolkit.Editor`，**方法全名是对外契约**，挪目录可以，改名要同步
  `gate-unity.ps1`、`gate-config.json`、流水线脚本，单独提交。

## 四、跨模块引用规则（R2 检查器把关）

一句话：**模块的公开面 = Contracts + Events，其余都是私有。**

- 模块 Y 引用模块 X：只准 `using <根>.X.Contracts` / `using <根>.X.Events`（含限定名直写）。
  引到对方 Service、State、Data、View、Utilities 即违规。
- 都想引的类型放 `Shared/`；两个模块要对话，优先「Y 订 X 的事件」而不是互相拿服务。
- 环状依赖（X↔Y 互引 Contracts）允许存在但要在评审里说明；出现第三个环成员必须拆 Shared。
- View 只准引本模块 Logic 与 Shared；跨模块的视觉复用下沉 `Scripts/View/` 或 `Shared/View/`。

正例：`Quest` 模块监听 `Inventory` 模块 `Events/` 里的 `ItemAcquired` 更新任务进度。
反例：`Quest` 直接 using `Inventory` 模块根拿 `InventoryService` 查背包——改成订事件或走 Contracts 接口。

检查范围：`Scripts/` 全树，**除去 `Scripts/Toolkit/`**。工具链是编辑器侧的东西、天然要深入模块内部
（关卡编辑器不认识关卡数据就没法工作），它也不进包、不参与模块之间的耦合，所以是永久的范围之外，
不占豁免清单的位置。豁免清单（`gate-config.json` 的 `moduleBoundaryExemptPaths`）只挂欠账，
拆一处删一条，燃尽即边界完全立住。模块名由 `Scripts/Modules/` 的子目录决定，不另开清单。

## 五、命名空间

| 层 | 命名空间 |
|---|---|
| 框架 | `HSGFrame.<模块>` |
| 业务 | `Template.<模块>[.Contracts/.Events/.Data/.View/.Utilities/.Editor]`。`Template` 是模板身份，`project.create` 生成新项目时整体替换成项目名 |
| 工具链 | `Template.Toolkit.<模块>`（同上，随替换机制走） |

`Shared/` 里的东西按同一条公式走 `Template.Shared.<职责>`；`Boot/` 与 `Scripts/View/`
这两个非模块落点用 `Template.Boot` 与 `Template.View`。

大改结构时，**结构轮只挪文件、命名空间一个不改，命名空间对齐单独走一轮**，
中间那段目录与命名空间不一致是已知状态。但**对齐轮要与 R2 检查器同轮上线**——
R2 查的正是这批命名空间，改完没人守就会立刻开始漂。

## 六、文档级约定（无检查器，靠评审）

- 一个文件一个主类型，紧密伴生的小类型（如返回值记录）可同居；文件名 = 主类型名。
- 业务代码打日志走 `HSGFrame.Logging` 的接口，不写裸 `UnityEngine.Debug.Log`。
  **R7 查 `Modules/`、`Shared/`、`View/` 三棵子树**；`Boot/` 与 `Toolkit/` 不查（启动装配与工具链
  本来就直接对着引擎说话）。唯一的永久豁免是日志落点自己（`View/UnityConsoleLogSink.cs`）。
- 每个模块根放一份 ≤40 行的 `模块说明.md`：一句话职责、公开面清单、依赖了谁的事件。
  说明里的命令与路径要能直接复制执行。
- `Update` 类逐帧逻辑集中经 `HSGFrame.MonoDriver` 驱动，业务 MonoBehaviour 数量克制——性能维度的代码侧。
