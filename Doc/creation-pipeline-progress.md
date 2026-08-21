# 创作管线 · 落地总账与交接

> 上游：[策划美术工作流接入方案](design-art-workflow-proposal.md) §十二「分期」。
> 本文是**总账 + 交接书**：新会话只读这一份就能接着干；每批的落地细节在
> [批次日志](creation-pipeline-batch-logs/)，按需再翻。
> 分支：`feature/creation-pipeline-p1` · 起始提交：`5a4f603`

## 一、分期与批次

方案 §十二 只分到 P4。**P5–P7 是本仓自己定义的**，来源是原第六节那份「规格已有但没落地」
清单——按独立性拆成七批，不按大小拆；前一批全绿才进下一批。

| 期 | 批次 | 内容 | 依赖 | 状态 |
|---|---|---|---|---|
| P1 | 1 池子地基 | 两层 schema + 合并 + 需求校验器 + 状态机 + 取号 + `pool.validate` / `req.validate` / `schema.check` | — | **已完成** |
| P1 | 2 同步与路由 | `pool.pull` / `pool.push` + Inbox 幂等 + 拒收理由 + 成员表/卡片路由四步 + 队列 + 值守模式 + `task.status` | 1 | **已完成** |
| P1 | 3 供给 | `bridge.provision`：建表描述 / 专项表 / 校验错误文案 / assistant-package / 指纹 + `Bridges/feishu/driver.json` | 1 | **已完成** |
| P1 | 4 门禁 | 池子校验、扩展合法性、供给对账、下游边界、层边界五道 + 接进 `gate.ps1` | 1、3 | **已完成** |
| P1 | 5 面板骨架 | Dashboard 加总览 / 任务简版 / 需求池 / 门禁 / 引擎 五页，`POST /cmd` 白名单 | 1、2 | **已完成** |
| P1 | 6 Aily 导入 spike | 配置包能否程序化导入的验证 + 兜底路径落文档 | 3 | **已完成**（结论是「未验证」，见日志） |
| P2 | 1 资产契约 | 资产请求与溯源边车两份基线 schema + 通用文档校验器 + `art.request` / `art.validate` | P1 | **已完成** |
| P2 | 2 资产规格 | 资产规格数据三层合并 + 规格/落点缺省 + 资产规格门禁 | P2-1 | **已完成** |
| P2 | 3 生图 driver | `Bridges/comfyui/` 自述 + 配方骨架 + 依赖清单 + 能力对账（**真调用不可验**） | P2-1 | **已完成** |
| P2 | 4 选片与认领 | 选片卡片出站事件 + 专项认领入站（补 P1 批次 2 的已知缺口） | P2-1 | **已完成** |
| P2 | 5 审查与放行 | 放行策略数据 + 风险分级 + 审查包组装 | P1-2 | **已完成** |
| P2 | 6 面板加页 | 资产 / 设计池 / 供给对账三页 | P2-1、P2-3 | **已完成** |
| P3 | — | 模型链路：加工计划 + 模型机检 + blender/tripo 自述（**真调用不可验**） | P2 | **已完成** |
| P4 | 1 冲突与重规划 | 冲突三选一闭环 + 变更打断重规划（确定性那一半） | P2 | **已完成** |
| P4 | 2 调度与面板 | 轮询调度 + 单实例锁 + 意见库晋升 + 面板任务图/冲突/晋升三页 | P4-1 | **已完成** |
| P5 | 1 放行流水 | 放行入账 + 确定性抽查 + 发现问题三连（revert 计划 / 策略回落 / 记意见库） | P2-5 | **已完成** |
| P5 | 2 晋升闭环 | 提案入库 + 状态机 + 真落成检查器草案 / 预审规则 | P4-2 | **已完成** |
| P6 | 1 同步与探测 | 同步水位 + 冲突自动探测 + 裁决流水 | P1-2、P4-1 | **已完成** |
| P6 | 2 重规划落地 | 需求快照成新基准 + 回方案关 + 脏项标脏净项保留 | P4-1 | **已完成** |
| P6 | 3 冲突债务 | 验收报告列未决冲突 + 设计汇总标注 + 助手冲突提醒 | P6-1 | **已完成** |
| P7 | 1 离风格报告 | PNG 解码 + 主色聚类 + 定稿色板 + 色板距离排序 | P2-3 | **已完成** |
| P7 | 2 面板审查与规范页 | 终审队列 + 放行流水页 + 规范浏览 + 晋升提案待批 | P5-1、P5-2 | **已完成** |
| P7 | 3 面板收尾 | 下游页 + 设计池时间线/定稿预览 + 资产页离风格列 | P7-1 | **已完成** |

