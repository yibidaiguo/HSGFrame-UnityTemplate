# d2b 批 · 配置表三件套与索引产物（2026-08-21）

`Config/{Tables,Schema,Mirror}/{背包,怪物,技能}.*` → `{Bag,Monster,Skill}.*`；
`Index/{技能索引,模型索引,Prefab索引,场景索引}.json` → `{skill,model,prefab,scene}-index.json`。

**表名跟实体名同源，也要解耦**：schema 里本来就有两个字段——
`tableName`（「背包」，给人看的）与 `tableIdentifierName`（`Bag`，生成代码用的）。
**文件名跟标识名走，展示名留在内容里**，`indexName` 同理保持中文、只改 `outputPath`。

### 这一批的坑最典型：三层，一层比一层晚才炸

1. **编译层**：没炸。表名是运行时参数，改文件名编译器不知道。
2. **测试层**：炸了 7 条，但都是**夹具自己写的文件名**没跟着改
   （夹具写 `背包.schema.json`、目标指向 `Bag.schema.json`）。
3. **生成层**：最后一条最阴——`LubanDefinitionWriter` 拼的
   `input="*rows@{表名}.json"` 用的是**展示表名**，于是 Luban 报
   「`'TbBag'` 的 input 文件或目录不存在」。**这一条要真跑 Luban 才看得见**，
   编译和大部分测试都过。

**一句话**：这类改名的故障，越靠近「真跑」的那一层越晚暴露。
`gate.ps1` 里那道 `codegen.run`（生成物是不是最新的）是唯一能兜住第三层的东西。
