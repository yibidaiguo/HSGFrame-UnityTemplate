# GameTemplateForAgent · 路标

一个面向 AI 协作的 Unity 通用游戏模板：纯 C# 逻辑层 + 命令层 + 四级门禁。
`Logic.*` 保持零 UnityEngine 依赖，靠双工程结构让同一批逻辑源码既能在 Unity 里编译，
又能在纯 `dotnet test` 下跑测试。

本仓库 2026-08-15 从 `D:\Projects\Unity\RPG` 的 `Template/` 子目录搬出，
带着完整的 14 次阶段提交历史。


一个 Unity 通用游戏模板：纯 C# 逻辑层 + 命令层 + 四级门禁。Logic.* 保持零 UnityEngine 依赖，靠双工程结构让同一批逻辑源码既能在 Unity 里编译，又能在纯 `dotnet test` 下跑测试。

## 五条铁律

1. **Logic.* 保持零 UnityEngine 依赖。** 需要数学用 `Unity.Mathematics`；需要引擎能力时把那段代码放到 `Adapter.Unity` 层。
2. **AI 通过命令层落地资产。** 读 inspect 出的 JSON，改 JSON，再用生成命令写回 `.prefab` / `.unity` / `.asset`。
3. **改测试断言是单独一步。** 走单独一次提交，提交信息带 `[测试变更]` 标记，单独验收。
4. **每改一次跑秒级门禁（`dotnet test`），改完一批跑十秒级门禁（`dotnet build` 全解决方案）。**
5. **产出落文件系统。** 执行后端的每一处成果都要能被 `git diff` 看见。

## 目录路标

| 路径 | 说明 |
|---|---|
| `Solutions/` | 纯 .NET 解决方案：`Logic.Core`（link Unity 源码 + Shim）与 `Logic.Tests`（xunit 测试） |
| `Solutions/UnityShim/` | Unity 序列化特性的空实现 Shim，仅供纯 .NET 侧编译 |
| `Tools/Cli/` | 命令层：`CommandFramework`（特性标记 + 反射扫描 + schema 推导）、`CommandHost`（命令宿主）、`unity-cmd.ps1`（Unity batchmode 入口，带超时必杀）、`toolkit-cmd.ps1`（纯 dotnet 快路径） |
| `Tools/Deps/` | 依赖快照（`Unity.Mathematics.dll`，取自 Unity 6000.3.11f1） |
| `UnityProject/` | Unity 工程本体，`Assets/_Project/Scripts/Logic/` 下按 Contracts / Data / State / Service 分层 |
| `Doc/` | 改造方案等文档（在仓库根目录，不在模板内） |

## 常用命令

```bash
dotnet build Template/Solutions/Template.sln
dotnet test Template/Solutions/Template.sln
```
