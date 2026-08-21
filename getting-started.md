# 开始使用

## 起一个新项目

```bash
git clone <本仓库> NewProject
cd NewProject
```

然后二选一：

### A. 直接拿模板工程当项目用

Unity Hub → **Add project from disk** → 选 `UnityProject/`。

> Unity Hub 的「新建项目」用不上：它从 Unity 自带模板生成一个**空工程**，
> 跟 clone 下来的这份没有关系。要用的是「添加已有工程」。

### B. 用模板生成一个新项目（推荐，模板本身保持干净）

```bash
dotnet run --project Tools/Cli/CommandHost/CommandHost.csproj -- run project.create --arguments-file 新项目.json
```

`新项目.json`：

```json
{
  "TemplateRoot": ".",
  "TargetDirectory": "D:/Projects/Unity",
  "ProjectName": "MyGameName"
}
```

> `ProjectName` 只认 ASCII（`^[A-Za-z][A-Za-z0-9_.]*$`），中文名会被拒。
> 框架包前缀 `com.hsgframe.*` 不跟项目改名，没有「包前缀」参数。
> 密钥文件（`local.json`）与运行时状态（`_Tasks/` 等）不会被复制进新项目。

生成完 Unity Hub → **Add project from disk** → 选 `D:/Projects/Unity/我的游戏/UnityProject/`。

## 环境怎么装

**UPM 包不用管**：`UnityProject/Packages/manifest.json` 里写着的包，Unity 打开工程时自己会去拉。

除此之外，常驻部分没有要装的东西：clone 完打开工程就能用。可选功能自带的安装步骤跟着各自的包走。

**模块包少装了、或者想暂时摘掉一个**：菜单 **工具链 → 模块管理**。
面板列出 `Packages/` 下的全部模块包和它们在清单里的装卸状态，一个包一行、装卸各一个按钮，
未安装的排在最上面，底下还有一个「全装回来」。卸载只摘掉清单里的那一行，
包目录一直留在盘上，所以任何时候都装得回来。
包目录本身被 `feature.remove` 整块删掉时，面板会把它标成「缺目录」——那种只能用 git 还原。

<!-- feature:hotfix 开始 -->

**热更要装一次的只有一样**：HybridCLR 的本地 il2cpp 数据（约 800 MB）。
它装在 `UnityProject/HybridCLRData/`，**刻意不进仓库**（进了 git 就变成每人克隆 800 MB）。

三种装法，挑一种：

| 场景 | 怎么做 |
|---|---|
| 打开工程时 | 没装会自动弹一句问你装不装，点「现在装」 |
| 想手动装 | 菜单 **工具链 → 热更 → 安装 HybridCLR 本地数据** |
| 无人值守 / CI | `./Tools/Cli/unity-cmd.ps1 -ExecuteMethod HSGFrame.Hotfix.Editor.HybridClrEnvironmentInstaller.InstallFromCommandLine -TimeoutMinutes 40` |

装完它会打一份报告：哪一步做了、哪一步跳过、哪一步失败。实测走 gitee 镜像约 20 秒。

**为什么不做成"clone 完就全好"**：800 MB 的 il2cpp 数据与编辑器版本绑定
（当前是 `v6000.3.x-8.13.0` 对应 Unity 6000.3.11f1），换 Unity 版本就要重装一次。
把它放进仓库既拖垮 clone，又会在换版本时给出错误的旧数据。

<!-- feature:hotfix 结束 -->

## 装完之后确认环境是好的

```powershell
./Tools/Gates/gate.ps1          # 全量门禁：测试 / 编译 + 二十多道专项检查（命名 / 边界 / 基线 /
                                #   池子 / 供给 / 生成物幂等 / Agent 镜像……），跑完落 _Generated/gate-report.json
./Tools/Gates/gate-unity.ps1    # 分钟级：Unity 真编译 / EditMode 测试 / .meta 完整性
```

两条都 PASS 就说明这份工程在你机器上是通的。

## 日常起服务：一键启停

```powershell
./Tools/start.ps1               # 编译一次 → 影子拷贝 → 起面板 + 飞书助手
./Tools/start.ps1 -NoAssistant  # 没配飞书密钥就只起面板
./Tools/stop.ps1                # 全停
```

面板默认在 `http://localhost:8766/panel`。服务从影子拷贝里跑，
**开着服务照样能 `dotnet build` / 跑门禁**，不会再把 bin 里的 DLL 锁死。

**Claude Code 用户**：仓库根的 `.mcp.json` 已注册项目 MCP 服务，
命令层全部命令（`asset_*` / `gate_*` / `task_*`……）在 Claude Code 里直接可用；
第一次要先跑一遍 `dotnet build Solutions/Template.sln` 把服务编出来。

**把活派给执行后端**（OpenAI 兼容 API，配置在 `local.json` 的「下游配置.oaicompat」）：

```powershell
pwsh Tools/dispatch.ps1 -Role implementer -TaskFile <任务书路径>
```

四个角色（implementer / verifier / operator / explore）的档案在 `Tools/AgentRunner/Roles/`，
任务书模板在 `.claude/skills/dev-cycle/templates/`，围栏在 `Tools/AgentRunner/Config/agent-policy.json`。

## 版本对照

| 组件 | 版本 | 装在哪 |
|---|---|---|
| Unity | 6000.3.11f1 | `UnityProject/ProjectSettings/ProjectVersion.txt` 钉死 |
<!-- feature:hotfix 开始 -->
| HybridCLR | `com.code-philosophy.hybridclr` v8.13.0 | UPM（manifest）+ 本地 il2cpp 数据（现装） |
<!-- feature:hotfix 结束 -->
| YooAsset | `com.tuyoogame.yooasset` 3.0.5 | UPM（manifest） |
| System.Text.Json | 8.0.5 netstandard2.0 | `UnityProject/Assets/Plugins/SystemTextJson/`（dll 进仓库，见同目录 `来源.md`） |
| Unity.Mathematics | 1.3.3 | UPM + `Tools/Deps/` 下的 dll 快照（给纯 .NET 侧用） |
