# 需求文档同步 A 批 · 需求目录化

> 上游：[需求文档与任务同步（设计审查）](../requirement-doc-and-task-sync-design.md) 第四节与第九节。
> 这一批是整件事里**唯一一处破坏性改动**，纯本地，不碰飞书。

## 一、动了什么

`Pools/Requirements/REQ-0042.json` → `Pools/Requirements/REQ-0042/`：

```
REQ-0042/
  requirement.json      ← 原来那份，一个字段不动
  index.md              ← 需求文档正文（B 批开始产）
  media/                ← 图片与视频本体
  snapshots/            ← 覆盖前留底
```

| 处 | 改法 |
|---|---|
| 8 处拼 `<id>.json` | 全收进 `PoolPaths` 的访问器族 |
| 3 处枚举需求文件 | 换成 `PoolPaths.EnumerateRequirementIdentifiers` |
| 取号 | 新增 `IdentifierAllocator.NextByDirectoryName`，REQ 用它；DR/WI/ASSET 仍扫文件名 |
| 校验 | 判据从「文件名即 id」改成「目录名即 id」，另加规则「需求.骨架缺失」 |
| 测试 | `PoolTestWorkspace` 是总闸，改它带走大半 |

## 二、为什么路径必须收进一处（决策 99）

散着拼的代价不是「改起来累」——那只是一次性的。真正的代价是
**下次再加一份随需求走的东西时，你没有任何办法知道还漏了谁**。
这一批一次就加了三样（文档、媒体、快照），如果还散着，
下一个人加第四样时要重新把全仓翻一遍，而且翻漏了没人会发现。

所以红线是：**只搬不收等于把同一个坑挖深**。以后加东西只改 `PoolPaths` 一处。

## 三、「目录在而骨架缺」要当场报，不许静默跳过

`RequirementValidator.CheckDirectory` 遇到没有 `requirement.json` 的子目录时报
「需求.骨架缺失」，而不是跳过。静默跳过的后果是**一条需求凭空从池子里消失，
而每一道门禁都是绿的**——决策 42 那一类假象，最贵的那种绿。

## 四、验收

| 门禁 | 结果 |
|---|---|
| 秒级（25 个测试工程） | 全绿，其中 CreationPipeline.Tests 641、Dashboard.Tests 112 |
| 十秒级（全解决方案编译） | 绿 |
| `pwsh Tools/Gates/gate.ps1` 全套 | **PASS，全部门禁全绿** |

## 五、销掉的三笔账

1. **Toolkit.Tests 与 Indexing.Tests 编不动。** 原因不是改动，是 `assist.serve`
   常驻进程占着 `Tools/Cli/CommandHost/bin` 的输出目录。进程停掉后两个工程分别 70 绿、28 绿。
   **这条值得记住**：全解决方案编不动时先看有没有常驻进程占着 bin，
   那时候的红跟你改了什么毫无关系。
2. **`_Generated/Bridges/feishu/validation-messages.json` 里还是旧规则 id。**
   重跑一次 `bridge.provision --driver feishu` 对齐，
   「需求.id与文件名」已换成「需求.id与目录名」，并多出「需求.骨架缺失」。
3. **飞书应用在知识库空间里没有身份**（空间信息与节点列表都回 `131006`）。
   这一条不归代码管，**D 批第一步就会炸**，动 D 批之前要用户先在飞书知识库设置里
   把应用加成空间成员（建节点要管理员）。

## 六、提交怎么分的

三次提交，按「不是一件事就不进同一个提交」分：

| 提交 | 内容 |
|---|---|
| 飞书助手常驻链路修四处 | **与本功能无关**的一批（单实例锁、事件去重、流编码、回话送达判定） |
| A 批源码 | `PoolPaths` 一族、取号器、校验器、8 处路径收编、供给产物、设计文档拆分 |
| A 批测试 `[测试变更]` | 10 个测试文件 + 重建测试基线（铁律 3） |

重建基线时顺带销掉两条不是这次的账：`ContactSheetComposerTests.cs` 与
`PngImageTests.cs` 是上一次提交（选片卡那批）落下的，那次没重建基线，
所以**基线锁在这次动手之前就已经是红的**。

## 七、已知缺口

- 仓库里现在**一条需求都没有**（`Pools/Requirements/` 是空的），
  所以目录化本身没有真数据跑过——跑过的是单测里的临时池子。
  第一条真需求会从助手会话或 `pool.pull` 进来，那时再看一眼形状对不对。
- `snapshots/` 与 `media/` 两个目录**只有路径访问器，还没有任何东西往里写**，
  分别等 D 批与 C 批。
