# 全量体检与优化方案 · 2026-08-21

> 起因：用户要求全量看一遍并优化（提示词 / 面板交互 / 需求跑偏 / 结构 / 测试 /
> skill 沉淀 / 多项目 / 角色分面板 / 一键启动 / 流程卡片）。
> 本文分两半：**已落地的**（当天做完，带提交号）与**待做批次**（要过设计审查再动手）。

## 一、已落地（2026-08-21，本分支）

| 事 | 提交 |
|---|---|
| 堵密钥外带：`project.create` / `template.sync` 跳过 `local.json`、水位文件与根级运行时目录 | cd92870 + c13507b |
| 提示词修理：三组装器版本改内容哈希、信封归一 `PromptEnvelope`、配置包「四个文件」实为五个、schema 表只列要填字段、降级轮不再要求读不到的表、合并产物目录统一 ASCII 名 | d2862a7 |
| 一键启停 `Tools/start.ps1` / `stop.ps1`：影子拷贝跑服务，**开着服务照样编译/跑门禁**；面板加 `--stop-file` 常驻模式 | c78ea43 |
| `.mcp.json`：命令层 108 条命令接进 Claude Code（MCP 服务此前建好没通电） | c78ea43 |
| 门禁报告落盘：`gate.ps1` 写 `_Generated/gate-report.json`，面板门禁页恒空的账销掉；临时参数文件加进程号防多项目互踩 | 3262013 |
| dev-cycle 技能瘦身：模板不再复述角色档案（7 节 vs 9 节打架消除）、主文件三处重复合并、补 base URL 教训 | 母仓 df2b730 |
| 文档纠偏：user-setup 过期路径与 tripo v2 地址、Jenkinsfile「七道」与 `流水线定义.json` 旧名、getting-started 的 PackagePrefix 幽灵参数 | 本批 |

**探查修正一条**：`explore` 角色档案并不缺，当时装在用户级目录里，探查只看了一处。
（2026-08-21 追加：执行层已整体换成仓库自有的 `agent.dispatch`，四个角色档案收进
`Tools/AgentRunner/Roles/`，dev-cycle 技能收进 `.claude/skills/dev-cycle/`。）

## 一·二、2026-08-22 落地批（正式版冲刺）

| 批 | 状态 | 凭据 |
|---|---|---|
| A 门禁补线 | **完成**：资产十道接进 gate-unity、配置表校验进 gate.ps1、接线对账测试、基线锁三口全堵（执行端实现）、CI 夜间/发布补分钟级、三条资产存量红清账 | gate.ps1 27 段 PASS + gate-unity PASS |
| 安装向导（新增需求） | **完成**：setup.init / setup.check / Tools/setup.ps1，密钥文件保护三查（ignore 覆盖 / 未被跟踪 / 键在不在），本仓实测红 0 黄 4 绿 3 | 提交在案 |
| B 面板一期 | **完成**：泳道看板、建需求表单（pool.draft 全链路真跑：REQ-0001 入池又清账）、详情页（阶段轴/验收标准/工作项）、/cmd 结构化通道、抽查弹窗换按钮 | 浏览器真开验证 |
| C 角色工作台 | **完成**：策划/美术/程序/管理四工作台过滤侧栏（分视图不分权限），全景随时可切回 | 浏览器验证 |
| E 版本事实源 | **完成**：unity-cmd.ps1 / pack-hotfix.ps1 按 ProjectVersion.txt 推编辑器路径，换版本只改一处 | gate-unity 真跑 |
| F 沉淀机制 | **完成**：模块自述门禁 gate.modulereadme（执行端实现）、三件套入 dev-cycle 验收清单、预审规则文件名 ASCII 化；**事实修正**：晋升落地器早已存在（task.promotion.decide 落地 → 检查器草案 / Specifications/Project/prereview-rules.json），当初「spec.promote 零实现」指的只是命令名 | 提交在案 |
| G 预审裁剪 | **完成**：spec-relevance.json 裁剪表 + diff 路径解析，没进表的规范始终带上、解析不出回退全量、N/M 说明进提示词 | 执行端实现 + 冒烟 |
| D 多项目/飞书隔离 | **未动，卡在用户**：表格事件订阅要先把应用提权到「可管理」（1069603），双 base 事件分裂也要真测才能定分发形态——决策 91：没真跑过的不写 | — |

## 二、待做批次（每批独立可验，动手前过设计审查）

### A. 门禁补线（测试体系最大的洞）

**为什么**：11 条 `asset.*` + `config.validate` + `index.check` + `art.validate`
全有实现有单测，但**不在任何 gate 脚本里**；加检查器忘接线是静默的。
CI 四条 Jenkins 流水线没有一条跑 EditMode/PlayMode——铁律 4 说的「假绿」恰是 CI 仅有的两级。
**做什么**：1) 门禁清单对账测试（gate.ps1 的调用集 = 注册的 gate.* 命令全集，
仿 `McpToolCoverageTests`）；2) 资产族门禁接进 `gate-unity.ps1`（资产检查本就属分钟级）；
3) nightly 流水线补调 `gate-unity.ps1`；4) 基线锁三个口子：子目录 glob 逃逸、
Unity 侧 8 个测试文件不在锁内、csproj 摘出 sln 后基线照绿（需「sln 引用对账」检查）。
**先决**：资产族对当前仓库的存量红先清点；`gate-config` 在执行端写盘拒绝清单里，由 Claude 改。
顺带修一处根错位：门禁报告写在模板根 `_Generated/`、面板按仓库根读——独立仓库下重合，
模板作宿主子目录时会恒显「未跑」（执行端首跑发现的）。

