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

- **测试基线锁**：新增测试文件必须登记进 `Tools/Gates/Config/test-baseline.json`，否则 `gate.baseline` 红。该目录在 `reasonix.toml` 的 deny 里，**执行后端改不了，只能由 Claude 跑更新模式**：
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
- **执行后端的沙箱把 `/tmp` 映射进仓库内的 `tmp/`。** 派活时要在任务书的「环境」节写死「不许用 `/tmp`，用 `$TEMP` / `Path.GetTempPath()`」。
- **本机是 .NET 10 preview SDK**：写盘的 `JsonSerializerOptions` 要写成 `new JsonSerializerOptions(JsonSerializerOptions.Default) { … }`，裸构造序列化 `JsonArray` 里的字符串元素会抛。
- **派活的任务书要落进仓库，命令行只给一句短指令。** 长任务书直接当命令行参数传给 `rx.py`，Reasonix 会判成 plan 模式、给子代理套上 `constraint=no-mutation`，表现成跑两分钟、`chars=0`、`exit=1`、磁盘零文件。可用写法：任务书放 `_Scratch/派活/任务-<批次>.md`，命令行写「读 <路径> 这份任务书，逐条落实，然后按它「返回什么」那节回报」。
- **`Tools/Gates/Config/` 在 `reasonix.toml` 的 deny 里**：`gate-config.json` 与 `test-baseline.json` 执行后端都改不了。派活时**别把这两个文件列进任务书的「改哪些文件」**，配置项由 Claude 自己补。
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

## 六、P5–P7 之外还剩什么

原第六节那份「规格已有但没落地」清单**已经排成 P5–P7**，见第一节。
剩下的全部是**可验范围之外的**——有环境才能做，不是排期问题。
**P8 正在把这张表逐行销掉：环境自己装，装不出来的如实留「未验证」。
计划与设计审查见 [创作管线P8计划](creation-pipeline-p8-plan.md)，新决策（80 起）见
[创作管线锁定决策P8](creation-pipeline-decisions-p8.md)，要用户填的密钥见
[创作管线要你填的](creation-pipeline-user-setup.md)。**

| 缺什么 | 卡在哪 |
|---|---|
| `bridge-feishu` / `bridge-comfyui` / `bridge-blender` / `bridge-tripo` 四个实现 | 四个下游一个都没有可用实例；按决策 23 一律不写桩 |
| 四个 driver 的「试跑绿」 | 同上——新增 driver 的四步接入，第 3 步都跑不到底 |
| 真资产、真模型、真加工报告 | 同上 |
| AI 对抗预审、执行后端评估一轮 | 要真调一次 LLM |
| 常驻 daemon 与唤醒事件源 | daemon 在门禁里没法验；事件订阅要经飞书 port |
| 冲突探测与设计池汇总的比对 | 那一半要语义判断，确定性算法做不了（P6 批次 1 只做「与存量需求比对」） |

**每一批的「已知缺口」那节是细账**，动手前先翻对应的批次日志。

---

> **交接提示：第一节的状态表已经全部「已完成」——方案定义的范围到此走完。**
> 接手时先读 [创作管线P8计划](creation-pipeline-p8-plan.md)：第六节那六项正在被 P8 逐行销掉，
> 环境（ComfyUI / Blender / LLM）自己装，真租户真花钱的那几项等用户填凭据。
>
> 两条从 P7 换来的教训，动面板或动「写在决策里但没人验过」的东西之前先看一眼：
> **决策 76**（面板 JS 的语法错，编译/测试/门禁全绿也照样一页不渲染，
> 加完页面必须真开一次看）与 **决策 79**（功能写好了但那条路上根本不会有数据经过）。
> 还有 **决策 78** 那半句：写在决策里不等于落了地——
> 「local.json 进 .gitignore」从 P1 写到 P7 才真的补上那一行。
