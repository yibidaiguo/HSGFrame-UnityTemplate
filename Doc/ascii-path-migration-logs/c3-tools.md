# c3 批 · `Tools/` 下的中文名（2026-08-21）

`Config/Luban/` → `Tools/Luban/Config/`（**待办 2 的第二半**），外加 `Tools/` 下九个中文名文件：

| 旧 | 新 |
|---|---|
| `Tools/AssetPipeline/Config/依赖方向规则.json` | `dependency-direction-rules.json` |
| `Tools/AssetPipeline/Config/打包分组规则.json` | `bundle-group-rules.json` |
| `Tools/AssetPipeline/Config/规则覆盖范围.json` | `rule-coverage.json` |
| `Tools/Luban/取工具.ps1` | `Tools/Luban/fetch-tool.ps1` |
| 三处 `来源说明.md`（Deps / Luban / HotfixProbe） | `SOURCE.md` |
| `Tools/Scaffold/Templates/新项目说明.md` | `new-project-readme.md` |
| `Tools/Scaffold/Templates/试验区说明.md` | `scratch-readme.md` |
| 脚手架写进新项目的 `_Scratch/说明.md` | `_Scratch/README.md`（`.gitignore` 的放行条同步改） |

**Luban 那一步差点把文件从 git 视野里抹掉**：`.gitignore` 有一条
`Tools/Luban/*`（Luban CLI 六百来个文件不进仓库），把 `Config/Luban/` 挪进
`Tools/Luban/Config/` 会被它整个吞掉。
脚本里**先放行、后挪**，顺序反了 `git mv` 之后那八个文件会「还在盘上、但不在 git 里」——
而 `git status` 干干净净，没有任何提示。验收靠
`git ls-files Tools/Luban/` 与 `git check-ignore -v`，两条都过了。