## 二、锁定的设计决策（改这些要重新走设计审查）

**全部条目在 [创作管线锁定决策](creation-pipeline-decisions.md)**——这份总账写到 200 行硬顶，拆出去了。
动任何设计前先读那一份；本文其余各节引用的「决策 N」都指那里的编号。

## 三、门禁现场（踩过的坑，别再踩）

- **测试基线锁**：新增测试文件必须登记进 `Tools/Gates/Config/test-baseline.json`，否则 `gate.baseline` 红。该目录在执行端的写盘拒绝清单里，**执行端改不了，只能由 Claude 跑更新模式**：
  `run gate.baseline --arguments-file <{TemplateRoot, ConfigurationPath, UpdateBaseline:true}>`
  **两个路径都要写绝对路径，且 `ConfigurationPath` 必须是 `gate-config.json` 而不是 `.host.json`**——
  写相对路径或写成 host 配置，它会「重建成功」但把基线削掉一百多条（P6 批次 1 踩过）。
- **命令的参数键一律按 CLR 属性名匹配**（`CommandRegistry` / `CommandArgumentBinder` / `CommandArgumentValidator`），
  `[JsonPropertyName]` 的中文别名会被默认值填充覆盖，做不成。决策 57 说的「中文键配 ASCII 别名」
  指的是 URL 查询串那类外部接口；`--arguments-file` 的键**本来就只认 ASCII 属性名**，别再试。
- **改动文件白名单**：`Doc/` `Pools/` `Bridges/` `_Tasks/` `_Generated/` 已补进 `gate-config.host.json`。
- **文档长度 200 行**：只有 `Doc/design-art-workflow-proposal.md`（总纲）在 `documentExemptions` 里。**其余文档一律 200 行管，本文与批次日志也是**——账本写不下就再拆一份批次日志，别去加豁免。
- **缩写黑名单**：`Mgr / Cfg / Svc / Btn / Idx / Tmp / Utils / Ctx / Param / Attr / Conf` 逐**词段**查。`Configuration` 是完整词段，不违规。
- **公开类型必须有中文 `<summary>`**，否则命名门禁红。
- **新 csproj 必须加进 `Solutions/Template.sln`**，否则十秒级门禁根本编不到它，绿灯是假绿。
- **命名门禁读不懂单行 raw string 字面量**：含中文键的 JSON 写成一行时，门禁会把中文键当标识符报红。**改成多行 raw string 就绿**，内容一个字不用动。
- **多行 raw string 里的「裸中文」照样判红**：`"标题": "值"` 这种带引号的没事，但测试里故意写坏的 JSON 若含裸中文（`这不是合法 JSON`），门禁看不出那是数据，会报「标识符含中文」。坏样本一律用 ASCII 写。
- **命令宿主的调用约定是 `run <命令名> --arguments-file <json 路径>`**，不吃行内 `--键 值`。任何要程序化调命令的地方都得先把参数落成临时 JSON。
- **服务跑着的时候 `dotnet build` 会因 DLL 被占用而失败**。跑完面板要等它退出再编译。
- **面板要 stdin 开着才不退出**（`Program.cs` 靠 `Console.ReadLine()` 挂住）。
  后台起法：`(sleep 300 | dotnet run --project Tools/Dashboard/Dashboard.csproj -- --port 8766) &`；
  管道给 `head` 会立刻 EOF，服务当场退。