### B. 面板改造一期：从「能看」到「能干活」

**为什么**：任务详情有接口无前端（阶段轴/责任视图/日志跟随整节缺席）、审查页只读、
需求在网页上完全建不了；操作人拼命令行导致名字带空格即拒。
**做什么**：1) 任务详情页接 `/api/panel/task` + 阶段轴；2) 需求录入表单
（走 `req.validate` → Inbox，与飞书入口同一条校验链）；3) 流程卡片视图：
一条需求一张卡，按「提出→确认→方案→执行→验收→终审→归档」泳道摆（用户点名要的形态）；
4) 抽查/裁决从 `window.prompt` 换成表单。
**先决**：决策 76 的教训——每页改完真开一次看；前后端中文键契约先补一条对账测试
（读 Reader 的 `JsonPropertyName` 集合 vs panel.js 里的 `行['…']` 引用，正则级即可）。

### C. 角色分面板（先分视图，不做鉴权）

**为什么**：十六页对谁都一样，策划找不到自己的三件事。决策 18 钉死 localhost，
鉴权本无必要——要分的是**视图**不是权限。
**做什么**：入口按职责分四个工作台：策划（需求池/任务/冲突/选片）、美术（资产/设计池/
离风格）、程序（门禁/供给/引擎）、管理（终审/放行/晋升/规范）；页头按 `Pools/Organization/成员.json`
的职责选默认工作台，全量视图保留。**先决**：B 批的卡片视图先行，否则分完还是表格的搬家。

### D. 多项目隔离与飞书一应用多项目

**为什么**：整条管线只有 `repositoryRoot` 一个隔离维度；飞书事件无项目路由，
两仓同开必串台（同 App 两条长连接分裂投递、固定表名「需求」撞表）。
**做什么**：1) `local.json` 加「项目标识」；2) 旁路按 base/app_token 过滤事件，
不属于本仓库的落盘直接丢并记一行；3) 约定「一项目一 base」，供给时把项目标识写进
表描述，pull/push 校验对账；4) 事件源改「一 App 一旁路进程 + 按 base 分发到多仓库」
的中枢形态（远期）；5) 知识空间标识已预留（wiki 同步链路未接，接的时候按项目分节点）。
**先决**：先在飞书侧建第二张 base 实测事件分裂的真实行为，再定分发形态。

### E. Unity 版本单一事实源

**为什么**：`ProjectVersion.txt` 是钉子，但 `unity-cmd.ps1` 与 `pack-hotfix.ps1`
各抄了一份 exe 全路径；换版本要动多处 + 14 个 package.json + 6 处文档。
**做什么**：1) `unity-cmd.ps1` 从 `ProjectVersion.txt` 读版本，按 Hub 默认装机路径
（`D:/Unity/Editor/<版本>` / `C:/Program Files/Unity/Hub/Editor/<版本>`）探测 exe，
探不到再报错让人指路径；2) `pack-hotfix.ps1` 改调同一函数。
**先决**：14 个 package.json 的次版本号另算（UPM 兼容声明，跟包走不跟机器走，不动）。

### F. 模块 skill 沉淀机制（做完编辑器就长出配套工具）

**为什么**：`spec-author` / `spec.promote` 在子文档 07 写全了，代码零实现；
`Specifications/Business/` 空置；模块 README 规范无门禁；`Index/` 建好没人消费。
**做什么**：1) 模块 README 门禁（`Modules/<模块>/README.md` 存在 + ≤40 行，规范早写了）；
2) `index.check` 接进门禁、面板加索引状态行——让 Index 活起来；3) 定「模块完成定义」：
一个模块收尾时必须产出 README + `Specifications/Business/<模块>/`（有则）+
`.claude/skills/<模块>/SKILL.md`（怎么用这个模块的编辑器/命令，触发词写清），
由 dev-cycle 验收清单把关；4) `spec.promote` 按子文档 07 落地（晋升链路的最后一环）。
**先决**：3) 需要用户认可「skill 落仓库 `.claude/skills/`」这个位置（Claude Code 会自动发现）。

### G. 提示词二期：预审的 34KB 规范灌入

**为什么**：预审每次无条件读整棵 `Specifications/`（34KB ≈ 8–11k token）；
设计里的「生效规范合并产物」从未实现（一期已把死路径修活成 `_Generated/EffectiveSpecifications`）。
**做什么**：二选一过设计审查：a) 实现合并器（三层就近合并出精简产物，幂等门禁跟上——
设计文档本来的路）；b) 按 diff 路径裁剪规范文件（映射表要立成决策）。推荐 a，b 可叠加。

## 三、顺序建议

A（补线，防住后面所有批次）→ B（面板一期）→ C（角色工作台）→ F（沉淀机制）
→ E（版本事实源，随时可插）→ G（提示词二期）→ D（多项目，动面最大放最后）。
