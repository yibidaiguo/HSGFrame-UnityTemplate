# 正式版说明 · v1（2026-08-22）

> 给「拿去跑正式环境」准备的一份账：这一版有什么、怎么起、上线前照单核对、已知的坑与边界。
> 落地过程的细账在各提交信息里（`git log`）。

## 一、这一版是什么

一个能让策划 / 美术 / 程序用 AI 自动化流程协作的 Unity 模板：

- **创作管线**：需求池 → 确认 → 方案 → 执行 → 验收 → 终审 → 归档，飞书与网页双入口，
  防跑偏机制齐备（字段所有权 / 锁定冻结 / 变更通道 / 重规划 / 人改检测 / gate.reqdoc）。
- **门禁体系**：gate.ps1 二十八段（含配置表校验、模块自述）+ gate-unity.ps1
  （资产十道 + Unity 真编译 + EditMode + .meta）+ gate-full（出包）。逐道报告落
  `_Generated/gate-report.json`，面板门禁页现读。接线由对账测试盯死——加检查器忘接线会红。
- **执行层（本版新换）**：`agent.dispatch` 直调 OpenAI 兼容 API（函数调用工具循环），
  implementer / verifier / operator / explore 四角色，任务书模板化，verifier 与 explore
  机械只读，围栏在 `Tools/AgentRunner/Config/agent-policy.json`。不再依赖任何第三方 agent CLI。
- **面板**：创作管线面板 17 页（新增「桥接包」装机页：每个编辑器与下游的本体与插件装没装、还差什么；
  配置就地改——非密钥预填当前值，密钥给空密码框，写得进读不回）
  + 日志看板 1 页 + 角色工作台（策划/美术/程序/管理四视图）+ 需求泳道看板 + 网页建需求
  （`pool.draft` 全链路）+ 需求详情页（阶段轴 / 验收标准 / 工作项）。
- **Claude Code 接入**：`.mcp.json` 注册 110+ 条命令工具；仓库本地 `/dev-cycle` 技能。

## 二、日常怎么起

| 事 | 命令 |
|---|---|
| 新机器装到能用 | `pwsh Tools/setup.ps1`（体检红项清完为止） |
| 只打开面板 | **双击 `panel.bat`**（只编两个工程、起面板、等端口应答再开浏览器；`panel.bat /skip` 用现成产物） |
| 关面板 | **双击 `panel-stop.bat`**（面板走停止文件优雅退出，超时才强杀） |
| 起面板 + 飞书助手 | `pwsh Tools/start.ps1`（影子拷贝跑服务，开着照样编译/跑门禁） |
| 全停 | `pwsh Tools/stop.ps1` |
| 改一批代码后 | `pwsh Tools/Gates/gate.ps1`；碰了 Unity 侧再跑 `pwsh Tools/Gates/gate-unity.ps1` |
| 派活给执行端 | `pwsh Tools/dispatch.ps1 -Role implementer -TaskFile <任务书>` |
| 生成新项目 | `project.create`（密钥与运行时状态不会被带走） |

## 三、上线前检查单（逐项打勾）

1. `pwsh Tools/setup.ps1` → **红 0**（黄的「未供给」项对要用的 driver 跑一次
   `bridge.provision --Driver <名>` 即绿；不用的 driver 黄着无妨）。
2. `pwsh Tools/Gates/gate.ps1` → PASS；`pwsh Tools/Gates/gate-unity.ps1` → PASS。
3. 飞书侧三件事（[creation-pipeline-user-setup.md](creation-pipeline-user-setup.md) 第三节）：
   权限已发版、机器人已进多维表格、**应用在表格上提权到「可管理」**（表格记录变更事件的唯一卡点）。
4. tripo 真出模型要在开发者门户**单独买 API credits**（网页版订阅是另一套额度）。
5. `pwsh Tools/start.ps1` 起服务，面板 `http://localhost:8766/panel` 打开，
   门禁页显示逐道结果、工作台切换正常。
6. 执行端冒烟：`pwsh Tools/dispatch.ps1 -Role explore -TaskFile <随便一个定位任务> -DryRun`
   干跑通过，再真派一单小活验回报。

## 三·一、跑起来之后

- **踩到问题写[BUG 反馈簿](Bugs.md)**：六项填全（现象 / 复现 / 期望 / 实际 / 环境 / 影响面），
  不用判断根因。要修就跟 Claude 说「修 BUG-XXXX」，走 dev-cycle 全流程。
- **想要但暂时做不了的**去[待办账本](Backlog.md)——那里每条都写清了卡在谁手里、
  动手前要先解决什么。第 1、2 条卡在你（飞书提权、tripo credits）。

## 四、已知边界（如实说）

- **创作管线还没跑过真需求**——机制全部单测 + 干跑 + 面板链路验证过，真需求端到端
  等你在正式环境第一单（建议先拿一条小需求全程走一遍再放量）。
- **多项目 / 飞书隔离（D 批）刻意未动**：表格事件订阅卡在应用提权（1069603），双 base
  的事件分裂行为要真测才能定分发形态。当前一个仓库一套服务是安全形态；
  两个仓库同开同一个飞书应用会串台，先别这么用。
- 执行端模型按 `local.json` 的 oaicompat 配置走，回报质量随模型档位波动；
  verifier / explore 只读是机械强制，implementer 的越界靠围栏 + 验收纪律双保险。
- 面板钉死 localhost（决策 18），工作台是视图过滤不是权限。
- `resource.verify` / PlayMode / 出包只在 gate-full 跑，日常两级不含。

## 五、这一版动过的核心账（细账见提交）

密钥外带堵洞（project.create/template.sync）· 提示词修理与版本哈希化 · 一键启停与影子拷贝
· MCP 通电 · 门禁报告落盘 · reasonix 全面脱钩换自有执行器 · 安装向导 · A 批门禁补线
（含基线锁三口、CI 分钟级、三条资产存量红清账）· 面板一期（看板/建需求/详情）· 角色工作台
· Unity 版本单一事实源 · 模块自述门禁与模块完成三件套 · 预审规范按 diff 裁剪。
