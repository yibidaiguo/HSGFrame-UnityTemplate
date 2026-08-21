# 创作管线 P8 · 把「不可验」翻成「已验」

> 上游：[创作管线进度](creation-pipeline-progress.md) 第六节那张「缺什么 / 卡在哪」表。
> P8 不是补功能，是**把环境装出来，让那六项能被真跑验证**。
> 装不出来的（要真租户、要真花钱的）如实留在「未验证」，**不许拿桩冒充**（决策 23）。

## 一、设计审查（六项）

### 1. 真正要解决的问题

第六节六项不是「忘了写」，是决策 23 刻意不写——没有真下游实例，写出来的都是骗人的。
所以本轮的核心动作是**装环境**，代码是装完之后的自然产物。
判据只有一条：**每一项的验收凭据必须是真调用的真回执，不是「测试绿了」。**

### 2. 影响面

- 新增：`Bridges/<名>/src/` 下的 driver 实现（C# 控制台工程，CLI 子进程）、
  `Tools/CreationPipeline/Config/{本机,下游}.json`、`Tools/CreationPipeline/` 的调用侧基础设施。
- 改动：`Solutions/Template.sln`（新工程）、面板下游页、`Doc/`。
- **不碰** `UnityProject/Assets/Game/Scripts/` 与 `Packages/com.hsgframe.*/Runtime/`：
  真资产落 `Designs/` 与 `_Tasks/`，**刻意不进 `UnityProject/Assets/`**。
  所以铁律 4 的分钟级 Unity 门禁仍不适用——这一条是**本期的选择**，
  哪一批真把资产往 Unity 资产树里放，那一批必须改跑 `gate-unity.ps1`。

### 3. 红线检查

| 条 | 结论 |
|---|---|
| 决策 5 / 78 密钥不入库 | `.gitignore` 已有 `Tools/CreationPipeline/Config/local.json`，核实过 ✓ |
| 决策 78 只判键在不在 | 所以**不许预生成空值键**——空串会让面板显示「已配」。模板叫 `local.example.json` |
| 决策 23 不写桩 | 装好才写；飞书/tripo 没凭据前一个字不写 |
| 决策 17 下游边界不破例 | driver 名是全仓禁止串，扫描根是 `Tools/{CreationPipeline,Cli,Dashboard}`。**实现落 `Bridges/<名>/src/`，在扫描根之外** |
| 决策 31 探测走文件 | 保留。真探测器**产出那个文件**，对账侧一个字不改 |
| 决策 1 C# 落 ASCII 目录 | `Bridges/comfyui|blender|tripo|feishu|oaicompat/` 全 ASCII ✓ |
| 铁律 3 改测试单独提交 | 照办 |
| 门禁现场 | 新 csproj 进 sln；新测试文件的 `test-baseline` 登记由 Claude 跑，执行后端改不了 |

### 4. 方案与被否掉的替代

**选定**：每个 driver 一个 C# 控制台工程，落 `Bridges/<名>/src/`，
按子文档 05 §一的协议跑——stdin 收 JSON、stdout 出 JSON、退出码 0/非 0，
错误格式 `{错误码, 人话, 可重试}`。可执行的解析走 `Tools/CreationPipeline/Config/downstream.json`。

**否掉一：driver 用 Python 写。** 贴 ComfyUI 生态、写起来短。
否的理由是**验证体系**：项目全部门禁是 `dotnet test` + `gate.ps1`，
Python 实现进不了秒级门禁、测试基线锁也管不到它，等于在管线正中间开一块没门禁的地。
代价是 ComfyUI 的调用要自己写 HTTP——可接受，`/prompt` + 轮询 `/history/{id}`
不需要 websocket。

**否掉二：真调用直接写进 `Tools/CreationPipeline/`。** 省一层进程。
否的理由是下游边界门禁会当场判红，而决策 17 说这道门禁的价值全在于没有例外；
再者那样 port 抽象就没了，加 driver 要改引擎。

### 5. 验证矩阵

每批都要：`dotnet test` / `dotnet build` / `pwsh Tools/Gates/gate.ps1` /
命令自测贴真输出 / 反向验证造一个坏输入。

**本期额外两条，缺一不算完成：**

- **真调用批次必须贴真产物**——路径 + 字节数 + （图给尺寸、模型给面数）。
  「测试绿了」不算验收凭据。
- **动面板的批次必须真开一次面板看**（决策 76、79）。

### 6. 任务拆分（按独立性，不按大小）

