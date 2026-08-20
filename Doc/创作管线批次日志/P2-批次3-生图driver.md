# 创作管线 · P2 批次 3「生图 driver」日志

> 总账在 [创作管线进度](../创作管线进度.md)。本文只装这一批的落地记录与验收证据。

## 这一批真正做的是什么

**不是「接通 ComfyUI」。** 没有可用的 ComfyUI 实例，真调用不在可验范围——
这与锁定决策 6 对飞书 API 的处置是同一条线。

真正做的是让「加一个本地形态的生图 driver」这件事在仓库里**有契约、有能力对账、有门禁**，
且引擎侧零 driver 特判。子文档 05 §七 那「新增 driver 的固定四步」要能走通，
前提就是这三样先存在。

## 落地

**数据**（`Bridges/comfyui/`）

- `driver.json`：port `生图`、形态 `本地`、密钥字段空、契约版本 `>=1.0 <2.0`。
  `字段类型映射` 是**空对象**——那是需求编辑端的概念，生图 driver 用不上，
  但 `BridgeDriverDescriptor.Load` 把它列进了必填字段，所以键在、值空。
- `依赖清单.json`：两条（Impact-Pack 节点 + SDXL 底模）。**大文件刻意不进仓库，清单进**。
- `配方/图标@v5/`：`workflow.json`（五节点骨架）+ `映射.json`（五条字段映射 + 一个锚点槽 + 依赖引用）。

**引擎**（`Tools/CreationPipeline/`，六个新类）

`RecipePaths` / `RecipeDefinition` / `DependencyManifest` / `CapabilityProbeResult` /
`CapabilityReconciler` / `RecipeInspector`。

**命令与门禁**

- `art.caps`：能力对账。只对**本地形态** driver 有意义，线上形态直接拒。
- `gate.recipe`：配方静态门禁，接进 `gate.ps1` 成为**第二十道**。

## 关键设计取舍

**能力探测的输出从文件读**（`ProbeResultPath`），不去起真探测器。
这让对账逻辑完全可单测，将来接真探测只是换一个输入源。
探测输出文件不存在时的失败文案会**从 driver 自述里读 `能力探测` 的值**报出来
（「跑 `comfyui-caps` 生成探测输出后再对账」）——命令名不写进代码。

**否掉的两个替代方案**：

1. 写一个 `bridge-comfyui` 子进程实现走 stdin/stdout JSON。否掉的理由是锁定决策 23——
   没环境可跑，写出来就是一段谁都没验过、却长得像「已接入」的代码。
2. 把能力对账并进 `gate.provision`。否掉的理由：供给对账管「下游表建了没」，
   能力对账管「本地依赖装了没」，失败原因和处置完全不同，混一道门禁报错文案说不清该干什么。

## 本批新增的锁定决策

30. **`Bridges/<名>/driver.json` 的 `字段类型映射` 对非需求编辑端 driver 是空对象**，
    键必须在（加载器必填），值空。不为此放宽加载器的必填清单——那会让真正缺字段的
    坏自述混过去。
31. **能力对账的探测输出走文件，不走真调用。** 版本**不比对**（只查「在不在」）：
    探测出的版本与清单不一致目前不报，因为没有真跑过的样本来定「差多少算不兼容」。

## 验收记录（全部由 Claude 亲自复跑，不采信执行后端的自述）

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 全绿；`CreationPipeline.Tests` **180/180**（156 → 180） |
| `pwsh Tools/Gates/gate.ps1` 全量 | **PASS**（二十道） |
| `gate.baseline --UpdateBaseline` | 新增 2 条、零修改 |
| driver 名泄漏检查 | `grep -rin comfyui Tools/CreationPipeline Tools/Cli Tools/Dashboard --include=*.cs` **零输出** |
| 真调用检查 | `grep -rn "HttpClient\|System.Net.Http\|Process.Start" Tools/CreationPipeline` **零输出** |
| `gate.recipe` 干净状态 | 「配方门禁（driver 2 个，配方 1 个）通过，问题 0 条」 |
| `art.caps` 干净状态 | 「能力对账通过（依赖 2 项，全部满足）」 |
| `git diff --stat` | 197 行**全是新增，零删除**——无顺手改的无关模块 |

**反向验证**（Claude 亲手造违规再撤销，两组）：

| 造的违规 | 门禁反应 |
|---|---|
| 映射里加一条指向不存在节点 `99` 的 `预算.调用上限`，依赖数组加一个未登记的名字 | `gate.recipe` 失败 **3** 条：节点不存在 / 请求字段不在白名单 / 依赖不在清单，三类各中一条 |
| 探测输出里抽掉 SDXL 底模 | `art.caps` 失败，文案同时给出**缺什么**（名称+类别）、**来源**（HuggingFace URL）、**怎么装**（清单无安装命令时的兜底句） |

## 执行后端这一轮的三件事

1. **命名门禁红了 6 条，是我验收时抓到的、不是它自查抓到的。**
   `CapabilityReconcilerTests.cs` 里两处**单行** raw string 含中文 JSON 键，
   命名门禁把中文键当标识符报红——这个坑总账第三节早就记了，任务书也抄进了红线，
   它仍然踩了。它在回流第 5 节**如实说明了自己没跑命名门禁**（依赖 `gate.ps1`，
   而基线锁需要派活方先补），所以这不算瞒报，算覆盖不到。
   已由 Claude 改成多行 raw string，**断言一个字没动**，因此不触发铁律 3 的
   「测试变更」单独提交。
2. **`BridgeDriverDescriptor` 没有「能力探测」属性**，而任务书又禁止改它。
   它在 `ArtCommands.cs` 内部用 `JsonDocument` 直接读那个键，绕开了冲突并如实报了上来。
   处置是对的。
3. **自纠过一次 driver 名泄漏**：XML 注释里的示例名「ComfyUI-Impact-Pack」含 `comfyui`
   子串（边界检查大小写不敏感），它自己 grep 到并改成「Impact-Pack」。

## 已知缺口（不阻塞，记着）

- **`bridge-comfyui` 实现不存在。** driver 自述里的 `实现` 字段指向一个还没有的东西。
  这是刻意的（决策 23），但意味着**四步接入的第 3 步「试跑绿」目前跑不到底**——
  `art.caps` 能跑，真 `generate` 不能。
- **配方的 `workflow.json` 是骨架，没在任何 ComfyUI 实例上跑过。** 真接上之后
  八成要整份换掉；映射与锚点槽的**形状**是这一批的产出，workflow 内容不是。
- **能力探测器 `comfyui-caps` 不存在。** 探测输出目前只能人手写。
- **版本不比对。** 见决策 31。
- **配方与资产类型的对应关系没有门禁。** `映射.json` 里有 `资产类型` 字段，
  但没人查「图标类型的资产请求是不是真的走了 `图标@v5` 配方」——那要等 `art.generate`。
