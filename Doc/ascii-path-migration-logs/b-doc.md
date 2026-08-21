# b 批 · `Doc/` 全部改名（2026-08-21）

对照表：

| 旧 | 新 |
|---|---|
| `Doc/创作管线进度.md` | `Doc/creation-pipeline-progress.md` |
| `Doc/创作管线P8计划.md` | `Doc/creation-pipeline-p8-plan.md` |
| `Doc/创作管线锁定决策.md` | `Doc/creation-pipeline-decisions.md` |
| `Doc/创作管线锁定决策P8.md` | `Doc/creation-pipeline-decisions-p8.md` |
| `Doc/创作管线要你填的.md` | `Doc/creation-pipeline-user-setup.md` |
| `Doc/策划美术工作流接入方案.md` | `Doc/design-art-workflow-proposal.md` |
| `Doc/创作管线子文档/` | `Doc/creation-pipeline-subdocs/`（七份按内容意译，如 `03-执行引擎.md` → `03-execution-engine.md`） |
| `Doc/创作管线批次日志/` | `Doc/creation-pipeline-batch-logs/`（三十余份按批次意译） |

做法与验收：

- 全部用 `git mv`，git 认出来是重命名（`R` 状态），历史跟得住。
- 引用同步改了 49 个文件：文档间的交叉链接、`.gitignore` 的注释、
  `gate-config.host.json` 的文档豁免、四处 C# 里当「参考示例路径」用的字符串字面量。
- **只改路径，不改正文**：文档标题、链接文字、散文里说的「子文档 05 §一」全留着——
  中文在文件内容里从来不是问题。
- 验收：写脚本把 `Doc/**/*.md` 里每一条相对链接逐个解析，**断链 0 条**；
  全仓搜六个旧文件名与两个旧目录名，**零命中**；
  `dotnet test` 全绿、`dotnet build` 0 错误、`gate.ps1` PASS 全绿。

**`Specifications/` 从 b 批挪到 c 批**：它底下有 `基线/*.json` 与 `项目/*.json` 是**代码要读的数据**
（`SpecificationPaths` 那一族），不属于「纯文档、无代码引用」。跟 `Config/` 一起改更安全。
