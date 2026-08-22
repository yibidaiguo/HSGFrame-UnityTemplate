---
name: create-module
description: 建一个新玩法模块时的目录导航：每个阶段该读哪份规范、该跑哪条命令、哪道门禁在管你。当用户说"新建一个 XXX 模块""加个 XXX 系统""这个功能放哪"，或直接打 /create-module 时用它。
---

# 新建模块：什么时候读什么

**这份技能本身不含规则，它只负责把你送到正本那一页。**规则的正本在 `Specifications/` 三份里，
在那里改、在这里指路——两处都写规则，迟早各说各话（总纲 §三·2 单一事实源）。

模块名先定下来：**全英文 PascalCase、一个完整名词**（`Inventory`、`Quest`、`Narrative`），
它会同时成为目录名、命名空间段和程序集引用里的一段，之后改名很贵。

## 五个阶段

| 阶段 | 动手前必读 | 这一步产出什么 | 谁在查你 |
|---|---|---|---|
| ① 起夹之前 | 《总纲》§二 §三 · 《代码》§二 | 确定它是不是一个模块、叫什么 | 人 |
| ② 搭骨架 | 《代码》§二 §三 §五 | 目录 + 服务/状态 + 程序集归属 | `gate.layerboundary` |
| ③ 写逻辑 | `CLAUDE.md` 铁律 1 · 《代码》§四 §六 | Logic 代码 + 单测 | `gate.moduleboundary` `gate.businesslog` `gate.naming` |
| ④ 要资产 | 《资源》§一 §三 §五 · `/art-recipe` | 资产请求 → 变体 → 入库 | `gate.assetspec` `asset.*` |
| ⑤ 收尾 | 本页最后一节「三件套」 | README + 业务规范 + 模块技能 | `gate.modulereadme` + 人 |

《总纲》= `Specifications/structure-overview.md`，《代码》= `structure-code.md`，
《资源》= `structure-assets.md`。**动手前先扫一眼 `Doc/pitfalls.md`**，它是踩过的坑，不是理论。

## ① 起夹之前：先确认它真是一个模块

读《总纲》§二（三个代码体：框架 / 业务 / 工具链，你多半在**业务**这一体）
和 §三·4（分级公式：代码按 `层 → 模块 → 职责`）。

三个常见的「其实不该建模块」：

- **只有一两个类** → 先放进已有模块，或 `Shared/`。目录是给人和 AI 导航的，不是仪式（《代码》§二）。
- **要被两个以上模块用** → 那是 `Shared/`，不是新模块。但 `Shared` 的门槛是**已经**有 ≥2 个使用者，
  不是「以后可能」。
- **是编辑器工具 / 命令 / 门禁** → 那是工具链，住 `Tools/`，不走这条路。

## ② 搭骨架

读《代码》§二拿目录形状、§三拿程序集归属、§五拿命名空间。要点：

- 落点 `UnityProject/Assets/Game/Scripts/Modules/<模块名>/`。
- **服务与状态住模块根**（`<模块名>Service.cs`、`<模块名>State.cs`），六个职责夹
  （`Contracts/ Events/ Data/ View/ Utilities/ Editor/`）**按需建，空夹不建**。
  模块内文件少于 5 个时全放模块根也行，超了再分。
- 程序集**不新增**：Logic 归 `Game.Logic`；模块里的 `View/` 与 `Editor/` 各放一个 `.asmref`
  指向 `Game.View` / `Game.Editor`。程序集数量本身就是编译与加载的成本。
- 要跑起来的运行时资产（可视体、面板设置、启动场景）用命令生成，别手搭：

```bash
dotnet run --project Tools/Cli/CommandHost/CommandHost.csproj -- describe runtime.scaffold
```

UI 面板走 `ui.scaffold`：`uidef.json` 是唯一事实源，UXML/USS/C# 三件套是生成物，**不手改**。

## ③ 写逻辑

- **铁律 1 压过一切**：`Logic.*` 零 UnityEngine。要数学用 `Unity.Mathematics`；
  真要引擎能力，那段代码搬去 `View/`（`Adapter.Unity` 那一层）。
- **公开面只有 Contracts + Events**（《代码》§四）。模块 Y 引用模块 X 只准
  `using <根>.X.Contracts` / `.Events`。两个模块要对话，优先「Y 订 X 的事件」，
  而不是互相拿服务——拿服务是把两个模块焊死。
- 打日志走 `HSGFrame.Logging`，不写裸 `Debug.Log`（《代码》§六，R7 查 `Modules/ Shared/ View/`）。
- 单测进 **`Solutions/Logic.Tests/`**（业务模块都在这一份里；`Solutions/<模块>.Tests/`
  是**框架包**的惯例，别混）。`Solutions/Logic.Core.csproj` 已经链接了 `Scripts/Modules/**`
  与 `Scripts/Shared/**`（排除 `View/` `Editor/`），新模块的源码自动被纯 .NET 侧编到，
  不用改工程文件——但**链接范围就是 `Game.Logic` 的定义，两处必须一致**（R3 对账）。
- **改测试断言是单独一次提交**，提交信息带 `[测试变更]`（铁律 3）。

每改一次跑秒级门禁；改完一批跑十秒级：

```bash
dotnet test Solutions/Template.sln
```

**注意铁律 4 的判据**：改动落在 `UnityProject/Assets/Game/Scripts/` 就得跑到分钟级
`pwsh Tools/Gates/gate-unity.ps1`——前两级一行 Unity 代码都不编，绿灯是假绿。
开不了 Unity 就如实说「Unity 侧未验」。

## ④ 要资产

**别在这里现编尺寸和落点。**读《资源》§一（目录树）、§三（静态/动态与加载分组）、
§五（文件名前缀表），然后走 `/art-recipe` 那份技能——它管「复用哪份配方还是新建」、
「模块自己的尺寸写哪」、「风格怎么定」。

模块自己的规格覆盖住 `Specifications/Business/<模块名>/asset-spec.json`，
出图时 `art.request --Module <模块名>` 才取得到。

## ⑤ 收尾：模块完成三件套

**三件不齐不算收尾。**

1. **`Modules/<模块名>/README.md`**，≤40 行：一句话职责、公开面清单（Contracts + Events）、
   依赖了谁的事件。里面的命令与路径要能直接复制执行。`gate.modulereadme` 查它在不在。
2. **`Specifications/Business/<模块名>/`**：**有业务规范才建**。
   模块自己的资产规格覆盖、业务约束住这里。没有就不建，空目录是噪音。
3. **`.claude/skills/<模块名>/SKILL.md`**：这个模块怎么用、扩展点在哪、触发词写清。
   做完一个模块就长出配套技能，别让下一个人从代码里猜。格式照本文件的头部。

收尾跑一次全量：

```bash
pwsh Tools/Gates/gate.ps1
```

## 一直有效的几条

- 新文件先问「它属于哪一格」（《总纲》§三·4）。答不上来就是还没想清楚，别先建目录。
- 下划线前缀只给 `_Inbox/ _Generated/ _Scratch/` 三个机器管理区，正式文件不许以下划线开头。
- 标识符全英文完整单词，**不许中文标识符**（`gate.naming` 查）；中文写进注释与数据键。
- 全仓路径 ASCII：目录名与文件名都不许有中文（`gate.pathascii`，block 模式、零豁免）。
- 模板与工具链里**不许出现宿主项目名**；`HSGFrame` 是框架自己的名字，不在此列。
- 想清楚但现在做不了的事，写进 `Doc/Backlog.md` 并注明卡在谁手里；踩到的坑写进 `Doc/Bugs.md`。
