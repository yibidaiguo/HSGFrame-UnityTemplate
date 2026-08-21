# d2a 批 · 文档、CI 定义与样例（2026-08-21）

没有语义耦合的一批，只是名字：

| 旧 | 新 |
|---|---|
| `开始使用.md` | `getting-started.md` |
| `Memory/记忆库说明.md` / `项目约定.md` | `Memory/README.md` / `project-conventions.md` |
| `Graphs/示例流程图.json` | `Graphs/sample-flow.json` |
| `提案/检查器/` | `Proposals/Checkers/` |
| `Pipelines/流水线定义.json` | `Pipelines/pipelines.json` |
| `Pipelines/Jenkinsfile.{秒级门禁,十秒级门禁,夜间构建,发布}` | `Jenkinsfile.{fast-gate,build-gate,nightly,release}` |

**又踩了一次「替换太贪」**：`提案/` → `Proposals/` 把注释里的 `晋升提案/` 变成了
`晋升Proposals/`。这次没有测试能看见（它在 XML 注释里），
是**改完逐条看 `git diff` 才抓到的**。
教训跟 d1 那条同源：**替换串越短越危险**，短到成为别的词的一部分时，
唯一的防线就是人眼过一遍 diff。
