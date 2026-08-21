# 结构规范 · 总纲

> 三份一套（随模板走）：本份定原则，《结构规范-代码》《结构规范-资源》定细则，都在本目录。
> 宿主项目的现状差距与迁移计划放宿主自己的文档区（本模板的开发宿主放在仓库根 `Doc/Specifications/`）。
> 本规范不取代模板 CLAUDE.md 的五条铁律，在其上加结构规则。

## 一、设计维度

每条规则都要能回答「服务哪个维度」。评审新规则时对着这张表问一遍：

| # | 维度 | 本规范里的落点 |
|---|---|---|
| 1 | 框架/业务分离 | 框架=UPM 包（`com.hsgframe.*`），业务=`Assets/Game/`，工具链=`Tools/` |
| 2 | 模块解耦 | 一模块一夹；跨模块只经 Contracts/Events；检查器把关（R2） |
| 3 | 可测试性 | Logic 零 UnityEngine、双工程 `dotnet test`（铁律，结构为它让路） |
| 4 | 性能优化 | 加载分组（常驻/按需/随场景）、图集与分组对齐、常驻分组字节预算（R6） |
| 5 | 资源热更新 | 动态区=收集根（`ResourceArt/`、`Scenes/World/`）；可选功能的程序集只住自己的包（R13） |
| 6 | Bug 查找 | 错误四要素、业务日志走 `HSGFrame.Logging`（R7 查裸 Debug.Log）、路径即职责、索引可查 |
| 7 | AI 规范遵守 | 能查的写成检查器，规则用前缀/路径机判；单一事实源防改漏；模块骨架可脚手架生成 |
| 8 | 协作与版本 | `.meta` 完整性（既有）、一夹一责、二进制资产全仓只一份（R4 查重复内容） |
| 9 | 构建与 CI | 四级门禁节奏不变；生成物幂等；构建与热更产物固定落点，生成物不手改 |
| 10 | 扩展成本 | 新模块=按标准骨架起夹（后续 `module.create` 命令），不靠记忆拼目录 |
| 11 | 本地化 | 文案经 `localization` 包资产分区，不焊进代码与预制体 |
| 12 | 安全边界 | `_Inbox` 内容不入正式引用；宿主声明的只读区保持只读；删除类动作停下问人 |

## 二、三个代码体 + 一个资产体

| 体 | 位置 | 身份 | 命名空间 |
|---|---|---|---|
| 框架 HSGFrame | `Packages/com.hsgframe.<模块>/` | 通用运行时框架，有自己的名字，不跟宿主走 | `HSGFrame.<模块>` |
| 业务 | `UnityProject/Assets/Game/` | 项目的玩法与内容 | `Template.<模块>.*`，生成新项目时由 `project.create` 整体替换为项目名 |
| 工具链 Toolkit | `Tools/`（驻 Unity 部分在 `Assets/Game/Scripts/Toolkit/`） | 开发期命令与门禁，随模板分发 | `Template.Toolkit.*`（随同一替换机制走） |
| 资产 | `UnityProject/Assets/Game/` 下 `Art / ResourceArt / Scenes / Settings` | 见《结构规范-资源》 | — |

进框架的判定：**换一个游戏也原样能用**才进 `com.hsgframe.*`；带一点玩法假设就留业务层。
拿不准先放业务层，第二个使用场景出现时再上提——上提是一次单独提交。

## 三、全局硬规则

1. **下划线前缀只给机器管理区。** 白名单就三个：`_Inbox/`（资产中转）、`_Generated/`（生成物，人不手改）、
   `_Scratch/`（仓库根，AI 试验区，git 忽略永不上传，内部分级仍照本规范执行）。
   其余正式文件与目录一律不以下划线开头。下划线的语义就是「此处内容不是人手维护的正式品」。
2. **万物只做一份（单一事实源）。** 改一处，处处生效；复制粘贴出第二份就是缺陷：
   - 配置表：Excel（Luban）唯一，镜像 JSON 有哈希锁（既有）
   - UI 面板：`uidef.json` 唯一，UXML/代码是生成物（既有幂等门禁）
   - 生成代码：`codegen.run` 幂等门禁（既有）
   - 资产：同内容全仓只一份（R4 哈希查重）；复用走引用，预制体定制走 Prefab Variant，不复制资产改副本
   - 依赖版本：`Packages/manifest.json` 唯一
   - 加载分组：`bundle-group-rules.json` 的分组条目上「加载分组」字段唯一，收集器配置与它对账（R5）。
     它是目录级的事，不写进文件级的 `导入规则.json`——理由见《结构规范-资源》第三节
<!-- feature:hotfix 开始 -->
   - 热更程序集清单：HybridCLR 设置唯一，命令校验
<!-- feature:hotfix 结束 -->
   - Agent 入口：规范正文只住规范文档目录；各模型入口文件（`CLAUDE.md` 为源，`AGENTS.md` 等为镜像）
     由 `Tools/AgentSync/agent-sync.ps1` 同步，R9 对账
3. **通用性**（既有硬原则）：模板与工具链里禁止出现宿主项目名；`HSGFrame` 是框架自己的名字，不进黑名单。
4. **分级公式**：资源 `类型 → 功能 → 模块 → 内容`；代码 `层 → 模块 → 职责`。任何新文件先问「它属于哪一格」。
5. **能查的写成检查器，查不了的才写文档。** 凡标 R 号的规则都要有对应检查器（工单在宿主差距文档）；
   没有 R 号的是文档级约定，靠评审。
6. **命名基线**：标识符全英文完整单词、缩写黑名单与中文标识符由 `gate.naming` 查，大小写由
   `.editorconfig` 随 `dotnet build` 查；代码文件名 = 文件内主类型名；
   资产文件名 = 前缀 + PascalCase 主干（中文词保留）+ 小写扩展名。

## 四、顶层地图（模板根，目标态）

```
<模板根>/
  Packages/com.hsgframe.*     框架（UPM，Runtime/Editor/Tests 三段式）
  UnityProject/Assets/
    _Inbox/                   资产中转（下划线白名单之一）
    Game/                     业务正式区
      Art/  ResourceArt/  Scenes/  Settings/  Scripts/  Tests/
    Plugins/ StreamingAssets/ 及第三方与生成物自留地，业务资产禁入
  Solutions/                  纯 .NET 测试工程（链接 Logic 源码）
  Tools/                      工具链（命令层、门禁、资产管线、代码生成、Agent 入口同步）
  Specifications/                       本套结构规范三份
  Config/ Graphs/ Levels/ UI/ Index/ Memory/   数据与定义（单一事实源侧）
<仓库根>/_Scratch/            AI 试验区：临时产物唯一落点，git 忽略，正式区不得引用
```
