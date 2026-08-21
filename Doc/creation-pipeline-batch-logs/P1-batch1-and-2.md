# 创作管线 · P1 批次 1 与批次 2 日志

> 总账在 [创作管线进度](../creation-pipeline-progress.md)。本文只装这两批的落地记录与验收证据。

### 批次 1 · 池子地基 —— 已完成

**目标产物**

- `Pools/Schema/基线/`:`需求.schema.json` `工作项.schema.json` `设计记录.schema.json`
- `Pools/Schema/项目/需求.扩展.json`(空壳占位,项目层自由改)
- `Pools/` 空目录骨架:`Inbox/` `Requirements/` `专项/` `组织/` `审查意见/` `Designs/记录/` `Designs/汇总/` `Designs/定稿/`
- `Tools/CreationPipeline/`(程序集 `Toolkit.CreationPipeline`,命名空间 `Template.Toolkit.CreationPipeline`):
  `PoolPaths` `PoolSchema` `PoolSchemaLoader`(基线⊕项目合并)`RequirementValidator` `RequirementStateMachine` `SchemaExtensionValidator` `IdentifierAllocator`
- `Solutions/CreationPipeline.Tests/`:xunit 测试
- `Tools/Cli/CommandHost/Commands/PoolCommands.cs`:`pool.validate` / `req.validate` / `schema.check`

**实际落地**(与目标一致,`Pools/Schema/基线/` 三份 schema 与目录骨架由 Claude 直接落盘,其余六个文件由执行后端分四次派活完成):

`Tools/CreationPipeline/`:`PoolFinding` `PoolPaths` `PoolSchema`(四个模型类)`PoolSchemaLoader` `SchemaExtensionValidator` `RequirementValidator` `RequirementStateMachine` `IdentifierAllocator`,共 1149 行。
`Solutions/CreationPipeline.Tests/`:`PoolTestWorkspace` 夹具 + 五个测试文件,共 **32 个 `[Fact]`**。
`Tools/Cli/CommandHost/Commands/PoolCommands.cs`:三条命令。

**验收记录**(全部由 Claude 亲自复跑,不采信执行后端的自述)

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 退出码 0;新工程 32/32 通过 |
| `gate.naming`(限 `Tools/CreationPipeline`) | 问题 0 条 |
| `gate.doc` | 问题 0 条(总纲已豁免) |
| `gate.baseline --UpdateBaseline` | 新增 6 条、**零修改**——独立证明本批没动既有测试 |
| `pool.validate` / `schema.check` 打真实空池子 | 均「问题 0 条」 |
| **反向验证**:植入一个五处违规的坏需求 | 全部 5 条抓出,每条带字段级中文理由与修复建议 |

**已知偏差**:`id` 字段缺失时会同时报「缺少字段 id」与「必填字段 id 缺失」两条(校验规则第 2、3 条各自独立执行)。不影响判定,后续要去重再说。

**遗留的环境问题**(不是本批引入):仓库里有个陈旧的 git worktree `.claude/worktrees/cool-colden-bfbbf9`(分支 `claude/cool-colden-bfbbf9`,**无独有提交、无未提交改动**)。它是仓库根的一份完整副本,会被全量 `gate.ps1` 的命名/通用性/文档三道递归扫进去,凭空制造几百条违规。删掉它即可,或把 `.claude` 加进 `gate-config.json` 的 `sourceScanSkipSegments`(那个键**不支持**宿主层覆盖,只能改通用配置)。

### 批次 2 · 同步与路由 —— 已完成

**这一批真正做的是什么**:批次名叫「同步」,但锁定决策 6 说飞书 API 不可验。所以本批把入站/出站里
**确定性的、纯文件的**那部分做成了能离线跑测试的机器,需要网络的那一小段留成产物文件等批次 3 的 driver 消费。
**本批一行网络调用都没有,也没有任何 driver 契约**——那是批次 3 的活,提前写必返工。

**实际落地**(`Tools/CreationPipeline/`,分四次派活)

- **入站**:`PipelinePaths`(仓库根下 `_Tasks/` `_Generated/` 的路径)`InboxEnvelope` `InboxScanner`
  `IntakeOutcome` `RejectionNotice` `ChangeRequestJournal` `RequirementIntake`。
- **路由与出站**:`MemberDirectory` `CardRouteTable` `EpicClaimBook` `CardRouter`
  `OutboundEnvelope` `PoolPushPlanner`。
- **队列与引擎**:`ExecutionQueue` `EngineSettings` `EngineDispatchRule` `TaskState` `TaskStatusReport`。
- **命令层**:`Tools/Cli/CommandHost/Commands/PipelineFlowCommands.cs` 五条命令
  `pool.pull` / `pool.push` / `task.status` / `engine.mode` / `engine.queue`。
