# GraphAdapter：运行时兼容层

`com.hsgframe.graphadapter` 只保留旧 `Graphs/*.json` 样例与两套运行时执行器的对拍能力。
这里的 `GraphDocument` / `GraphJsonCodec` 是**有损兼容投影**，不能表达完整 Unit、黑板层级、
编辑器布局、稳定 `authoringKey` 和并发修订信息，因此不得作为新图的创作格式或事实源。

## 唯一创作源

- 图：`NodeGraphAsset`
- 黑板：`BlackboardAsset`
- Editor 读写门面：`NodeEditor.EditorUI.GraphAuthoringAssetAccess`
- 可交换文档：上游的 `GraphAuthoringDocument`，它只是一次读/改/写快照，不是并行保存的第二份源

人工在节点编辑器修改资产后，AI 用同一个门面 `Read`；AI 写回后，人工继续打开同一个资产。
不要把 `GraphAuthoringDocument` 或旧 `GraphDocument` JSON 作为 sidecar 提交并宣称它是权威数据。

## Editor 代码

调用代码必须放在 Editor-only 程序集中，并直接引用 `NodeEditor.Editor`。本包刻意不再包装一层，
避免 DTO、校验和诊断语义随上游升级而分叉。

```csharp
using NodeEditor.EditorUI;

var path = "Assets/NodeGraph/Dialogue/Opening.asset";
var read = GraphAuthoringAssetAccess.Read(path);
if (!read.Succeeded)
{
    // 将 read.Diagnostics 原样返回给 AI 或工具调用方。
    return;
}

var document = read.Document;
// 修改 document；保留 Read 返回的 revision 向量。
var validation = GraphAuthoringAssetAccess.Validate(path, document);
if (validation.Succeeded)
{
    var write = GraphAuthoringAssetAccess.Write(path, document);
    // 以后续 write.Document 作为下一次编辑基线。
}
```

新建图不要复制旧图或手工拼 revision：调用
`CreateDraft(path, module, group, graphType)` 取得当前有效黑板与完整 owner revision，修改后再走同一套
`Validate` / `Write`。

发现可用图和节点/Unit/黑板契约时，使用 `List(module)` 与 `Describe(module)`；批处理环境可使用
上游 `GraphAuthoringCommandLine` 的 `list`、`describe`、`read`、`draft`、`validate`、`write` 命令。

## 兼容层的边界

`Graphs/示例流程图.json` 仅是回归测试夹具。`GraphJsonCodec` 仍能读取它，
`GraphExecutor` 与 `NodeEditorGraphRunner` 仍能执行并对拍；它没有资产写回能力。
新模块不要扩展这个 JSON 格式，应该在所属 NodeGraph 模块注册节点、Unit、根目录与校验器。
