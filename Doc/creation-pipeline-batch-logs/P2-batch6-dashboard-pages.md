# 创作管线 · P2 批次 6「面板加页」日志

> 总账在 [创作管线进度](../creation-pipeline-progress.md)。本文只装这一批的落地记录与验收证据。
> 这一批做完 **P2 收尾**。

## 这一批真正做的是什么

面板从五页加到八页：**资产 / 设计池 / 供给对账**。三页全是只读消费方，
数据一律走管线已有的公开 API（`AssetSpecCatalog` / `AssetPaths` / `ProvisionReconciler` /
`DependencyManifest` / `RecipeDefinition`），**不另写一份读取逻辑**（锁定决策 21）。

## Claude 在验收时抓到的一个假绿（本批最要紧的一条）

`ReadProvision` 原本这么写：对账整体抛异常时把 `findings` 置为 null，
逐行计数时跳过分摊。**但状态判定只看 `hasFingerprint`**：

```
if (!hasFingerprint)      → 未跑
else if (findingCount==0) → 一致      ← 这里
else                      → 失配
```

于是「有指纹 + 对账整体崩了」会落进第二支，被判成 **`一致`**——
**把崩掉的对账说成对上了**。它自己的注释写的是「全部行按未跑」，代码没做到。

这正是锁定决策 20（门禁报告不存在时报「未跑」，不是绿也不是红）要防的那类假绿，
只是换了个地方冒出来。已由 Claude 单立一个 `reconcileRan` 标志修掉：

```
if (!reconcileRan || !hasFingerprint) → 未跑
```

并补了一条回归测试 `FingerprintPresentButReconcileFailedIsStillUntested`。
**这条测试是先验过真能抓住 Bug 才留下的**：把修复临时改回去，它立刻红
（`Expected: "未跑" / Actual: "一致"`），改回来即绿。

教训记进锁定决策 42：**「没有 finding」和「没跑对账」是两件事，不许合并成一个分支。**

## 落地

**`CreationPanelReader`**（只加不改）：`PanelAssetRow` / `PanelDesignRow` / `PanelProvisionRow`
三个行模型 + `ReadAssets` / `ReadDesigns` / `ReadProvision` 三个方法。

- **资产页**：扫 `_Tasks/*/资产请求/*.json`，变体合格判定与选片一致
  （顶层图片文件且有同名边车）；坏的资产请求**不产行**。
- **设计池页**：扫 `Designs/` 下 `定稿` / `汇总` / `记录` 三类；
  **坏文件照样产一行**且 `IsReadable` 为 false——设计池页要让人看见「这里有个坏文件」，
  静默吞掉才是骗人。这一点与资产页相反，是刻意的：资产请求坏了说明它还没成形，
  设计池文件坏了说明有东西烂在库里。
- **供给对账页**：driver 名从 `Bridges/` 扫（**代码里零 driver 字面量**），
  自述加载失败记 `自述损坏` 不抛。

**`CreationPanelPage`** 加三个标签页与渲染；对账状态上色 `一致`绿 / `失配`红 / **`未跑`灰**。
**`DashboardServer`** 加三条路由。`/cmd` 白名单与绑定地址一个字没动。

## 本批新增的锁定决策

42. **「零 finding」与「对账没跑成」必须分成两个分支。** 合并成一个会把崩掉的对账
    判成「一致」——这是决策 20 那类假绿的变种。凡是「没查出问题」的判定，
    都要先确认「查过了」。
43. **坏数据在资产页不产行、在设计池页产行。** 资产请求坏了说明它还没成形，
    不该出现在资产清单里；设计池文件坏了说明有东西烂在库里，必须让人看见。

## 验收记录（全部由 Claude 亲自复跑，不采信执行后端的自述）

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 全绿；`Dashboard.Tests` **58/58**（40 → 58，含 Claude 补的那条回归） |
| `pwsh Tools/Gates/gate.ps1` 全量 | **PASS**（二十一道） |
| `gate.baseline --UpdateBaseline` | 新增 3 条、零修改 |
| driver 名泄漏 | `grep -rinE "feishu\|comfyui" Tools/Dashboard --include=*.cs` **零输出** |
| `git diff --stat Tools/CreationPipeline` | **零输出**——面板确实没去改管线 |

**面板真跑**（`--port 8792`，Claude 亲自起服务 + curl 三个接口）：

| 接口 | 返回 |
|---|---|
| `/api/panel/assets` | `[]`——模板零池子内容，符合决策 4 |
| `/api/panel/designs` | `[]`——同上 |
| `/api/panel/provision` | 两行真数据：**comfyui 未供给 / 未跑 / 依赖清单 true / 配方 1**；**feishu 已供给 / 一致 / 依赖清单 false / 配方 0** |
| `/panel` 页面 | 三个新标签 `资产` `设计池` `供给对账` 都在 |

`feishu` 那行是 `一致` 而不是 `未跑`，说明**对账在真仓库里确实跑成了**——
上面那处修复没有把正常路径也误判成未跑。

## 执行后端这一轮报上来的三件事

1. **`ProvisionReconcileReport` 没有「哪些 driver 已供给」的名单**，只有计数，
   任务书写的「报告的 driver 在已供给计数里」落不了字面。它改用逐行查指纹文件存在性，
   口径与 `ProvisionReconciler` 内部 `File.Exists(fingerprintPath)` 逐字一致。**这个改动是对的。**
2. **`Reconcile` 会抛**（第一行就 `PoolSchemaLoader.Load`，测试环境必然缺基线 schema），
   任务书三条规则没覆盖这个情形。它加了 try-catch 并**主动请派活方确认**——
   方向是对的（往「未跑」那边靠），但实现漏了一半，见上面那条假绿。
3. **序数序断言写反过一次**（「乙」U+4E59 < 「甲」U+7532），改断言不改实现后转绿。
   这是本批唯一一次真实红→绿，它如实说了三个读取器是纯新增、没有先写失败实现的对象。

## 已知缺口（不阻塞，记着）

- **资产页的「离风格报告」没做**（子文档 04 说资产页要有「按需求/定稿筛选、离风格报告」）。
  离风格报告要算色板距离，得先有真资产与定稿数据。
- **设计池页没有「记录时间线」与「定稿预览（色块 + 参考图）」**，只有平铺列表。
- **三页都没有筛选与分页。** 模板仓库零池子内容，现在三页有两页是空的，
  筛选做了也验不了。
- **`ReadProvision` 吞掉的异常没有把原因带到界面上。** 现在只报「未跑」，
  不报「为什么没跑」。往安全那边靠了（不会假绿），但诊断性差一档——
  要补就得给 `PanelProvisionRow` 加一个原因字段。
- **`art.` 命令族仍不在 `/cmd` 白名单里**（决策 19 的六族不含它），所以资产页点不了
  `art.select`。这是既有约束。
