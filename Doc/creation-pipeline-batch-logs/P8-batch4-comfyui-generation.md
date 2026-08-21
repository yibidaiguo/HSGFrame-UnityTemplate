# P8 批次 4 · comfyui：真探测、真出图

> 上游：[创作管线P8计划](../creation-pipeline-p8-plan.md)。
> 销的是第六节那张表里「bridge-comfyui 实现」「试跑绿」「真资产」三行。

## 一、落了什么

`Bridges/comfyui/配方/图标@v5/workflow.json` 原本是**骨架**：五个节点只有
`类型` 与 `参数`，没有任何连线，也缺 KSampler / VAEDecode / 负面提示，
翻译不出一张能跑的图。按决策 87 补了 `连线` 键与三个新节点（`6`/`7`/`8`），
**节点 2/3/4/5 的语义一个字没动**——`映射.json` 与 `gate.recipe` 靠它们对上。

| 文件 | 内容 |
|---|---|
| `Bridges/comfyui/src/BridgeComfyui/ComfyClient.cs` | `/object_info` 探测、`POST /prompt`、轮询 `/history/{id}`、`/view` 下载图字节 |
| `Bridges/comfyui/src/BridgeComfyui/WorkflowTranslator.cs` | **纯函数**：中文骨架 + 连线 → ComfyUI API 形状 |
| `Bridges/comfyui/src/BridgeComfyui/Program.cs` | `caps` / `generate` 两个动作 |
| `Tools/Cli/CommandHost/Commands/BridgeCommands.cs` | `bridge.generate` |

## 二、验证结果——真图，不是「测试绿了」

| 步 | 结果 |
|---|---|
| `bridge.probe` | 节点 2 项、模型 1 项。节点里有 `ComfyUI-Impact-Pack`，模型里有 `sd_xl_base_1.0.safetensors` |
| `art.caps` | 「能力对账通过（依赖 2 项，全部满足）」 |
| `bridge.generate` | **真出 3 张 PNG**，各 84–97 KB，**真 256×256**（执行端另用 Python 读 IHDR 交叉验证过） |
| 溯源边车 | 3 份，含 driver / 配方 / 随机种 / 提示词 / 文件哈希 / prompt_id |
| 反向验证 | 关掉 ComfyUI 后 `bridge.probe` 报「下游不可达」，**且确认没写出探测文件** |
| 门禁全量 | `gate.ps1` **PASS 全绿**（配方门禁也绿） |

**探测结果里多一项 `websocket_image_save`**——那是 ComfyUI 自带、但住在
`custom_nodes/` 下的内置节点。多报不影响对账（判据是「依赖 ⊆ 探测结果」）。

## 三、装机时撞到的一条，已写成决策 88

底模下到 **0.14G / 6.9G** 时，`/object_info/CheckpointLoaderSimple` **已经把它列进候选**
——因为那个接口只是列目录文件名。这会让 `art.caps` 理直气壮报
「依赖 2 项，全部满足」，然后真出图炸在模型损坏上。
决策 31 定死「只查在不在、不比版本」，**这个洞它天生堵不住**。
规矩只能放在流程上：**装依赖没报完成之前，`art.caps` 的绿不算验收凭据**。

**验完整不能只看文件大小。** 第一次等待用 6.6e9 字节当阈值就误报过一次完成
（PowerShell 按 GiB 显示 6.23，而目标是 6,938,040,682 字节 = 6.46 GiB，当时还差 233 MB）。
最后是读 safetensors 的头（前 8 字节是头长度，后面那段要能解析成 JSON 张量表）才确认的。

## 四、决策 26 的前提是空的（本批最重要的发现）

决策 26 的原话：**「变体本体 gitignore，边车全部进 git（变体可由边车重生成）」。**

边车确实记全了：随机种、提示词、变体序号、配方名、prompt_id。
三张图共用一个种子（batch 出图），靠 `变体序号` 区分。

**但 `bridge.generate` 没有种子参数**——参数只有
`Driver` / `RequestPath` / `RecipeName` / `OutputDirectory` / `RepositoryRoot` / `TimeoutSeconds`。
**没有任何一条路能把边车里的种子喂回去。**

所以「变体可由边车重生成」这句话，至今**只是写在决策里，没有落地**：
边车是回执，不是配方。变体被 gitignore 掉是基于一个做不到的承诺——
真删了一张变体，它就是没了。

这正是本仓反复警告的那类失败（决策 79：写在决策里不等于落了地；
决策 76/79 那两条都是同一个毛病）。

### 批次 4b 把它补上了，而且验成了

`bridge.generate` 加了 `Seed` 参数（**用 string 不用 long**——种子是 64 位无符号量，
有符号整数会在边界上悄悄变号，而边车里存的本来就是字符串）。
给了种子就用它、**不许加任何偏移**（加偏移会让重生成永远对不上）。

真跑对比：不传种子出一批 → 记下边车里的种子 → **换个输出目录、回喂那个种子再出一批**。

| | 第 1 张 | 第 2 张 | 第 3 张 |
|---|---|---|---|
| 结果 | ✅ 逐字节一致 | ✅ 逐字节一致 | ✅ 逐字节一致 |

**Claude 独立复算过这三对 SHA256**，不是只看执行端那张表。
还多查了一步排除「批次 2 是拷贝的」：两批的 `prompt_id` 不同
（`2eb459e9…` vs `85d80f1b…`）、相隔 13 秒，**确是两次真提交**。
（文件名都从 `00001` 重来，是因为桥走 `/view` 下载后按自己的规则落盘，
不跟 ComfyUI 的磁盘计数器——一开始看着像拷贝，查了 prompt_id 才排除。）

**结论：决策 26 的「变体可由边车重生成」现在是真的了。**
边车从回执变成了配方，变体继续 gitignore 才站得住脚。

**任务书当时是按「两种结果都有价值」写的**——万一哈希对不上，
那就说明即使有种子这条链路也做不到逐字节复现（cudnn autotune 一类），
决策 26 就得降级。红线里写死了「对不上就如实报，严禁调参数去凑一致」。
结果是好的那一种。

## 五、已知缺口

- **重生成路径没有**（第四节）。补上之前，别把变体当「随时可再生」。
- **参考图锚点槽（节点 `5` LoadImage）没接进主链**。没给参考图时它是孤立节点，
  ComfyUI 不执行未被引用的节点，所以不影响出图；但**图生图那条路等于没通**。
- **只有一个配方 `图标@v5`**，其余资产类型的配方还没有。
- **变体没进 `_Tasks/<需求>/30-产物/`** 的正式落点，这批落在验证目录里就删了。
  真跑业务流程时要走 `AssetPaths` 那套。