- **测试**:七个新测试文件,`CreationPipeline.Tests` 从 32 条涨到 **76 条**。

**本批定下的新文件格式**(后续批次按这个读写)

| 文件 | 形状要点 |
|---|---|
| `Pools/Inbox/<渠道>-<记录id>-<修订>.json` | 信封:`渠道/记录id/修订/提交人/提交时间/关联需求/字段`,`字段` 只装策划端字段 |
| `Pools/队列.json` | `{ "条目": [{需求id, 入队时间, 理由}] }`,顺序即先进先出 |
| `_Tasks/<REQ>/状态.json` | 见子文档 03 §二,原样落地 |
| `_Tasks/<REQ>/变更/<时间戳>.json` + `累积.json` | 锁定后的字段级 diff,累积文件同名字段以最新为准 |
| `_Generated/拒收/<渠道>-<记录id>-<修订>.json` | 文件名不带时间戳,重跑覆盖同一份,保证幂等 |
| `_Generated/出站/<时间戳>-<REQ>-<事件>.json` | 回写意图 + 卡片路由结果,**不发卡片,只落文件** |

**本批新增的锁定决策**(改这些要重新走设计审查)

7. **幂等靠已入池需求的 `来源.修订`,不另立账本。** `(渠道, 记录id)` 建索引,信封修订 ≤ 已入池修订即跳过。
   Inbox 文件**处理后不删**——留证据,靠幂等跳过。
8. **卡片路由的默认表是代码内建值**(`CardRouteTable.Default()`),`Pools/组织/卡片路由.json` 存在才逐键覆盖。
   这样锁定决策 4「模板仓库零池子内容」与子文档 01「默认表随基线发」两条同时成立。
9. **`提出人` 是伪职责**:先查成员表姓名,查不到退化成 `策划`,再走第②③④步。
10. **值守是默认模式**:`Config/创作管线/引擎.json` 缺失时 `EngineSettings.Load` 返回值守——
    配置读不到时最安全的行为是永不自动。`EngineDispatchRule.TryTakeNext` 在值守下无条件返回 false 且不动队列。

**验收记录**(全部由 Claude 亲自复跑,不采信执行后端的自述)

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 25 个测试工程全绿;`CreationPipeline.Tests` **76/76** |
| `gate.naming` × 3(实现 / 测试 / CommandHost) | 三处均 0 条 |
| `gate.doc` | 0 条 |
| `gate.baseline --UpdateBaseline` | 新增 7 条、**改 1 条**(只有夹具 `PoolTestWorkspace.cs`,纯追加,diff 逐行看过)——独立证明本批没动既有测试断言 |
| 五条新命令真实调用 | 全部跑通,见下 |
| **反向验证**:一好一坏两条 Inbox 记录同轮入站 | 好的入池 `REQ-0001`,坏的抓出 **5 条**问题(空标题 / 空验收标准 / 缺目标 / 缺玩法 / 下游写了工程字段 `状态`),拒收单落盘 |
| **幂等验证**:同一份 Inbox 连跑两次 | 第二次「跳过 1 条」,需求目录没长出第二个文件 |
| **路由验证**:提出人在成员表 | `待验收` 卡片命中 `Submitter`,收件人 `ou_A`,理由说清了为什么 |
| **值守验证**:`engine.queue` | 「自动派活:不可以(值守模式不自动派活,请人工跑 task.run)」 |
| 不认识的出站事件 | 失败并列出全部可用事件,不静默吞掉 |

**本批修掉的一个缺陷**(验收时发现,已修):拒收单的「位置」原本指向内部临时候选文件
(`Temp/创作管线校验-<guid>/REQ-xxxx.json`)。拒收单是要回贴给策划看的,指到一个用完即删的
临时路径毫无意义——已改成指回下游记录本身。

**已知缺口**(不阻塞,记着)

- **出站事件表没有能路由到 `美术` 的事件**(`选片` 卡片由引擎发,不走 `pool.push`),
  所以专项认领那条路径目前只有单元测试覆盖,命令层跑不到。批次 5 引擎接上后自然打通。
- **同步水位没做**(`Config/创作管线/同步水位.json`)。它要真拉取才有意义,属批次 3 的 driver。
  本批的 `pool.pull` 是从**已经躺在 Inbox 里的文件**读,不负责把文件弄进来。
- **隐式认领没落笔**(「首次处理该专项卡片 = 隐式认领」)。本批的 `CardRouter` 只做路由决策,不写盘。
- `RejectionNotice.cs` / `RequirementIntake.cs` 的 `JsonSerializerOptions` 是裸构造,
  `OutboundEnvelope.cs` 是 `new JsonSerializerOptions(JsonSerializerOptions.Default)`。
  两种写法并存,当前都不炸(测试覆盖到),但 .NET 10 preview 下裸构造序列化 `JsonArray`
  字符串元素会抛——后续统一成后者。
