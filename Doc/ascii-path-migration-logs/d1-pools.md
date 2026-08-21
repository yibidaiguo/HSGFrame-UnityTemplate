# d1 批 · `Pools/` `_Generated/` `Bridges/`（2026-08-21）

| 旧 | 新 |
|---|---|
| `Pools/Schema/{基线,项目}/` | `Pools/Schema/{Baseline,Project}/` |
| `Pools/Schema/Baseline/需求.schema.json` 等五份 | `requirement / work-item / design-record / asset-request / provenance` |
| `Pools/Designs/{定稿,汇总,记录}/` | `Pools/Designs/{Final,Digest,Records}/`，`定稿.json` → `final.json` |
| `Pools/{专项,审查意见,晋升提案,知识,组织}/` | `Pools/{Epics,ReviewOpinions,Promotions,Knowledge,Organization}/` |
| `Bridges/*/依赖清单.json` | `dependencies.json` |
| `Bridges/comfyui/配方/图标@v5/` | `Bridges/comfyui/recipes/icon@v5/`（`映射.json` → `mapping.json`） |
| `_Generated/Bridges/feishu/*` | `table-description / epic-table / validation-messages / fingerprint`，`助手配置包/` → `assistant-package/`，`知识/` → `knowledge/` 及其五份 |
| `_Generated/门禁报告.json` | `_Generated/gate-report.json`（**只有消费者、没有生产者**，改了不会有症状，只能靠搜字符串） |

### 这一批必须讲清楚的两件事

**一、实体名与文件名解耦。** `需求` `工作项` `资产请求` 这些是**数据里的领域词汇**，
中文留着才读得懂（`"实体": "需求"` 出现在 schema、信封、卡片、面板文案里）。
但文件名要 ASCII。所以 `PoolPaths` 加了一张显式的
**实体名 → 文件名词干**表（`EntityFileStem`）。表里没有的实体按原名返回——
那样路径门禁会把它列出来，比悄悄拼一个中文文件名强。

**二、目录名与展示标签解耦。** 面板设计池页的分类列显示的就是「定稿 / 汇总 / 记录」，
而目录已经是 `Final / Digest / Records`。原来这两样共用一个字符串常量，
改目录名那一刻页面文案会跟着变英文，或者忘了改其中一处、那一整类恒显示为空。
现在 `DesignCategories` 是 `(目录名, 展示标签)` 两栏。

### 踩到的三个坑

1. **无脑替换误伤数据字段**：`"配方"` 既是目录名，也是**溯源边车里的字段名**。
   脚本把 schema 里的 `{ "名称": "配方" }` 换成了 `"recipes"`，
   两条溯源测试当场红。**测试替我看住了**——这正是「断言的形状决定它能不能发现故障」：
   那两条断言的是「校验零发现」与「往返逐字段相等」，所以字段名一变就红。
2. **目录移动的顺序**：先把 `助手配置包/知识` 挪成 `assistant-package/knowledge`，
   再挪 `助手配置包` 时目标已存在，于是整个旧目录被塞进了新目录里，成了
   `assistant-package/助手配置包/`。**先挪子目录、后挪父目录会撞这个。**
3. **`final.json` 的取名规则**：定稿是一稿一目录，文件名恒定、目录名才是这份定稿的名字。
   读取器靠「文件名等于某个常量」判断要不要取目录名——文件名从 `定稿.json` 改成
   `final.json` 时那个常量要跟着改，**漏改的表现是名字变成「final」，不是报错**。

### 验收

- `dotnet test` 全绿（25 个测试工程）、`dotnet build` 0 错误、`gate.ps1` PASS 全绿。
- **真跑重生成**：`bridge.provision` 重跑 → 11 个产物全部按新名落盘；
  `gate.provision` 供给对账 0 问题（指纹只比内容哈希，改名不影响，实证）。
- **真跑助手**：`assist.serve` 干跑 → 「知识文件 5 份」，说明
  `assistant-package/knowledge/` 那条路径真读到了。