- **面板的 JS 住在 C# verbatim 字符串里，引号转义是雷区**：`""` 是一个引号，
  空串分支必须写 JS 单引号 `''`。写错了整份脚本语法错、一页都不渲染，
  而编译/测试/门禁全绿（决策 76，P4 批次二埋到 P7 批次二才发现）。
  **改完面板务必真开一次看**。
- **本机是 .NET 10 preview SDK**：写盘的 `JsonSerializerOptions` 要写成 `new JsonSerializerOptions(JsonSerializerOptions.Default) { … }`，裸构造序列化 `JsonArray` 里的字符串元素会抛。
- **`Tools/Gates/Config/` 在执行端围栏（`Tools/AgentRunner/Config/agent-policy.json`）的写盘拒绝清单里**：`gate-config.json` 与 `test-baseline.json` 执行端改不了。派活时**别把这两个文件列进任务书的「改哪些文件」**，配置项由 Claude 自己补。
- **assistant-package的文件清单有三份**（`PackageFiles` / `ProspectiveFiles` / `AssistantPackageInspector`），
  加包文件时三份都要改，只改前两份会让 `gate.provision` 假绿（决策 72，P6 批次 3 踩过）。
- **`bridge.provision` 的参数名是 `Driver` 不是 `DriverName`**，写错会被参数校验拦下。
- **`gate.ps1` 的输出重定向到文件时子进程 JSON 日志会丢**（`Invoke-GateCommand` 用 `Out-Host`）。要逐道明细就单跑那条 `gate.*` 命令。
- **陈旧 worktree 已清理**（批次 3）：`.claude/worktrees/cool-colden-bfbbf9` 曾让全量 `gate.ps1` 凭空多出几百条违规。已 `git worktree remove --force`（确认过零独有提交、工作区干净、HEAD 是当前分支的祖先）。**全量门禁现在可以直接跑。**

## 四、验证矩阵

到 P2 批次 2 为止**都不碰** `UnityProject/Assets/Game/Scripts/` 与 `Packages/com.hsgframe.*/Runtime/`，所以铁律 4 的分钟级 Unity 门禁**不适用**——批次 2 只落 `Specifications/` 下的纯数据与 `Tools/` 下的引擎，资产落点是**字符串**，没有真资产进 `UnityProject/`。
**真有资产文件往 `UnityProject/Assets/` 落的那一批（最早是 P2 批次 3）起，这一行必须改成「要跑 `gate-unity.ps1`」。**

| 档 | 命令 | 每批都要 |
|---|---|---|
| 秒级 | `dotnet test Solutions/Template.sln` | ✓ |
| 十秒级 | `dotnet build Solutions/Template.sln` | ✓ |
| 门禁全量 | `pwsh Tools/Gates/gate.ps1` | ✓ |
| 命令自测 | 新增命令各跑一次真实输入，贴输出 | ✓ |
| 反向验证 | 造一个坏输入，确认它被抓出来而不是静默通过 | ✓ |

## 五、批次日志

