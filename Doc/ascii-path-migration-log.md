# 路径去中文化 · 各批落地记录

> 设计审查与分批表在 [路径去中文化](ascii-path-migration.md)。
> 这一份按批次倒序记：对照表、做法、验收，以及每一批踩到的坑。



## d1 批 · `Pools/` `_Generated/` `Bridges/`（2026-08-21）

| 旧 | 新 |
|---|---|
| `Pools/Schema/{基线,项目}/` | `Pools/Schema/{Baseline,Project}/` |
| `Pools/Schema/Baseline/需求.schema.json` 等五份 | `requirement / work-item / design-record / asset-request / provenance` |
| `Pools/Designs/{定稿,汇总,记录}/` | `Pools/Designs/{Final,Digest,Records}/`，`定稿.json` → `final.json` |
| `Pools/{专项,审查意见,晋升提案,知识,组织}/` | `Pools/{Epics,ReviewOpinions,Promotions,Knowledge,Organization}/` |
| `Bridges/*/依赖清单.json` | `dependencies.json` |
| `Bridges/comfyui/配方/图标@v5/` | `Bridges/comfyui/recipes/icon@v5/`（`映射.json` → `mapping.json`） |
| `_Generated/Bridges/feishu/*` | `table-description / epic-table / validation-messages / fingerprint`，`助手配置包/` → `assistant-package/`，`知识/` → `knowledge/` 及其五份 |
| `_Generated/门禁报告.json` | `_Generated/gate-report.json`（**只有消费者、没有生产者**，改了不会有症状，只能靠搜字符串） |

### 这一批必须讲清楚的两件事

**一、实体名与文件名解耦。** `需求` `工作项` `资产请求` 这些是**数据里的领域词汇**，
中文留着才读得懂（`"实体": "需求"` 出现在 schema、信封、卡片、面板文案里）。
但文件名要 ASCII。所以 `PoolPaths` 加了一张显式的
**实体名 → 文件名词干**表（`EntityFileStem`）。表里没有的实体按原名返回——
那样路径门禁会把它列出来，比悄悄拼一个中文文件名强。

**二、目录名与展示标签解耦。** 面板设计池页的分类列显示的就是「定稿 / 汇总 / 记录」，
而目录已经是 `Final / Digest / Records`。原来这两样共用一个字符串常量，
改目录名那一刻页面文案会跟着变英文，或者忘了改其中一处、那一整类恒显示为空。
现在 `DesignCategories` 是 `(目录名, 展示标签)` 两栏。

### 踩到的三个坑

1. **无脑替换误伤数据字段**：`"配方"` 既是目录名，也是**溯源边车里的字段名**。
   脚本把 schema 里的 `{ "名称": "配方" }` 换成了 `"recipes"`，
   两条溯源测试当场红。**测试替我看住了**——这正是「断言的形状决定它能不能发现故障」：
   那两条断言的是「校验零发现」与「往返逐字段相等」，所以字段名一变就红。
2. **目录移动的顺序**：先把 `助手配置包/知识` 挪成 `assistant-package/knowledge`，
   再挪 `助手配置包` 时目标已存在，于是整个旧目录被塞进了新目录里，成了
   `assistant-package/助手配置包/`。**先挪子目录、后挪父目录会撞这个。**
3. **`final.json` 的取名规则**：定稿是一稿一目录，文件名恒定、目录名才是这份定稿的名字。
   读取器靠「文件名等于某个常量」判断要不要取目录名——文件名从 `定稿.json` 改成
   `final.json` 时那个常量要跟着改，**漏改的表现是名字变成「final」，不是报错**。

### 验收

- `dotnet test` 全绿（25 个测试工程）、`dotnet build` 0 错误、`gate.ps1` PASS 全绿。
- **真跑重生成**：`bridge.provision` 重跑 → 11 个产物全部按新名落盘；
  `gate.provision` 供给对账 0 问题（指纹只比内容哈希，改名不影响，实证）。
- **真跑助手**：`assist.serve` 干跑 → 「知识文件 5 份」，说明
  `assistant-package/knowledge/` 那条路径真读到了。

## c3 批 · `Tools/` 下的中文名（2026-08-21）

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

## c2 批 · `规范/` → `Specifications/`（2026-08-21）

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

## c1 批 · `Tools/CreationPipeline/Config/`（2026-08-21）

`Config/创作管线/` 整个挪进工具自己的目录下，文件名一并去中文——**待办 2 的第一半**。

| 旧 | 新 |
|---|---|
| `Config/创作管线/本机.json` | `Tools/CreationPipeline/Config/local.json`（仍在 .gitignore 里） |
| `Config/创作管线/本机.示例.json` | `Tools/CreationPipeline/Config/local.example.json` |
| `Config/创作管线/下游.json` | `Tools/CreationPipeline/Config/downstream.json` |
| `Config/创作管线/引擎.json` | `Tools/CreationPipeline/Config/engine.json` |
| `Config/创作管线/同步水位.json` | `Tools/CreationPipeline/Config/sync-watermark.json` |

**最危险那一步的验收**（密钥不入库，决策 5）：

```
git check-ignore -v Tools/CreationPipeline/Config/local.json
→ .gitignore:80:Tools/CreationPipeline/Config/local.json
git status --porcelain | grep local.json
→ 空
```

**两条 `Path.Combine` 的写法都要改**：C# 里是按段写的（`"Config", "创作管线"`），
文档与脚本里是正斜杠（`Config/创作管线`）。只改一种，另一种会静默留在原地。

引用同步改了 38 个文件，含 5 处路径常量（`PipelinePaths` / `EngineSettings` /
`LocalBridgeSettings` / `BridgeRouteTable` / `SyncWatermark`）、7 份测试、
飞书长连接旁路那个 Python 脚本、面板读取器。

**旁路进程重启过**：它在启动时读一次配置，不重启就还拿着旧路径。
重启后日志确认「会话目录」那一行出现（这是批次 12 加的），并重新连上长连接。

真跑验收：`bridge.balance` 真发一次请求（它要读 `local.json` 里的密钥）→ HTTP 200。
**这条比测试绿有力**：测试用的是临时目录造的假结构，真读的是新路径的真文件。

## b 批 · `Doc/` 全部改名（2026-08-21）

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

## a 批 · 门禁先立（2026-08-21）

- 新增 `Tools/Gates/PathAsciiChecker.cs` 与命令 `gate.pathascii`，接进 `gate.ps1`。
- 配置加两个键：`pathAsciiMode`（`warn` / `block`）与 `pathAsciiExemptPrefixes`（存量欠账名单）。
- 首次扫描结果：**存量 222 条**（含未跟踪文件；已跟踪的是 206 条）。
- **刻意先 warn**：一道新规矩默认不该把别人的构建弄红；但它照样把每一条列出来——
  看不见存量的规矩等于没有。
