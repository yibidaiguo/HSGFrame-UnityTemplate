# Graphs 目录

本目录中的 JSON 是 `com.hsgframe.graphadapter` 的旧运行时兼容测试夹具，不是节点图的创作源。

新图以及现有图的人工/AI 编辑都以 Unity 中的 `NodeGraphAsset` 和 `BlackboardAsset` 为准，
并通过 `NodeEditor.EditorUI.GraphAuthoringAssetAccess` 读、校验和写回。不要从这里的 JSON
覆盖 Unity 资产，也不要为新模块扩展这套有损格式。
