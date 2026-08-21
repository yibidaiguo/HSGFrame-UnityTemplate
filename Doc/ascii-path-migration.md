# 路径去中文化 · 设计审查与迁移账本

> 上游：[待办归档](backlog-archive.md) A 条（文件名与目录名去中文化）与 B 条（`Config/` 分层）。
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
| 其余 | 29 | `Pipelines` / `Solutions` / `Bridges` / `Levels` / `提案` / `getting-started.md` 等 |

### 3. 两条锁定决策要改

**决策 1** 原文：「C# 一律落 ASCII 目录……中文只许出现在纯数据目录（`Pools/Epics/`）与**文件名**里」。
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
| b | `Doc/`（纯文档；`Specifications/` 挪到 c 批，它底下有代码要读的数据文件） | 全仓搜旧名零命中；链接逐条解析；`gate.doc` 绿 | **已完成** |
| c1 | `Config/创作管线/` → `Tools/CreationPipeline/Config/` + `.gitignore` 密钥路径 + 路径常量 | `dotnet test` + `gate.ps1` + 真跑一条要读密钥的命令；**盯死密钥那条** | **已完成** |
| c2 | `Specifications/` → `Specifications/` + `SpecificationPaths` | 同上 + 面板规范页真开一次 | 待 |
| c3 | `Tools/Luban/Config/` → `Tools/Luban/Config/` + `Tools/` 下的中文文件名 | 同上；**Luban 那条 gitignore 会把新目录整个吞掉，要同步放行** | 待 |
| d1 | `Pools/` + `_Generated/` + `Bridges/` | `pool.validate` / `gate.provision` 绿 + 真跑重生成 | **已完成** |
| d2a | 文档、CI 定义、样例图、空目录占位（无语义耦合） | `dotnet test` + `gate.ps1` | **已完成** |
| d2b | `Config/{Tables,Schema,Mirror}` 表名三件套 + `Index/` 输出名 | 同上 + `codegen.run` 重生成一致 + 真跑一次 Luban | **已完成** |
| d2c | `Levels/` + `UI/` + `Packages/` 里的资产名 | **`gate-unity.ps1` 跑过并 PASS** | **已完成** |
| e | `UnityProject/` + `_Tasks/` 运行时路径 | **`gate-unity.ps1` 跑过并 PASS** | **已完成** |
| f | 门禁从 warn 改成 block | 反向验证过：造一个中文名当场判红，删掉复绿 | **已完成** |

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

**逐批的对照表与验收细节一批一份，索引在 [各批落地记录](ascii-path-migration-log.md)。**
这一份只放设计审查与分批表——两样东西的读者不一样：
审查是「为什么这么改」，落地记录是「改了什么、怎么验的」，
而且落地记录会一直长，跟审查绑在一份里迟早撞上 200 行硬顶（真撞了一次）。