| 批 | 内容 | 依赖 | 状态 |
|---|---|---|---|
| P8-1 | daemon 循环外壳 + 文件唤醒事件源 | 无 | **已完成** |
| P8-2 | 环境装机（ComfyUI / SDXL / Impact-Pack / Blender 4.2） | 无 | 进行中 |
| P8-3 | driver 调用协议基础设施 + `bridge-blender` + 真加工 + 机检真跑 | P8-2 的 Blender 那一半 | **已完成** |
| P8-4 | `bridge-comfyui` + 真能力探测 + 真变体 | P8-2、P8-3 | **已完成** |
| P8-4b | 补重生成路径：`bridge.generate` 收种子，让决策 26 的前提成真 | P8-4 | **已完成** |
| P8-5 | `bridge-oaicompat`（执行后端 port）+ AI 对抗预审 | P8-3、用户填 key | **已完成** |
| P8-5b | 执行后端评估一轮（影响映射判脏净）+ 语义冲突比对 | P8-5 | **已完成** |
| P8-6 | `bridge-tripo`：代码通 ✅ / 真出模型 ❌ 卡账号积分 | P8-3、用户填 key | **部分完成** |
| P8-7 | `bridge-feishu`：真发卡片 ✅ / 真建表 ✅ | P8-3、用户建应用 | **已完成** |
| P8-7b | `pull`/`push` 真读真写飞书记录 | P8-7 | **已完成** |
| P8-8 | 面板下游页收尾（真开一次看） | P8-4、P8-7 | **已完成** |
| P8-9 | 飞书长连接旁路：消息事件 ✅ / 表格记录变更事件 ❌ 卡 1069603 | P8-1、P8-7 | **部分完成** |
| P8-10 | `bridge-tripo` 翻 v3（待办 3）：端点全部真回包核过 | P8-6 | **已完成** |
| P8-11 | 面板全面重做：十六页按职责分六组、总览换 KPI 与图、正文挪出 C# 字符串 | P8-8 | **已完成**（另一会话，未提交） |
| P8-12 | 助手 B 形态 `serve`：常驻会话真回话、现场校验、真写草稿 | P8-7、P8-9 | **已完成** |
| P8-13 | 补漏一批：各批次日志「已知缺口」里能自己做完的全部 | 各批 | **已完成** |
| P8-14 | 图生图接线 + 变体正式落点（补 P8-4 的缺口） | P8-4 | **已完成** |
| P8-15 | 总账：把进度文档第六节按实际结果重写 | 全部 | 待 |

P8-1 与 P8-2 互不依赖、也不依赖别的，所以并行起跑。
P8-3 起串行——它定下的调用协议形状是后面四批共用的，拆开派会拿到四套不一致的实现。

**协议基础设施原本挂在 comfyui 那批，中途换到了 blender 那批。**
理由是装机进度：Blender 400M 早就装完并真跑通了 `--background --python`，
而 ComfyUI 的 CUDA 版 torch 还在下。地基跟着**先就绪的那个** driver 落地，
比干等着强——地基本身与 driver 无关，谁先到谁带。

## 一点五、真输入是真的，来路要写清

- **模型加工的输入**是 Khronos 官方样本 `Suzanne.gltf`（含 `.bin` 与两张 1024 贴图），
  落在 `_Scratch/样本模型/`。派活方用 Blender 真读过：**面数 3936、材质数 1、
  贴图尺寸 1024、骨骼数 0**。
- **它不是 tripo 出的粗模**——模型生成那一环还没通（P8-6）。
  用公开样本验加工站，是因为「加工站行不行」与「粗模从哪来」本来就是两件事。
  **批次日志里必须写明这一条**，不许让下一个接手的以为模型生成链路已经通了。

## 二、要用户填的东西

见 [创作管线要你填的](creation-pipeline-user-setup.md)。**密钥的值 Claude 全程不碰。**

## 三、新增的锁定决策

见 [创作管线锁定决策P8](creation-pipeline-decisions-p8.md)（决策 80 起）。
原 [创作管线锁定决策](creation-pipeline-decisions.md) 写到 179 行，离 200 行硬顶不够放新条目，拆出去了。

## 四、批次日志

| 批次 | 日志 |
|---|---|
| P8 批次 1 daemon 与唤醒源 | [P8-batch1-daemon-and-wake.md](creation-pipeline-batch-logs/P8-batch1-daemon-and-wake.md) |
| P8 批次 3 调用协议与 blender 加工 | [P8-batch3-protocol-and-blender.md](creation-pipeline-batch-logs/P8-batch3-protocol-and-blender.md) |
| P8 批次 5 执行后端与 AI 对抗预审 | [P8-batch5-backend-and-prereview.md](creation-pipeline-batch-logs/P8-batch5-backend-and-prereview.md) |
| P8 批次 4 comfyui 真生图 | [P8-batch4-comfyui-generation.md](creation-pipeline-batch-logs/P8-batch4-comfyui-generation.md) |
| P8 批次 9 飞书长连接唤醒源 | [P8-batch9-feishu-longconn-wake.md](creation-pipeline-batch-logs/P8-batch9-feishu-longconn-wake.md) |
| P8 批次 7b 飞书读写闭环 | [P8-batch7b-feishu-roundtrip.md](creation-pipeline-batch-logs/P8-batch7b-feishu-roundtrip.md) |
| P8 批次 8 面板下游页收尾 | [P8-batch8-dashboard-downstream.md](creation-pipeline-batch-logs/P8-batch8-dashboard-downstream.md) |
| P8 批次 5b 语义评估两项 | [P8-batch5b-semantic-eval.md](creation-pipeline-batch-logs/P8-batch5b-semantic-eval.md) |
| P8 批次 6 tripo 桥 | [P8-batch6-tripo-bridge.md](creation-pipeline-batch-logs/P8-batch6-tripo-bridge.md) |
| P8 批次 7 飞书桥 | [P8-batch7-feishu-bridge.md](creation-pipeline-batch-logs/P8-batch7-feishu-bridge.md) |
| P8 批次 10 tripo 翻 v3 | [P8-batch10-tripo-v3.md](creation-pipeline-batch-logs/P8-batch10-tripo-v3.md) |
| P8 批次 11 面板全面重做 | [P8-batch11-dashboard-rebuild.md](creation-pipeline-batch-logs/P8-batch11-dashboard-rebuild.md) |
| P8 批次 12 助手常驻会话 | [P8-batch12-assistant-serve.md](creation-pipeline-batch-logs/P8-batch12-assistant-serve.md) |
| P8 批次 13 补漏一批 | [P8-batch13-gap-sweep.md](creation-pipeline-batch-logs/P8-batch13-gap-sweep.md) |
| P8 批次 14 图生图 | [P8-batch14-img2img.md](creation-pipeline-batch-logs/P8-batch14-img2img.md) |
