# 路径去中文化 · 设计审查与迁移账本

> 上游：[待办账本](Backlog.md) 第 1 条（文件名与目录名去中文化）与第 2 条（`Config/` 分层）。
> 这两条**必须合批**——都要挪 `Tools/CreationPipeline/Config/`、都要改 `.gitignore` 的密钥路径、
> 都要改路径常量与门禁配置。分两次做等于把同一批危险改动做两遍。

## 一、设计审查

### 1. 真正要解决的问题

**不是「中文不好看」，是中文路径会在别处炸。**
git 在不同 `core.quotepath` 下显示不一致；CI 容器 locale 不是 UTF-8 时路径会烂；
某些 .NET / MSBuild / Unity 的路径处理在非 ASCII 下有历史坑；命令行里还要额外操心引号。
本仓已经踩过一个近亲：**`gate.ps1` 的输出重定向到文件时子进程 JSON 日志会丢**（编码相关）。

同一批要还的第二笔账：`Config/` 根下混着**三种不同的东西**——
业务数据（`Tables` `Schema` `Mirror`，策划天天改）、表格工具链（`Luban`，程序很少改）、
机器配置（`创作管线`，装机的人一次性）。而结构规范总纲 §四把 `Config/` 定义成
「数据与定义（单一事实源侧）」，机器配置本来就不该在里面。

### 2. 影响面（2026-08-21 实测）

**206 / 1283 个已跟踪文件**带中文路径，**25 个中文目录**：

| 顶层 | 文件数 | 难点 |
|---|---|---|
| `UnityProject` | 76 | Unity 靠 GUID 引用，`.meta` 跟着一起挪就安全，但**要真开 Unity 验** |
| `Doc` | 48 | 互相有大量中文文件名的交叉链接，改名要同步改链接 |
| `Pools` | 14 | 目录名进了代码常量（`PoolPaths` 那一族） |
| `Config` | 11 | 含 `Tools/CreationPipeline/Config/`，还进了 `.gitignore` |
| `_Generated` | 11 | 产物，重生成即可；但**指纹要重算** |
| `Tools` | 9 | |
| `规范` | 8 | 目录名本身就是中文，且写死在 CLAUDE.md 与多份规范文档里 |
| 其余 | 29 | `Pipelines` / `Solutions` / `Bridges` / `Levels` / `提案` / `开始使用.md` 等 |

### 3. 两条锁定决策要改

**决策 1** 原文：「C# 一律落 ASCII 目录……中文只许出现在纯数据目录（`Pools/专项/`）与**文件名**里」。
新写法：**全仓的目录名与文件名一律 ASCII；中文只许出现在文件内容里**
（注释、文案、数据值、JSON 的键都不受限）。适用范围从「含 `.cs` 的目录」扩到全仓。

**决策 2** 原文：「配置落 `Tools/CreationPipeline/Config/`，不落 `Config/` 根」。
新写法：**工具链配置落 `Tools/<工具>/Config/`，`Config/` 只留业务数据。**
这不是发明新规矩，是**回到仓库里早就有的惯例**——`Tools/Gates/Config/`、
`Tools/AssetPipeline/Config/`、`Tools/CodeGen/Config/`、`Tools/Indexing/Config/`
四个工具都是这么放的，只有 `创作管线` 破了例。

### 4. 被否掉的替代

**否掉一：只改新文件，存量放着。** 成本最低。
否的理由是**这条规矩会永远处于半生不熟**：存量 206 个文件里随便哪个被工具链踩到都要单独查一次，
而「哪些是存量、哪些是新的」没有任何人记得住。**规矩要么全仓成立，要么不叫规矩。**

**否掉二：一次性全改完再验。** 一个提交解决战斗。
否的理由是**验不动**：`UnityProject` 那 76 个要真开 Unity 验，`Config` 那 11 个牵着密钥路径，
混在一个提交里出问题时没有二分的余地。**按能不能独立验来分批**，不按大小分。

**否掉三：把中文名保留成软链接 / 别名。** 兼容存量引用。
否的理由是它把问题**加倍**：两个名字同时存在，工具链踩哪个都可能，
而且 git 与 Unity 都不适合放符号链接。

### 5. 分批表（按独立性，不按大小）

| 批 | 范围 | 怎么验 | 状态 |
|---|---|---|---|
| a | 加 `gate.pathascii` 门禁 + 配成 warn 只列不拦 | 门禁自己跑一次，列出存量 | **已完成** |
| b | `Doc/`（纯文档；`规范/` 挪到 c 批，它底下有代码要读的数据文件） | 全仓搜旧名零命中；链接逐条解析；`gate.doc` 绿 | **已完成** |
| c1 | `Config/创作管线/` → `Tools/CreationPipeline/Config/` + `.gitignore` 密钥路径 + 路径常量 | `dotnet test` + `gate.ps1` + 真跑一条要读密钥的命令；**盯死密钥那条** | **已完成** |
| c2 | `规范/` → `Specifications/` + `SpecificationPaths` | 同上 + 面板规范页真开一次 | 待 |
| c3 | `Config/Luban/` → `Tools/Luban/Config/` + `Tools/` 下的中文文件名 | 同上；**Luban 那条 gitignore 会把新目录整个吞掉，要同步放行** | 待 |
| d | `Pools/` + `_Generated/` + 指纹重算 | `pool.validate` / `gate.provision` 绿 | 待 |
| e | `UnityProject/` | **必须跑 `gate-unity.ps1`** | 待 |
| f | 门禁从 warn 改成 block | 门禁自己判红一次再改对 | 待 |

### 6. 风险与回滚

- **最危险的一步是 `.gitignore` 里那条密钥路径**：`Tools/CreationPipeline/Config/local.json` 一旦挪走
  而 gitignore 没跟上，密钥文件当场变成可入库（决策 5 要防的正是这件事）。
  **对策**：c 批里那两个改动必须同一个提交，且提交前跑 `git status` 确认
  `local.json` 仍然不出现在待提交列表里。
- **改名用 `git mv`**，让 git 认出是重命名而不是「删一个加一个」——历史才跟得住。
- **每批一个提交**，出问题 `git revert` 单批即可。
- **Unity 那批**：`.meta` 必须跟着同名文件一起挪，漏一个就是资产引用断链。
  `gate-unity.ps1` 的 `.meta` 完整性那一道正是拦这个的。

## 二、命名对照表

原则：**意译不音译**，用领域里已有的英文词；目录用大驼峰（与 `Tools/` `Pools/` 一致），
文档文件用小写连字符（与 `Backlog.md` 之后的新文件一致）。

对照表随各批落地逐步补进本文件第三节。

## 三、各批落地记录

### c1 批 · `Tools/CreationPipeline/Config/`（2026-08-21）

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

### b 批 · `Doc/` 全部改名（2026-08-21）

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

**`规范/` 从 b 批挪到 c 批**：它底下有 `基线/*.json` 与 `项目/*.json` 是**代码要读的数据**
（`SpecificationPaths` 那一族），不属于「纯文档、无代码引用」。跟 `Config/` 一起改更安全。

### a 批 · 门禁先立（2026-08-21）

- 新增 `Tools/Gates/PathAsciiChecker.cs` 与命令 `gate.pathascii`，接进 `gate.ps1`。
- 配置加两个键：`pathAsciiMode`（`warn` / `block`）与 `pathAsciiExemptPrefixes`（存量欠账名单）。
- 首次扫描结果：**存量 222 条**（含未跟踪文件；已跟踪的是 206 条）。
- **刻意先 warn**：一道新规矩默认不该把别人的构建弄红；但它照样把每一条列出来——
  看不见存量的规矩等于没有。