| 批次 | 日志 |
|---|---|
| P1 批次 1、2 | [P1-batch1-and-2.md](creation-pipeline-batch-logs/P1-batch1-and-2.md) |
| P1 批次 3 供给 | [P1-batch3-provisioning.md](creation-pipeline-batch-logs/P1-batch3-provisioning.md) |
| P1 批次 4 门禁 | [P1-batch4-gates.md](creation-pipeline-batch-logs/P1-batch4-gates.md) |
| P1 批次 5 面板骨架 | [P1-batch5-dashboard-skeleton.md](creation-pipeline-batch-logs/P1-batch5-dashboard-skeleton.md) |
| P1 批次 6 Aily 导入 spike | [P1-batch6-aily-import-spike.md](creation-pipeline-batch-logs/P1-batch6-aily-import-spike.md) |
| P2 批次 1 资产契约 | [P2-batch1-asset-contract.md](creation-pipeline-batch-logs/P2-batch1-asset-contract.md) |
| P2 批次 2 资产规格 | [P2-batch2-asset-spec.md](creation-pipeline-batch-logs/P2-batch2-asset-spec.md) |
| P2 批次 3 生图 driver | [P2-batch3-image-driver.md](creation-pipeline-batch-logs/P2-batch3-image-driver.md) |
| P2 批次 4 选片与认领 | [P2-batch4-selection-and-claim.md](creation-pipeline-batch-logs/P2-batch4-selection-and-claim.md) |
| P2 批次 5 审查与放行 | [P2-batch5-review-and-release.md](creation-pipeline-batch-logs/P2-batch5-review-and-release.md) |
| P2 批次 6 面板加页 | [P2-batch6-dashboard-pages.md](creation-pipeline-batch-logs/P2-batch6-dashboard-pages.md) |
| P3 模型链路 | [P3-model-chain.md](creation-pipeline-batch-logs/P3-model-chain.md) |
| P4 批次 1 冲突与重规划 | [P4-batch1-conflict-and-replan.md](creation-pipeline-batch-logs/P4-batch1-conflict-and-replan.md) |
| P4 批次 2 调度与面板 | [P4-batch2-scheduling-and-dashboard.md](creation-pipeline-batch-logs/P4-batch2-scheduling-and-dashboard.md) |
| P5 批次 1 放行流水 | [P5-batch1-release-ledger.md](creation-pipeline-batch-logs/P5-batch1-release-ledger.md) |
| P5 批次 2 晋升闭环 | [P5-batch2-promotion-loop.md](creation-pipeline-batch-logs/P5-batch2-promotion-loop.md) |
| P6 批次 1 同步与探测 | [P6-batch1-sync-and-detect.md](creation-pipeline-batch-logs/P6-batch1-sync-and-detect.md) |
| P6 批次 2 重规划落地 | [P6-batch2-replan-landing.md](creation-pipeline-batch-logs/P6-batch2-replan-landing.md) |
| P6 批次 3 冲突债务 | [P6-batch3-conflict-debt.md](creation-pipeline-batch-logs/P6-batch3-conflict-debt.md) |
| P7 批次 1 离风格报告 | [P7-batch1-style-deviation.md](creation-pipeline-batch-logs/P7-batch1-style-deviation.md) |
| P7 批次 2 面板审查与规范页 | [P7-batch2-dashboard-review-and-specs.md](creation-pipeline-batch-logs/P7-batch2-dashboard-review-and-specs.md) |
| P7 批次 3 面板收尾 | [P7-batch3-dashboard-finish.md](creation-pipeline-batch-logs/P7-batch3-dashboard-finish.md) |
| 需求文档同步 A 批 需求目录化 | [REQDOC-batchA-requirement-directory.md](creation-pipeline-batch-logs/REQDOC-batchA-requirement-directory.md) |
| 需求文档同步 B 批 规范与渲染 | [REQDOC-batchB-doc-spec-and-render.md](creation-pipeline-batch-logs/REQDOC-batchB-doc-spec-and-render.md) |

> P8 各批的日志索引在 [创作管线P8计划](creation-pipeline-p8-plan.md) 末尾那张表里。
> 「需求文档与任务同步」这件事的设计审查见
> [主文档](requirement-doc-and-task-sync-design.md) 与 [形状细节](requirement-doc-and-task-sync-shapes.md)。

## 六、P8 之后：那六项现在各是什么状态

原来这一节是一张「缺什么 / 卡在哪」的表，六行全是「没有可用实例，验不了」。
**P8 把环境装出来之后，逐行重写如下。判据只有一条：有没有真调用的真回执。**

