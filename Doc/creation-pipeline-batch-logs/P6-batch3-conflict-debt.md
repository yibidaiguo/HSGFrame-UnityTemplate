# 创作管线 · P6 批次 3「冲突债务」日志

> 总账在 [创作管线进度](../creation-pipeline-progress.md)。本文只装这一批的落地记录与验收证据。

## 这一批真正做的是什么

冲突挂了账之后，**账只有 `conflict.list` 一个出口**——审查的人看不到、助手不知道、
设计汇总里也没标。挂账等于挂给了空气。

总方案 §三 要求三个出口：验收报告必列未决冲突、设计汇总把冲突区域标红、
助手据冲突列表提醒后续涉区需求。这一批把三个都铺上。

## 未决的判据只许有一套

`ConflictDebtView` 的未决判据**逐字复用 `ConflictList.PendingCount()`**
（状态不是「已裁决」，或选择是「强制推送」）。
另写一套会让门禁说 3 条、审查包说 2 条——同一个账本两个数字，比没有数字更糟。

`ConflictDebtReport.Scanned` 与「零未决」是两个字段：**`LoadFailureReason` 非空时
即便 `Entries` 有内容也算没扫成**。列表残缺就不能拿它下「无冲突」的结论（决策 42）。

## 三个出口都不加闸

未决冲突**不改放行结论、不拦执行**（决策 51：冲突不拦执行）。
本批只把账摆到人眼前，一道新闸都不加。审查包第六节渲染分三支：
`（未查：原因）` / `本需求无未决冲突（池子里共 N 条）` / 逐条列出加合计——
**「未查」永远不会渲染成「无未决冲突」**。

## 验收时抓到的一个假绿（本批最重要的一条）

assistant-package加第七个文件 `知识/conflicts.md` 之后，`gate.provision` **仍然是绿的**，
而 git 里那份 `_Generated/Bridges/feishu/` 根本没有这个文件。

原因：助手包的文件清单**有三份**，不是两份——

| 清单 | 谁在用 | 本批同步了吗 |
|---|---|---|
| `AssistantPackageBuilder.PackageFiles` | `Build` 真写哪些文件 | ✅ |
| `AssistantPackageBuilder.ProspectiveFiles` | `BridgeProvisioner` 报产物 | ✅ |
| **`AssistantPackageInspector` 自己那份** | **`gate.provision` 查「产物齐全」** | ❌ |

执行后端的接线测试断言的是前两份相等，所以它看不见第三份掉队了。
决策 22 的「产物齐全」那一半**在这一批一度失效**——门禁明明该红却是绿的。

**修了两处**：给检查器清单补上第七个文件；补一条测试
`PackageInspectorKnowsEveryFileBuildWrites` 断言 **Build 写的每个文件检查器都认得**。
补完之后重跑 `bridge.provision` 重生成产物（`_Generated/Bridges/` 进 git，决策 12）。

**这条测试真能抓**：把检查器那行删掉，它当场红
（`Assert.Contains() Failure: Item not found in collection`），加回去就绿。

## 落地

**引擎**：`ConflictDebtView`（纯计算，全文无 `File.` / `Directory.`）；
`ReviewPackageBuilder` 加第六节（`ReviewPackageInput` 新参数只加在末尾，
现有参数顺序与名字一字未动）；`AssistantPackageBuilder` 加第七个包文件、
设计汇总顶部冲突区域标注、系统提示「必须遵守」加第 4 条；
`AssistantPackageInspector` 补第七个文件（**验收时补的，见上一节**）。

**命令**：`task.release` 加可选 `PoolRoot`，输出追加一行未决冲突数。

**产物**：重跑了 `bridge.provision --Driver feishu`，
`_Generated/Bridges/feishu/` 从 10 份产物变 11 份。

**门禁**：不新增。

## 本批新增的锁定决策

71 – 72 见 [创作管线锁定决策](../creation-pipeline-decisions.md)：
未决判据只许一套、助手包三份清单必须同步。

## 验收记录（全部由 Claude 亲自复跑，不采信执行后端的自述）

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 全绿；`CreationPipeline.Tests` **388/388**（368 → 388） |
| `pwsh Tools/Gates/gate.ps1` 全量 | **PASS** |
| `gate.provision` 单跑 | 补检查器前**绿（假绿）** → 补后**红，点名缺 `conflicts.md`** → 重跑供给后**绿** |
| `gate.baseline --UpdateBaseline` | 新增 2 条、**修改 3 条**，与它自述改的既有测试**逐个对得上**，无未申报改动 |
| 生产调用点核查 | `ReviewPackageBuilder` 全仓**只有定义处**，它说的是真的 |

**三个出口逐个真看过产物**：

- **助手包**：`知识/conflicts.md` 真的生成了，零未决时写「当前没有未决冲突。」
- **系统提示**：「必须遵守」第 4 条是任务书那句**逐字原文**。
- **设计汇总**：零未决时**不插**冲突区域那一段（对的，没有账就不该标红）。
- **审查包第六节**：三支分支的代码逐行读过，`（未查）` 与「无未决冲突」是两条互斥路径。

## 执行后端这一轮报上来的

1. **`task.release` 没有「组 `ReviewPackageInput` 的地方」**——**它是对的，我核过**：
   `ReviewPackageBuilder` 在生产代码里**零调用方**，全仓只有定义处与测试引用。
   任务书假设 P2 批次 5 已经把审查包接进命令，实际没有。
   它**没有自己造一个审查包组装点**（那要给 `task.release` 加需求 id、提交清单一堆参数），
   只落地了能诚实落地的部分，并把差异写进第 5、6 节。判断正确。
2. **没写红灯证据，改用 mutation 验证**：删掉「强制推送兜底」判据后测试红 1 条。
   如实说了是「实现先行、后补测试」，没假装有红灯。
3. **改了 3 个表外文件**（`BridgeProvisioner.cs` 与两份既有测试），全是签名连坐适配，
   逐个登记了。核过：`BridgeProvisioner.cs` 只动了一行实参。

## 改了哪些既有测试（逐条看过）

| 文件 | 改了什么 | 判断 |
|---|---|---|
| `AssistantPackageBuilderTests.cs` | `BuildWritesAllSixFiles` → `…SevenFiles`，`Assert.Equal(6→7)`；5 处补实参 | 加第七个文件后产物数必然是 7，不改这条恒红。**是规格变了，不是把红的改绿。** |
| `ReviewPackageBuilderTests.cs` | 5 处补第 8 个实参 `null` | 纯签名适配，**断言一个字没动** |
| `AssistantPackageInspectorTests.cs` | `Assert.Equal(10→11)` 三处 | **Claude 改的**，检查器清单加一项的必然结果 |

## 已知缺口（不阻塞，记着）

- **审查包在生产代码里没有调用方。** `ReviewPackageBuilder` 与它的第六节都写好了，
  但**没有任何命令会去组装一份审查包**——「验收报告必列未决冲突」目前只做到
  「渲染器会列」，没做到「真有报告被产出来」。这是 P2 批次 5 留的坑，不是本批的。
  要接上得给 `task.release` 补需求 id、变更路径、提交清单一堆输入，另算一批。
- **`task.release` 报的是全池未决数**，不是本需求的——该参数类没有需求 id 字段。
- **语义比对仍然不做**：本批只搬运已经挂在账上的冲突，不判断「这条新需求
  语义上是否跟旧设计矛盾」（总账第六节早记着）。
- **设计汇总的冲突标注只标 id，不标「哪一段」。** 真要把冲突区域在汇总正文里标红，
  得知道冲突落在汇总的哪一节——那要语义判断。
