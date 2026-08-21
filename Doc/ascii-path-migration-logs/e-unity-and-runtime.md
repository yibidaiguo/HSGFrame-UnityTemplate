# e 批 · UnityProject 资产与 _Tasks 运行时路径（2026-08-21）

最后一块，也是最大的一块：**76 个 Unity 资产（38 个文件 + 38 个 `.meta`）**，
外加 `_Tasks/` 下那一整套运行时目录与文件名。

## Unity 那 76 个

| 类 | 改法 |
|---|---|
| `导入规则.json` ×12 | `import-rules.json`（**代码按文件名扫它**，不是随便一个名字） |
| `归档路由.json` | `archive-routes.json` |
| `模块说明.md` ×2 / `来源.md` | `README.md` / `SOURCE.md` |
| 材质 `Mat_任务物件` 等 ×5 | `Mat_QuestItem` / `Mat_Teleporter` / `Mat_SpawnPoint` / `Mat_Interactable` / `Mat_Trigger` |
| 预制体 `P_*` ×5 | 同上前缀换成 `P_` |
| 贴图 / 音效 / 输入表 | `T_Village_Ground_Texture` / `T_Chest02_Diffuse` / `A_Monster_Death_Sfx` / `IA_DefaultInput` |
| 场景 `启动.unity` / `村庄.unity` | `Boot.unity` / `Village.unity` |
| 设置资产 | `EntityResourceMap.asset` / `MainPanelSettings.asset` / `DefaultTheme.tss` |

**GUID 帮了大忙**：Unity 的资产引用走 `.meta` 里的 GUID，
`.meta` 跟着同名文件一起挪，引用一条都不会断。所以这一批**没有一个引用是靠改文本修好的**——
真正要盯的是那些**不走 GUID 的引用**，只有三类：

1. **`EditorBuildSettings.asset` 里的场景路径**（按路径，不按 GUID）；
2. **代码里按文件名扫的约定名**（`import-rules.json`、`LevelDefinitionFileName`）；
3. **资产里按「地址字符串」写的引用**——见下面那个坑。

## 这一批最阴的一个坑：Unity 把中文写成转义序列

`EntityResourceMap.asset` 里的资源地址，中文被写成了 `\uXXXX` 形式的转义序列
（`P_可交互物` 存成 `P_` 加四段转义码）。于是：

- 文件改名了 ✅
- 代码改名了 ✅
- `git grep 中文` **零命中** ✅
- 而那串地址还指着旧名字 ❌

表现是 EditMode 测试报「地址『可交互物』指不到任何预制体」，**不是编译错**。
修法是按同样的转义规则去搜替换（把中文按 Unity 的写法转成转义序列再比对）。

**推论：改 Unity 资产名之后，`git grep` 搜不到不等于没有残留。**
`.asset` / `.unity` / `.prefab` 里的字符串要按转义形式再搜一遍。

## `_Tasks/` 那一套运行时路径

`唤醒/` → `wake/`、`会话/` → `conversations/`、`已处理/` → `processed/`、
`草稿/` → `drafts/`、`30-产物/` → `30-outputs/`、`20-工作项/` → `20-work-items/`、
`变体/` → `variants/`、溯源边车后缀 → `.provenance.json`、
`05-变更影响.md` → `05-change-impact.md`、需求快照 → `00-requirement.vN.json`……

这些**在 `.gitignore` 里**，但仍然是仓库路径——**gitignore 决定进不进 git，不决定它叫什么**。
而且它们全是代码常量，改起来比资产安全得多。

信号文件名里原来带事件名的中文（`…-收到消息.json`），改成 ASCII 槽位
（`…-message.json`）：**事件名的中文进文件内容的「事件」字段**，文件名只要能排序与不撞名。

## f 批 · 门禁翻成 block（同一次做完）

存量清零之后立刻把 `pathAsciiMode` 从 `warn` 改成 `block`，并**反向验证过**：
临时造一个中文名文档 → 门禁当场判红并指名道姓，删掉 → 复绿。

## 验收

- `gate.ps1` **PASS 全绿**、`gate-unity.ps1` **PASS**（Unity 编译绿、EditMode 27/27、`.meta` 0 问题）。
- `gate.pathascii`：**存量 0 条，模式 block**。
- 真跑：助手会话（`assist.serve`）在新路径下照常取信号、归档到 `conversations/processed/`；
  飞书长连接旁路重启后日志确认目录已是 `_Tasks/wake` 与 `_Tasks/conversations`。

## 留下的一条，要用户决定

**资产名归一化器仍然产中文名**：`AssetInboxArchiver` 把带中文的图片名归一成
`T_<中文>_….png`，而且有一条测试**专门锁住这个行为**（`PlanKeepsChineseWordsInStem`）。

存量资产已经全部改成 ASCII，但**下一张进收件箱的中文名图片仍然会被归一成中文名**，
然后被 `gate.pathascii` 判红。两者现在是打架的。

改哪一边都是**策略决定**，不该由这一批顺手定：
要么归一化器也转 ASCII（那要一套音译或翻译规则），
要么给收件箱产出的资产名开一个豁免。**留给用户点头。**