| 原来那一行 | 现在 | 凭据 |
|---|---|---|
| 四个 driver 实现 | **五个都写了**（多一个 `oaicompat`） | 各自的批次日志 |
| driver 的「试跑绿」 | **四绿一未点**：blender / comfyui / feishu / oaicompat 真跑过；tripo 只干跑（真跑要花积分） | [批次 13](creation-pipeline-batch-logs/P8-batch13-gap-sweep.md) |
| 真资产 | **真出图**：文生图 3 张、图生图 1 张，都是真 PNG 真尺寸 | [批次 4](creation-pipeline-batch-logs/P8-batch4-comfyui-generation.md)、[批次 14](creation-pipeline-batch-logs/P8-batch14-img2img.md) |
| 真模型 | **没有**——tripo 代码通了，卡在账号 API 积分（不是代码问题） | [批次 10](creation-pipeline-batch-logs/P8-batch10-tripo-v3.md) |
| 真加工报告 | **真加工过**：Blender 真跑，产物能再被读回来 | [批次 3](creation-pipeline-batch-logs/P8-batch3-protocol-and-blender.md) |
| AI 对抗预审、执行后端评估 | **都真调过 LLM**，产的是报告不是判定（决策 89） | [批次 5](creation-pipeline-batch-logs/P8-batch5-backend-and-prereview.md)、[批次 5b](creation-pipeline-batch-logs/P8-batch5b-semantic-eval.md) |
| 常驻 daemon 与唤醒事件源 | **daemon 有限轮可验**；文件唤醒源通了，**飞书消息事件真唤醒过一次** | [批次 1](creation-pipeline-batch-logs/P8-batch1-daemon-and-wake.md)、[批次 9](creation-pipeline-batch-logs/P8-batch9-feishu-longconn-wake.md) |
| 语义冲突比对 | **真调过**，产报告 | [批次 5b](creation-pipeline-batch-logs/P8-batch5b-semantic-eval.md) |

**另外多做了一件当初没在表上的**：助手 B 形态（常驻会话）。
飞书机器人现在**真会回话**：收消息 → 执行后端 → 现场跑 `req.validate` → 回话 →
写下游草稿 → 投唤醒信号叫醒引擎，整条链真跑通过（[批次 12](creation-pipeline-batch-logs/P8-batch12-assistant-serve.md)）。

## 七、还卡着的事

**全部收进[待办账本](Backlog.md)**，一处维护——那里每条都写清了为什么值得做、影响面、
以及卡在谁手里（飞书提权、tripo credits、Aily 导入能力未知、两处白名单策略、资产名归一化器）。
本节不再抄一份，抄了必分叉。

**面板像素级观感那条已销**：2026-08-22 用浏览器真开验证过看板 / 详情 / 工作台三处。

**每一批的「已知缺口」那节是细账**，动手前先翻对应的批次日志。

---

> **2026-08-21 全量体检**：提示词修理、一键启停（影子拷贝，服务开着门禁照跑）、
> `.mcp.json` 通电、门禁报告落盘（面板门禁页不再恒空）、密钥外带堵洞。
> 已落地清单与待做批次见 [全量体检与优化方案](optimization-plan-2026-08.md)。
>
> **2026-08-22 正式版 v1**：A–G 批次全落地（门禁补线 / 面板一期 / 角色工作台 / 安装向导 /
> 版本事实源 / 沉淀机制 / 预审裁剪），执行层换成自有的 `agent.dispatch`。
> 怎么起与上线检查单见 [正式版说明](release-notes-v1.md)；实际项目的问题写 [BUG 反馈簿](Bugs.md)。
>
> **交接提示：P1–P8 全部走完，第六节那六项已逐行销账（销不掉的写清卡在谁手里）。**
> 接手时按这个顺序读：本文第六节（现状）→ [待办账本](Backlog.md)（还卡着什么）→
> [创作管线P8计划](creation-pipeline-p8-plan.md) 的批次表 → 对应的批次日志。
> **路径去中文化那笔待办也做完了**（`gate.pathascii` 现在是 block 模式），
> 设计审查见 [路径去中文化](ascii-path-migration.md)。
>
> 两条从 P7 换来的教训，动面板或动「写在决策里但没人验过」的东西之前先看一眼：
> **决策 76**（面板 JS 的语法错，编译/测试/门禁全绿也照样一页不渲染，
> 加完页面必须真开一次看）与 **决策 79**（功能写好了但那条路上根本不会有数据经过）。
> 还有 **决策 78** 那半句：写在决策里不等于落了地——
> 「local.json 进 .gitignore」从 P1 写到 P7 才真的补上那一行。
