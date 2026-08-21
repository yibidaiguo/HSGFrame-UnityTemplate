# 踩坑清单

> 每一条都是真踩过、且**下次还会踩**的。动手前扫一眼比事后查两小时便宜。
> 已经被工具修掉的坑不留在这里（那些进[归档在 git 历史里的提交信息]），留的都是仍然成立的。

## 门禁

- **测试基线锁**：新增测试文件必须登记进 `Tools/Gates/Config/test-baseline.json`，否则 `gate.baseline` 红。
  该目录在执行端的写盘拒绝清单里，**执行端改不了，只能由 Claude 跑更新模式**：
  `run gate.baseline --arguments-file <{TemplateRoot, ConfigurationPath, UpdateBaseline:true}>`。
  **两个路径都要写绝对路径，且 `ConfigurationPath` 必须是 `gate-config.json` 而不是 `.host.json`**——
  写相对路径或写成 host 配置，它会「重建成功」但把基线削掉一百多条。
- **新 csproj 必须加进 `Solutions/Template.sln`**，否则十秒级门禁根本编不到它，绿灯是假绿。
  （现在基线锁的解决方案成员对账会抓这件事，但加的时候顺手做掉更省事。）
- **文档长度 200 行**：只有 `Doc/design-art-workflow-proposal.md`（总纲）在 `documentExemptions` 里。
  其余一律 200 行管——写不下就拆一份，别去加豁免。
- **`gate.ps1` 的输出重定向到文件时子进程 JSON 日志会丢**（`Invoke-GateCommand` 用 `Out-Host`）。
  要逐道明细就单跑那条 `gate.*` 命令。
- **加了检查器要三样做齐**：检查器 + 命令注册 + 接进 gate 脚本。少接一步，
  `GateWiringCoverageTests` 会红——那条测试就是为「加了忘接线」立的。

## 命名门禁

- **缩写黑名单**逐**词段**查：`Mgr / Cfg / Svc / Btn / Idx / Tmp / Utils / Ctx / Param / Attr / Conf`。
  `Configuration` 是完整词段，不违规。
- **公开类型与公开成员必须有中文 `<summary>`**。
- **含中文键的 JSON 字面量写成单行 raw string 会被判红**（门禁把中文键当标识符）。
  **改成多行 raw string 就绿**，内容一个字不用动。
- **多行 raw string 里的「裸中文」照样判红**：`"标题": "值"` 这种带引号的没事，
  但测试里故意写坏的 JSON 若含裸中文，门禁看不出那是数据。坏样本一律用 ASCII 写。
- **全仓路径必须 ASCII**（`gate.pathascii` 是 block 模式）。新写的落盘路径——
  尤其是代码里拼出来的产物文件名——别用中文，写的时候就撞不上。

## 命令层

- **调用约定是 `run <命令名> --arguments-file <json 路径>`**，不吃行内 `--键 值`。
  要程序化调命令就先把参数落成临时 JSON（面板的 `/cmd` 也是这么转的）。
- **参数键一律按 CLR 属性名匹配**（`CommandRegistry` / `CommandArgumentBinder` / `CommandArgumentValidator`）。
  `[JsonPropertyName]` 的中文别名会被默认值填充覆盖，做不成——决策 57 说的「中文键配 ASCII 别名」
  指的是 URL 查询串那类外部接口。
- **`bridge.provision` 的参数名是 `Driver` 不是 `DriverName`**，写错会被参数校验拦下。
- **认不出的参数会被静默忽略**：`--dry-run` 不等于 `--DryRun`，写错了不会报错，
  只是那个开关没生效（真供给跑成了非干跑，踩过一次）。

## 面板

- **面板的 JS 住在 C# verbatim 字符串里，引号转义是雷区**：`""` 是一个引号，
  空串分支必须写 JS 单引号 `''`。写错了整份脚本语法错、一页都不渲染，
  而编译 / 测试 / 门禁全绿（决策 76，埋了三期才被人发现）。**改完面板务必真开一次看。**
- **前后端靠中文键对齐**（前端 `行['预览']` vs 后端 `[JsonPropertyName("预览")]`），
  批量替换误伤过一次：整列恒显「无」而不报错、测试全绿。改键名时两边一起改。
- **浏览器会缓存 `/panel`**：改完前端重启服务后页面没变，先带个查询参数强刷再判断。

## 环境

- **本机是 .NET 10 preview SDK**：写盘的 `JsonSerializerOptions` 要写成
  `new JsonSerializerOptions(JsonSerializerOptions.Default) { … }`，
  裸构造序列化 `JsonArray` 里的字符串元素会抛。
- **起服务一律走 `Tools/start.ps1`**（影子拷贝，bin 不被占）。见到
  「being used by another process」多半是有人绕开脚本直接 `dotnet run` 起了服务。
- **`.ps1` 一律 UTF-8 带 BOM**：掉了 BOM，Windows PowerShell 5.1 会把中文读成乱码。

## 派活

- **执行端改不了的两处**：`Tools/Gates/Config/`（门禁判定标准）与
  `Tools/AgentRunner/Config|Roles/`（围栏与角色档案本身）。
  派活时别把这些列进任务书的「改哪些文件」，配置项由 Claude 自己补。
- **`assistant-package` 的文件清单有三份**（`PackageFiles` / `ProspectiveFiles` /
  `AssistantPackageInspector`），加包文件时三份都要改，只改前两份会让 `gate.provision` 假绿（决策 72）。
- **外部 API 的 base URL 与端点形状不许凭印象写进任务书**，第一条要核的就是 base URL（决策 94）。
- **任务书自相矛盾会烧掉一整轮**：写完自己过一遍「这几条能同时成立吗」，
  尤其是「必须报错」与「某某测试必须继续绿」这类组合。
