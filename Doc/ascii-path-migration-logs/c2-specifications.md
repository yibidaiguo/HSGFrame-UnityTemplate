# c2 批 · `规范/` → `Specifications/`（2026-08-21）

| 旧 | 新 |
|---|---|
| `规范/基线/` | `Specifications/Baseline/`（`资产规格.基线.json` → `asset-spec.baseline.json`，`放行策略.基线.json` → `release-policy.baseline.json`） |
| `规范/项目/` | `Specifications/Project/`（`资产规格.json` → `asset-spec.json`，`放行策略.json` → `release-policy.json`） |
| `规范/业务/` | `Specifications/Business/` |
| `规范/结构规范-总纲.md` | `Specifications/structure-overview.md` |
| `规范/结构规范-代码.md` | `Specifications/structure-code.md` |
| `规范/结构规范-资源.md` | `Specifications/structure-assets.md` |

引用同步改了 52 个文件，含 `CLAUDE.md`、`AGENTS.md`、四道资产门禁、
`SpecificationPaths` 那一族路径常量。

**顺手补了 `SpecificationPaths.BusinessRoot(repositoryRoot)`**：
面板要枚举「有哪些模块写了规范」，而原来那个类只有「按模块名取目录」的方法，
回答不了这个问题，于是面板自己 `Path.Combine` 了一遍——**路径就有了第二个来源**。
补上根目录方法，那处绕过才有地方收。

**这一批唯一红过的地方值得记**：`Dashboard.Tests` 的规范页测试全红了 6 条。
原因不是代码错，是**测试夹具自己造的目录名没跟着改**——
它写 `Specifications/基线/…`，而读取器找的是 `Specifications/Baseline/`。
测试用的是系统临时目录、造的是自己的一棵树，所以**改名脚本扫不到它**。
这类「夹具里硬写的目录名」是改名批次最容易漏的一处，
而它的表现恰好是「读出来是空的」——跟目录真的空长得一模一样。
