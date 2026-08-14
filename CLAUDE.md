# Template · 路标

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
| `Tools/` | 工具与依赖快照（`Tools/Deps/Unity.Mathematics.dll`） |
| `UnityProject/` | Unity 工程本体，`Assets/_Project/Scripts/Logic/` 下按 Contracts / Data / State / Service 分层 |
| `Doc/` | 改造方案等文档（在仓库根目录，不在模板内） |

## 常用命令

```bash
dotnet build Template/Solutions/Template.sln
dotnet test Template/Solutions/Template.sln
```
