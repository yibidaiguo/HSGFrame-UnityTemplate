# P8 批次 3 · driver 调用协议与 blender 真加工

> 上游：[创作管线P8计划](../creation-pipeline-p8-plan.md)。
> 销的是第六节那张表里「四个 driver 的实现」「四个 driver 的试跑绿」「真加工报告」
> 三行里**属于 blender 的那一份**。

## 一、这批把什么翻成了可验

至今**一个 driver 实现都没有**：加工计划（`art.plan`）算得出来没人执行，
模型机检（`art.modelcheck`）要的那份指标文件没人产。
Blender 4.2.23 装到 `D:/Tools/Blender` 之后，这条链路第一次真跑通了。

**地基跟着先就绪的 driver 落地。** 调用协议原本排在 comfyui 那批，
但 Blender 400M 早装完，ComfyUI 的 CUDA 版 torch 还在下——地基与具体 driver 无关，
谁先到谁带。

## 二、调用协议（后面每个 driver 都照这个形状）

子进程，**stdin 收一份 JSON，stdout 出一份 JSON**，退出码 0/非 0。

- 请求：`{"契约版本","port","动作","配置","载荷"}`
- 成功：`{"契约版本","成功":true,"载荷":{…}}`
- 失败：`{"契约版本","成功":false,"错误":{"错误码","人话","可重试"}}`

**stdout 上只许有那一份 JSON，日志全走 stderr。** 这条不是洁癖：
Blender 自己往 stdout 狂打东西，`BlenderRunner` 必须把它当数据读进来解析，
一旦漏到本进程 stdout，表现就是「JSON 解析失败」这种查不到根因的错。
脚本靠约定前缀行 `BRIDGE_RESULT <json>` 回传结果；**找不到那一行就是失败**
（错误码 `加工站没回结果`），绝不返回一个空指标。

| 文件 | 内容 |
|---|---|
| `Tools/CreationPipeline/BridgeCallEnvelope.cs` | 信封三件套 + 可读的解析失败原因 |
| `Tools/CreationPipeline/BridgeRouteTable.cs` | 读 `Config/创作管线/下游.json`；**不存在与坏掉是两支** |
| `Tools/CreationPipeline/LocalBridgeSettings.cs` | 读 `本机.json`；密钥值只从 out 参数出去 |
| `Tools/CreationPipeline/BridgeInvoker.cs` | 起子进程、关 stdin、异步读、超时必杀 |
| `Config/创作管线/下游.json` | 域路由 + 实现表（进 git，零密钥） |
| `Bridges/blender/src/BridgeBlender/` | driver 实现（决策 84：落 `Bridges/`，不落 `Tools/`） |
| `Bridges/blender/scripts/{probe,process}.py` | 能力探测 + 八步加工 |
| `Tools/Cli/CommandHost/Commands/BridgeCommands.cs` | `bridge.probe` / `bridge.process` |

## 三、验证结果——真跑，不是「测试绿了」

输入是 **Khronos 官方样本 `Suzanne.gltf`**（含 `.bin` 与两张 1024 贴图）。

| 步 | 结果 |
|---|---|
| `bridge.probe --Driver blender` | 真写出探测输出（**Claude 独立复跑过一次**，拿到同样的文件） |
| `art.caps --Driver blender` | 「能力对账通过（依赖 1 项，全部满足）」 |
| `art.plan` | 八步：启用 7、禁用 1（烘法线，原因「基线未开启」） |
| `bridge.process` | 真跑 Blender，**面数 3936 → 2700**（计划要求 ≤3000），产出 gltf 2078 B + bin 275400 B + 两张贴图 |
| `art.modelcheck` | 「模型机检通过（五项全过）」 |
| **反向验证** | 可执行文件指到不存在的路径 → 报「下游不可达」，**且确认没写出空文件** |
| 秒级 / 十秒级 / 门禁全量 | `dotnet test` 全绿、`dotnet build` 0 错 0 警、`gate.ps1` **PASS 全绿** |

**Claude 另外做了一次执行端没做的验证**：把产出的 `prop_suzanne.gltf`
**重新导回 Blender**，读出「面数 2700、材质数 1、对象名 prop_suzanne」——
产物是真能再被读的模型，命名那一步也真生效了，不是一个写坏的壳。

**本批不碰 `UnityProject/Assets/Game/Scripts/` 与 `Packages/com.hsgframe.*/Runtime/`，
所以铁律 4 的分钟级 Unity 门禁不适用。**

## 四、执行端自己撞出来的两个真实坑（都已修进脚本）

这两条任务书里没写，是 Blender 的真实行为，记下来免得下一个人再踩：

1. **`--factory-startup` 的场景自带默认 Cube、相机与灯光。**
   第一次加工把 Cube 一起导出了，材质数变 2、包围盒被撑成 y=2.0/z=2.0。
   `process.py` 现在导入前先清空场景对象。
2. **`DECIMATE` 修改器的 `ratio` 是近似值，接近 1 时可能减不动。**
   ratio=3000/3936≈0.762 应用后得 3012（还超上限），第二轮 ratio≈0.996 完全无效。
   现在按 10% 余量迭代下探，直到真的 ≤ 目标面数。

## 五、验收时 Claude 自己动手改的

- **`driver.json` 的「能力探测」字段原本写的是 `blender-caps` / `comfyui-caps`，
  那两个命令根本不存在。** 真命令是 `bridge.probe --Driver <名>`。
  面板下游页与 `art.caps` 找不到探测文件时的提示都直接摆这个字段，
  摆一条不存在的命令等于把人往沟里带。两份 driver.json 都改成真命令。
- 清掉验收残留：`_Tasks/REQ-0042/`（demo 资产请求与加工计划）与几个探针脚本。
  **模板仓库 `_Tasks/` 下一个文件都不该进 git**（`git ls-files _Tasks` 是空的），
  这与决策 4「模板仓库零池子内容」同源——生成出来的新项目不该自带别人的任务残渣。

## 六、已知缺口

- **输入不是 tripo 出的粗模**，是公开样本。模型生成那一环还没通（P8-6，等密钥）。
  「加工站行不行」与「粗模从哪来」是两件事，但**别把这条当成模型生成链路已通**。
- **烘法线那一步没真跑过**——加工计划里它是禁用的（基线未按资产类型开启）。
  八步里真执行的是 7 步。
- **`process.py` 的 UV 步骤做的是「保底展开」**，不是按规格数据挑 UV 通道。
  规格数据里目前也没有那个字段。
- **`gate.model` 扫的是仓库里的模型资产，本批产物落在 `_Scratch/`（gitignore 里）**，
  所以门禁没真扫过它。哪天真资产进仓库，那道门禁才第一次有活干。
