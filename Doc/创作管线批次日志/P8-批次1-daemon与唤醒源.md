# P8 批次 1 · daemon 循环外壳与文件唤醒源

> 上游：[创作管线P8计划](../创作管线P8计划.md) 第一节第 6 条。
> 销的是第六节那张表里「常驻 daemon 与唤醒事件源」这一行的**前一半**。

## 一、这批把什么翻成了可验

决策 54 当初写「不写常驻 daemon」，理由是**常驻进程在门禁里没法验**。
这批把那条前提解掉：循环支持「跑满 N 轮自己退出」，`最大轮数=0` 才是无限。
门禁跑 3 轮、假时钟、假 sleep，判定链路全程可复现（决策 81）。

决策 54 的另一半原样不动——**外壳不做判定**：daemon 只干三件事，
「取活判定 + 记账 + 消费唤醒」。取到活之后真去执行工作项是别的命令的事。

## 二、落了什么

| 文件 | 内容 |
|---|---|
| `Tools/CreationPipeline/WakeSignalSource.cs` | 文件投递式唤醒源：扫 `_Tasks/唤醒/*.json`，按文件名序数序取第一个，消费后**移动**到 `已处理/`（撞名追加 `-2`） |
| `Tools/CreationPipeline/DaemonTickRecord.cs` | 轮次记录 + `_Tasks/引擎轮次.jsonl` 追加写账本；`Read` 坏行跳过并计数 |
| `Tools/CreationPipeline/PollingDaemon.cs` | 循环外壳：拿锁 → 查停止 → 查唤醒 → 判定 → 记账 → 查轮数 → sleep |
| `Tools/Cli/CommandHost/Commands/PipelineFlowCommands.cs` | `engine.daemon` 命令 + 参数类 |
| `Solutions/CreationPipeline.Tests/{PollingDaemon,WakeSignalSource}Tests.cs` | 10 条测试 |

## 三、验证结果

| 档 | 结果 |
|---|---|
| 秒级定向 `dotnet test --filter` | 10/10 通过（**Claude 独立复跑过一次**，不是只看执行端的表） |
| 十秒级 `dotnet build` | 0 错误 0 警告 |
| 全量 `dotnet test` | 0 失败 |
| 门禁全量 `gate.ps1` | **PASS，全部门禁全绿** |
| 命令自测 | `engine.daemon MaxRounds=3` 真跑，三轮都报「值守模式，永不自动取活」，收尾报「跑满 3 轮」 |
| 反向验证 | 执行端做过变异验证：`Poll` 的排序改成倒序、停止检查改成恒 false，两条测试都真红了，恢复后绿 |

**本批不碰 `UnityProject/Assets/Game/Scripts/` 与 `Packages/com.hsgframe.*/Runtime/`，
所以铁律 4 的分钟级 Unity 门禁不适用。**

## 四、验收时补的两处

执行端交回来之后 Claude 自己动手补的，不是它写崩：

1. **`engine.daemon` 少了 `PoolRoot` 参数。**
   它按子文档 03 §五把池根写死成 `<仓库根>/Pools`，推断本身没错，
   但 `engine.tick` / `engine.wake` / `engine.mode` / `engine.queue` 四条命令
   全都收 `PoolRoot` 参数。守护写死会让同一台机器上两条命令看的是两个队列，
   **而谁都不会发现**。补成「显式给了就用，留空退化成 `Pools`」。
2. **运行时状态进了 git 的视野。** `_Tasks/引擎轮次.jsonl` 是这台机器跑了什么的流水、
   `.engine.lock` 里存的是本机进程号、`_Tasks/唤醒/` 是这台机器收到过什么。
   三样都补进 `.gitignore`——进 git 只会让两台机器互相打架。

## 五、已知缺口

- **唤醒事件源只落了「文件投递」这一种**（决策 82）。飞书事件订阅要么长连接、
  要么内网穿透，两条都要真租户，归 P8-7 / P8-8。
  文件源不是桩——它是一条真能跑通的唤醒通道，且不假装自己是飞书。
- **`DaemonTickLedger.Read` 分不出「整份读不动」**。文件不存在返回空列表（空是正常状态，
  决策 77），但 IO 整个读失败也返回空列表——接口没有失败原因字段。
  账本目前只在验收时人看，还没有页面拿它印统计数字；
  **哪天面板要拿它印「问题 0 条」，这个缺口就是决策 77 那类假绿，必须先补。**
- **锁释放失败没往上透。** `SingleInstanceLock.ReleaseFailureReason` 有值时
  `DaemonRunSummary` 看不见，删不掉的锁文件会让下一轮启动走「接管陈旧锁」那条路——
  能自愈，但自愈过程不留痕。

## 六、给下一个接手的

- **daemon 不执行工作项，这是故意的。** 想让它真跑活，那是另一条命令的事，
  别把执行塞进循环体——决策 52 与 54 都在拦这件事。
- **一轮崩掉不让 daemon 死掉，但账上必须留痕**（决策 83）：
  `判定跑成=false` 与「这一轮没取到活」是两支，永远不许合并。
