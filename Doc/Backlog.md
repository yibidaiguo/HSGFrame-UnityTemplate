# 待办账本

> 已经想清楚、但**刻意不现在做**的事。每条要写足三样：**为什么值得做、影响面多大、
> 动手前要先解决什么**。没有这三样的条目不许进这份表——那种叫愿望，不叫待办。

## 1. 文件名与目录名去中文化（只保留注释与数据里的中文）

**提出人**：用户，2026-08-21。
**状态**：待设计审查。**不许顺手开工**——它跟决策 1 冲突，见下。

### 为什么值得做

跨平台与工具链上，中文路径是一类**低频但很贵**的故障源：
git 在不同 `core.quotepath` 设置下显示不一致、CI 容器的 locale 不是 UTF-8 时路径会烂、
某些 .NET / MSBuild / Unity 的路径处理在非 ASCII 下有历史坑、
命令行里还要额外操心引号。本仓已经踩过一个近亲：
**`gate.ps1` 的输出重定向到文件时子进程 JSON 日志会丢**（编码相关）。

### 影响面（2026-08-21 实测）

**197 / 1240 个已跟踪文件**带中文名，**24 个中文目录**：

| 顶层 | 文件数 | 备注 |
|---|---|---|
| `UnityProject` | 76 | 最大的一块。Unity 靠 GUID 引用，`.meta` 跟着一起挪就安全，但要真开 Unity 验 |
| `Doc` | 39 | 互相有大量中文文件名的交叉链接，改名要同步改链接 |
| `Pools` | 14 | 池子目录名进了代码常量（`PoolPaths` 那一族） |
| `Config` | 11 | 含 `Tools/CreationPipeline/Config/`，还进了 `.gitignore` |
| `_Generated` | 11 | 产物，重生成即可；但**指纹要重算**（决策 13） |
| `Tools` | 9 | |
| `规范` | 8 | 目录名本身就是中文，且写死在 CLAUDE.md 与多份规范文档里 |
| 其余 | 29 | `Pipelines` / `Solutions` / `Bridges` / `Levels` / `提案` / `getting-started.md` 等 |

### 动手前必须先解决的

1. **决策 1 要改。** 它现在写的是「C# 一律落 ASCII 目录……中文只许出现在纯数据目录
   （`Pools/Epics/`）与**文件名**里」。新规矩要把「与文件名」那半句删掉，
   并把适用范围从「含 .cs 的目录」扩到**全仓**。**改决策要重走设计审查。**
2. **路径常量是硬编码的中文字面量**：`PoolPaths` / `PipelinePaths` / `ProvisionPaths` /
   `RecipePaths` / `SpecificationPaths` / `AssetPaths` 都在 `Path.Combine` 里拼中文段。
   改名 = 同时改这些常量，漏一个就是运行时才炸的路径错。
3. **门禁配置里也有路径**：`gate-config.json` / `gate-config.host.json` 的
   `changedPathWhitelist` 等。那两个文件在 `reasonix.toml` 的 deny 里，**只能 Claude 改**。
4. **`.gitignore` 里有中文路径**（`Tools/CreationPipeline/Config/local.json` 等），漏改会让密钥文件
   突然不再被忽略——**这一条最危险**，改名那一刻必须同步。
5. **UnityProject 那 76 个要真开 Unity 验**（铁律 4）：改的是资产树，
   十秒级门禁一行 Unity 代码都不编，绿灯是假绿。
6. **改完要加一道门禁拦回潮**，否则下次又混进来。
   现有 `directoryNamePattern` 只作用于**含 `.cs` 的目录**，管不到 `Doc/` 与 `Pools/`。

### 分批建议（按独立性，不按大小）

| 批 | 范围 | 能独立验吗 |
|---|---|---|
| a | 加门禁规则 + 把它配成**只警告不拦**，先把存量列出来 | 能 |
| b | `Doc/` + `Specifications/`（纯文档，无代码引用） | 能 |
| c | `Config/` + `.gitignore` + 门禁配置 | 能，但要盯死密钥那条 |
| d | `Pools/` + `_Generated/` + 路径常量 + 指纹重算 | 能 |
| e | `UnityProject/` | **要跑 `gate-unity.ps1`** |
| f | 门禁规则从警告改成拦 | 能 |

### 从现在起的临时规矩

**新建的文件与目录一律用 ASCII 名**，别再往这 197 个里加。
本文件（`Doc/Backlog.md`）就是按新规矩起的名。
存量的改名等设计审查过了再统一动。

## 3. `bridge-tripo` 整个对着旧版 API 写的，要翻到 v3 —— **已完成**

**提出人**：Claude 自查发现，2026-08-21（用户给了官方文档链接才查出来）。
**状态**：**已完成，2026-08-21**。落地与实证见
[P8 批次 10](creation-pipeline-batch-logs/P8-batch10-tripo-v3.md) 与
[endpoints-verified.md](../Bridges/tripo/endpoints-verified.md)；
教训已立成[决策 94](creation-pipeline-decisions-p8.md)。
**下面这些是当时的分析，留着当账**——真出模型仍卡在账号 API 积分（见第 4 条），
那一条不是代码问题。

### 怎么发现的

用户充值后 API 仍报 `2010 积分不足`，去查官方文档才发现**主机和大版本都不对**：

| | 我们在用的 | 文档现在的 |
|---|---|---|
| 主机 | `api.tripo3d.ai` | **`openapi.tripo3d.ai`** |
| 版本 | `/v2/openapi` | **`/v3`** |
| 提交 | `POST /task` | `POST /v3/generation/text-to-model` |
| 查任务 | `GET /task/{id}` | `GET /v3/tasks/{id}` |
| 查余额 | `GET /user/balance` | `GET /v3/account/balance` |

