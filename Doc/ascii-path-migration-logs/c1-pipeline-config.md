# c1 批 · `Tools/CreationPipeline/Config/`（2026-08-21）

`Config/创作管线/` 整个挪进工具自己的目录下，文件名一并去中文——**待办 2 的第一半**。

| 旧 | 新 |
|---|---|
| `Config/创作管线/本机.json` | `Tools/CreationPipeline/Config/local.json`（仍在 .gitignore 里） |
| `Config/创作管线/本机.示例.json` | `Tools/CreationPipeline/Config/local.example.json` |
| `Config/创作管线/下游.json` | `Tools/CreationPipeline/Config/downstream.json` |
| `Config/创作管线/引擎.json` | `Tools/CreationPipeline/Config/engine.json` |
| `Config/创作管线/同步水位.json` | `Tools/CreationPipeline/Config/sync-watermark.json` |

**最危险那一步的验收**（密钥不入库，决策 5）：

```
git check-ignore -v Tools/CreationPipeline/Config/local.json
→ .gitignore:80:Tools/CreationPipeline/Config/local.json
git status --porcelain | grep local.json
→ 空
```

**两条 `Path.Combine` 的写法都要改**：C# 里是按段写的（`"Config", "创作管线"`），
文档与脚本里是正斜杠（`Config/创作管线`）。只改一种，另一种会静默留在原地。

引用同步改了 38 个文件，含 5 处路径常量（`PipelinePaths` / `EngineSettings` /
`LocalBridgeSettings` / `BridgeRouteTable` / `SyncWatermark`）、7 份测试、
飞书长连接旁路那个 Python 脚本、面板读取器。

**旁路进程重启过**：它在启动时读一次配置，不重启就还拿着旧路径。
重启后日志确认「会话目录」那一行出现（这是批次 12 加的），并重新连上长连接。

真跑验收：`bridge.balance` 真发一次请求（它要读 `local.json` 里的密钥）→ HTTP 200。
**这条比测试绿有力**：测试用的是临时目录造的假结构，真读的是新路径的真文件。
