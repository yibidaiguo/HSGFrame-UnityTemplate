# Template · 路标

一个 Unity 通用游戏模板：纯 C# 逻辑层 + 命令层 + 四级门禁。Logic.* 保持零 UnityEngine 依赖，靠双工程结构让同一批逻辑源码既能在 Unity 里编译，又能在纯 `dotnet test` 下跑测试。

## 五条铁律

1. **Logic.* 保持零 UnityEngine 依赖。** 需要数学用 `Unity.Mathematics`；需要引擎能力时把那段代码放到 `Adapter.Unity` 层。
2. **AI 通过命令层落地资产。** 读 inspect 出的 JSON，改 JSON，再用生成命令写回 `.prefab` / `.unity` / `.asset`。
3. **改测试断言是单独一步。** 走单独一次提交，提交信息带 `[测试变更]` 标记，单独验收。
4. **每改一次跑秒级门禁（`dotnet test`），改完一批跑十秒级门禁（`dotnet build` 全解决方案）。**
   **判据：改动只要落在 `UnityProject/Assets/Game/Scripts/` 或 `Packages/com.hsgframe.*/Runtime/`，
   十秒级不算数——那两级一行 Unity 代码都不编，`Game.View` / `Game.Boot` / `Toolkit.Editor`
   全在覆盖面之外，绿灯是假绿。这时必须跑到分钟级 `Tools/Gates/gate-unity.ps1`。**
   开不了 Unity 的执行后端，改完这两处要如实说「Unity 侧未验」，不许拿十秒级的绿当验收。
5. **产出落文件系统。** 执行后端的每一处成果都要能被 `git diff` 看见。

## 目录路标

| 路径 | 说明 |
|---|---|
| `Solutions/` | 纯 .NET 解决方案：`Logic.Core`（link Unity 源码 + Shim）与 `Logic.Tests`（xunit 测试） |
| `Solutions/UnityShim/` | Unity 序列化特性的空实现 Shim，仅供纯 .NET 侧编译 |
| `Tools/Cli/` | 命令层：`CommandFramework`（特性标记 + 反射扫描 + schema 推导）、`CommandHost`（命令宿主）、`unity-cmd.ps1`（Unity batchmode 入口，带超时必杀）、`toolkit-cmd.ps1`（纯 dotnet 快路径） |
| `Tools/Deps/` | 依赖快照（`Unity.Mathematics.dll`，取自 Unity 6000.3.11f1） |
| `UnityProject/` | Unity 工程本体，`Assets/Game/Scripts/` 下按模块优先摆：`Boot/`（AOT 启动）、`Modules/<模块>/`、`Shared/`、`View/`、`Toolkit/Editor/`，四个程序集见《规范/结构规范-代码》第三节 |
| `Doc/` | 改造方案等文档（在仓库根目录，不在模板内） |
| `规范/` | 结构规范三份（总纲/代码/资源），动目录结构、加模块、放资产前先读；宿主的现状差距与迁移账本在仓库根 `Doc/规范/` |

## 常用命令

```bash
dotnet test Solutions/Template.sln
```

```bash
dotnet build Solutions/Template.sln
```

四级门禁的入口（后两级才编 Unity 侧程序集，判据见铁律 4）：

```bash
pwsh Tools/Gates/gate.ps1
```

```bash
pwsh Tools/Gates/gate-unity.ps1
```

```bash
pwsh Tools/Gates/gate-full.ps1
```

## 名字的归属

- 框架包：`com.hsgframe.*` / `HSGFrame.*`。HSGFrame 是框架自己的名字，
  地位与 `Unity.Mathematics` 一样是依赖，**不跟宿主项目改名**，`project.create` 也不替换它。
- 命名空间：`Template.*`。这是**模板自己的身份**，不是待办事项——
  `project.create` 生成新项目时会按新项目名整体替换掉它（连同 `Template.sln` 与
  `Template.Hotfix.Analyzer.dll` 这类带命名空间的文件名）。