**根因是 Claude 自己**：`Tools/CreationPipeline/Config/local.example.json` 里那个 v2 的 base URL
是 Claude 凭印象写的，执行端照着任务书建的桥。
**两份配置文件已改成 v3**，但**桥的代码还没翻**。

### 教训

**外部 API 的 base URL 与端点形状，不许凭印象写进任务书。**
本仓已经为「不许凭印象」立过好几条规矩（决策 31 不编版本号、决策 88 依赖没下完不算绿、
决策 91 只有真跑才算数），这次是同一个毛病换了个地方犯：
**任务书里那几行「我照文档写的，你要拿真回包核对」写了，但 base URL 本身没让核。**
下次写下游任务书，**第一条要核的就是 base URL**。

### 动手时要做的

1. `Bridges/tripo/src/BridgeTripo/TripoClient.cs` 全部端点翻 v3。
2. 请求体：`{"prompt": …, "model": "v3.1-20260211"}`。
   **`model` 必填且只认这四个**：`P1-20260311` / `v2.5-20250123` /
   `v3.0-20250812` / `v3.1-20260211`。
   **官方快速开始那页自己写错了**——它另一处写 `tripo-v3.1`，服务端当场拒（实测）。
3. 余额接口换 `/v3/account/balance`（回 `{"data":{"balance":0.00,"frozen":0.00}}`）。
4. 错误码语义要重核：v3 的 `1004` 是参数非法、`2010` 仍是积分不足、`4001` 是端点不存在。
5. **翻完必须真跑一次**才算数（决策 91），而真跑要账号有 **API credits**——见第 4 条。

## 4. tripo 的 API 额度要单独买（不是待办，是给用户的说明）

实测三个事实：

- 网页控制台显示有积分（先是 200 免费分，后来用户又充了值）；
- `GET /v2/openapi/user/balance` 与 `GET /v3/account/balance` **都回 0**；
- 真提交任务两个版本都回 `2010 You don't have enough credit`。

**结论：Tripo Studio（网页版）的订阅/积分与 API credits 是两套额度。**
要跑通模型生成 port，必须在开发者门户的定价/计费页**单独购买 API credits**。

这也解释了最早那 200 免费积分为什么用不了——与决策 91 记的是同一件事。

## 2. `Config/` 分层：工具配置归位（**与第 1 条合并成一批做**）

**提出人**：用户，2026-08-21。**布局已定**（用户选的方案），**时机已定**：与第 1 条合批。
**状态**：待设计审查（改决策 2 要走）。

### 问题

`Config/` 根下五个目录是**三种不同的东西**，混在同一层：

| 目录 | 真实性质 | 谁改 |
|---|---|---|
| `Tables` `Schema` `Mirror` | 业务数据 | 策划，天天改 |
| `Luban` | 表格工具链本身 | 程序，很少改 |
| `创作管线` | 机器与下游配置 | 装机的人，一次性 |

而结构规范总纲 §四的顶层地图把 `Config/` 定义成
**「数据与定义（单一事实源侧）」**——机器配置本来就不该在里面。

**决策 2 当年撞见过这件事**，用「开个子目录」绕过去了：
「`Config/` 已被 Luban 配置表占满，下游配置混进去两套语义会打架」。
现在是还那笔账。

### 定下来的布局：跟既有惯例走

仓库里**早有一套惯例**，只是 `创作管线` 破了它——**4 个工具都把配置放在自己目录下**：
`Tools/Gates/Config/`、`Tools/AssetPipeline/Config/`、`Tools/CodeGen/Config/`、
`Tools/Indexing/Config/`。

所以：

- `Config/` **只留业务数据**（`Tables/` `Schema/` `Mirror/`），回到总纲给它的定义。
- `Tools/CreationPipeline/Config/` → **`Tools/CreationPipeline/Config/`**（顺带去中文，与第 1 条同批）。
- `Tools/Luban/Config/` → **`Tools/Luban/Config/`**。

不发明新概念，新工具以后也知道该放哪。

### 动手前必须先解决的

1. **决策 2 要改**（它现在写的是「配置落 `Tools/CreationPipeline/Config/`」）。
2. **`.gitignore` 里那条密钥路径必须同一个提交改掉**——
   `Tools/CreationPipeline/Config/local.json` 一旦挪走而 gitignore 没跟上，
   密钥文件当场变成可入库。**这是全部两条待办里最危险的一步。**
3. `Tools/Gates/Config/` 的 `changedPathWhitelist` 有 `Config/` 前缀，要跟着改。
   那两个配置文件在 `reasonix.toml` 的 deny 里，**只能 Claude 改**。
4. 路径常量：`PipelinePaths` / `ProvisionPaths` 等拼 `Tools/CreationPipeline/Config` 的地方。
5. `Tools/Luban/Config/` 挪动要确认 `luban.conf` 里的相对路径与 `gen.sh` 跟着走。
6. **改完补一条门禁**：`Config/` 下只许出现业务数据目录，
   出现工具链配置即红——否则下次又混回去。

### 与第 1 条的关系

**两件事都要挪 `Tools/CreationPipeline/Config/`、都要改 `.gitignore` 的密钥路径、都要改路径常量与门禁配置。**
分两次做等于把同一批危险改动做两遍，**必须合批**。
合批后第 1 条分批表里的 c 批（`Config/` + `.gitignore` + 门禁配置）
就是这一条的落点。
