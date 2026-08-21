# 创作管线 · P1 批次 5「面板骨架」日志

> 总账在 [创作管线进度](../creation-pipeline-progress.md)。本文只装这一批的落地记录与验收证据。

## 这一批真正做的是什么

`Tools/Dashboard/` 原本只有一页滚动日志。本批按子文档 04 加上创作管线的
**总览 / 任务 / 需求池 / 门禁 / 引擎**五页，外加 `POST /cmd` 的命令白名单。

重点不是画界面，是把五页的数据源做成**纯读取器**——子文档 04 的第一条总原则是
「零私有状态：每页 = 文件读取器 + 渲染」。服务端一个业务状态都不缓存，每次请求现读文件。

## 实际落地

**数据与路由（派活）**

- `Tools/Dashboard/CreationPanelReader.cs`：六个只读方法 + 六个不可变模型类。
  模型属性名是 ASCII，靠 `[JsonPropertyName("中文")]` 把出去的 JSON 键定成中文。
- `Tools/Dashboard/PanelCommandWhitelist.cs`：纯判定，不执行任何东西。
- `Tools/Dashboard/PanelCommandRunner.cs`：起 CLI 子进程，白名单在起进程**之前**判。
- `Tools/Dashboard/DashboardServer.cs`：加七条路由；老构造 `DashboardServer(channel, port)` 签名不动。
- `Tools/Dashboard/Dashboard.csproj`：加对 `CreationPipeline.csproj` 的引用（单向，不成环）。

**页面与入口（Claude 自己写）**

- `Tools/Dashboard/CreationPanelPage.cs`：五页装在一份自包含 HTML 里，零 CDN 零外部依赖。
- `Tools/Dashboard/Program.cs`：从当前目录逐级往上找 `.git` 定仓库根，
  推出 `Pools/` 与命令宿主工程路径；找不到时**明说**「面板五页会返回未配置」，
  不让用户对着五页「取数据失败」猜原因。支持 `--repository-root` 覆盖。

**路由表**

| 路由 | 给什么 |
|---|---|
| `/` | 原来的滚动日志页（没动） |
| `/panel` | 创作管线面板五页 |
| `/api/panel/overview` `/tasks` `/requirements` `/gates` `/engine` | 五页的数据，JSON |
| `/api/panel/task?id=REQ-xxxx` | 单条任务详情文本，**与 CLI 的 `task.status` 同源** |
| `POST /cmd` | 执行一条白名单内的命令 |

## 本批新增的锁定决策

18. **绑定地址钉死 `http://localhost:{Port}/`。** 面板能触发命令 = 能动仓库；
    开局域网访问要显式改代码并想清楚后果，不给配置开关。
19. **`POST /cmd` 只放行六族前缀**（`task.` / `pool.` / `bridge.` / `engine.` / `conflict.` / `spec.`），
    命令名里出现 `.. / \ & | ; ` $ < >` 任一字符直接拒，整行超 500 字符拒。
    **判定在起进程之前**，且全程走 `ProcessStartInfo` 的参数列表，一次都不拼 shell。
20. **门禁报告不存在时报「未跑」，不是绿也不是红。** `_Generated/gate-report.json` 是后续期才产的东西，
    现在没有谁写它。把没有的东西说成绿是这类面板最典型的骗人法。
    路径照样报出去——面板要能告诉人「该有的报告长在哪」。
21. **面板与 CLI 同源。** 任务详情直接调 `TaskStatusReport.RenderOne`，
    不另写一份渲染；两处显示不一致这种毛病从源头上不存在。

## 验收记录（全部由 Claude 亲自复跑，不采信执行后端的自述）

| 项 | 结果 |
|---|---|
| `dotnet build Solutions/Template.sln` | 0 警告 0 错误 |
| `dotnet test Solutions/Template.sln` | 全绿；`Dashboard.Tests` **40/40** |
| `pwsh Tools/Gates/gate.ps1` 全量 | **PASS** |
| `gate.baseline --UpdateBaseline` | 新增 2 条、**零修改** |

**真起服务打过一遍**（`dotnet run --project Tools/Dashboard -- --port 8791`，Claude 亲手 curl）：

| 请求 | 实际返回 |
|---|---|
| `/api/panel/overview` | `{"进行中任务":0,…,"门禁":"未跑","下游数":1,"已供给":1}`——下游那两个数与批次 3 供给的 feishu 对得上 |
| `/api/panel/engine` | `{"模式":"值守",…,"卡片路由":{11 条}}`——模式与锁定决策 10 对得上 |
| `/api/panel/gates` | `{"状态":"未跑","报告路径":"…/_Generated/gate-report.json","条目":[]}` |
| `/api/panel/task?id=REQ-0001` | `REQ-0001 需求文件不存在`（模板仓库零池子内容，这就是对的） |
| `/panel` | 9408 字节 HTML，标题命中 |
| `POST /cmd` `pool.validate --PoolRoot Pools` | **HTTP 200**，退出码 0，输出是宿主真打出来的「池子校验通过，问题 0 条」 |
| `POST /cmd` `git status` | **HTTP 403**，「命令「git」不在面板白名单里…」 |
| `POST /cmd` `pool.validate\|rm` | **HTTP 403**，「命令名里有不该出现的字符：\|」 |

## 本批修掉的两个缺陷（验收时发现，已修）

1. **任务书把命令宿主的调用约定写错了**——我写的是 `run <整条命令行>`，
   宿主实际只吃 `run <命令名> --arguments-file <json>`。执行后端**照任务书写并如实报了这处不一致**，
   没有自作主张去改宿主。Claude 把 `PanelCommandRunner` 改成：先把命令行拆成参数对象、
   落成一份临时 JSON、把路径喂给宿主、跑完即删。这是任务没写清，不是它写崩。
2. **`Program.cs` 还在用老的两参构造**，面板五页会全部 503。已接上仓库根自动探测。

另有一处门禁踩坑：新测试里那份「故意写坏的 JSON」原本含裸中文，
命名门禁看不出那是字符串里的数据，判成「标识符含中文」。**改成纯 ASCII 的坏 JSON 即可**，
断言一个字没动。这条已并进总账的「门禁现场」。

## 已知缺口（不阻塞，记着）

- **面板是只读 + 命令框，没有子文档 04 里的关卡按钮、看板拖拽、DAG 渲染。**
  那些要等 P4 的完整版；P1 要的就是「骨架」。
- **没有 `FileSystemWatcher` 推送**，刷新靠手动切页。子文档 04 允许「手动刷新兜底」，
  但真正的文件监听推送还没做。
- **`_Generated/gate-report.json` 至今没有生产者。** `gate.ps1` 只往控制台打，不落报告文件。
  门禁页要真有内容，得先让 `gate.ps1` 落一份报告——那是独立的一小件活。
- **命令输出的中文在 GBK 控制台下会花**，但走 HTTP 到浏览器是 UTF-8，页面上是正常的。
