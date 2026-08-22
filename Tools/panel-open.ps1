<#
  一键打开管理面板：没起就起，起了就直接开浏览器。

  这个脚本是 panel.bat 的正身——双击那个 bat 走到这里。它做四件事：
    1. 面板已经在跑（端口应答）就什么都不起，直接开浏览器。这一步要紧：
       重复起一份会去抢同一个端口，第二份起不来，而人以为自己「又打开了一次」。
    2. 没在跑就调 Tools/start.ps1 -NoAssistant。只起面板不起助手是有意的：
       助手要飞书密钥，没配密钥的机器上它起不来，而那跟「看面板」这件事无关。
    3. 等端口真应答了再开浏览器。起进程只说明进程起来了，不说明它在听端口——
       早开一秒，浏览器给的是「无法访问此网站」，人以为面板坏了。
    4. 起不来就把日志路径与最可能的原因说出来，不静悄悄地退。

  用法：
    pwsh Tools/panel-open.ps1                 # 起面板并打开浏览器
    pwsh Tools/panel-open.ps1 -SkipBuild      # 不编译，直接用现成产物（编译被占用时的兜底）
    pwsh Tools/panel-open.ps1 -Port 8790      # 指定端口
    pwsh Tools/panel-open.ps1 -NoBrowser      # 只起，不开浏览器
#>
[CmdletBinding()]
param(
    # 面板端口。多项目并行时各仓库传不同端口，别都用默认值。
    [int]$Port = 8766,

    # 跳过编译，直接用现成产物。改过代码就别用。
    [switch]$SkipBuild,

    # 只把面板起起来，不开浏览器。
    [switch]$NoBrowser,

    # 等端口应答最多几秒。
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'progress.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$panelUrl = "http://localhost:$Port/panel"
$logDirectory = Join-Path $repositoryRoot 'Logs/services'

# 端口应答不应答：面板在跑的唯一可信判据。PID 文件会过期（进程被杀、机器重启），
# 端口不会——它要么真的有人在听，要么没有。
function Test-面板在跑 {
    param([int]$探测端口)
    try {
        $响应 = Invoke-WebRequest -Uri "http://localhost:$探测端口/panel" -UseBasicParsing -TimeoutSec 2
        return $响应.StatusCode -eq 200
    } catch {
        return $false
    }
}

if (Test-面板在跑 -探测端口 $Port) {
    Write-Host "面板已经在跑（端口 $Port），直接开浏览器。"
} else {
    # 同一个仓库只许有一份面板：两份共用同一个 PID 文件与同一个停止文件，
    # 起第二份会把第一份的 PID 记录覆盖掉，之后 stop.ps1 一停停俩、或者漏掉一个成孤儿进程。
    # 换端口是合法需求，但要先停掉在跑的那份，不能两份并存。
    $pid文件 = Join-Path $repositoryRoot '_Tasks/sidecar/dashboard-pid.json'
    if (Test-Path $pid文件) {
        $在跑的 = Get-Content $pid文件 -Raw | ConvertFrom-Json
        if ($在跑的.端口 -ne $Port -and (Get-Process -Id $在跑的.面板 -ErrorAction SilentlyContinue)) {
            Write-Host ''
            Write-Host "这个仓库已经有一份面板在端口 $($在跑的.端口) 上跑（PID $($在跑的.面板)）。"
            Write-Host "  要看它：http://localhost:$($在跑的.端口)/panel"
            Write-Host '  要换端口：先双击 panel-stop.bat 停掉，再用新端口起。'
            Write-Host '  （一个仓库两份面板共用同一个停止文件与 PID 文件，并存会把停止这件事搅乱。）'
            exit 1
        }
    }

    Write-Host "面板没在跑，起一份（端口 $Port）..."

    # 只编面板真正要跑的那两个工程，不编整个解决方案。
    # 差别不是快慢，是「起不起得来」：解决方案里有些工程的 bin 被别的常驻进程占着
    # （客户端起的 MCP 服务就占着 Tools/Mcp/bin），编整个 sln 会在那里 MSB3021 失败，
    # 而那跟「打开面板」这件事一点关系都没有。编完再让 start.ps1 走影子拷贝那一套。
    if ($SkipBuild) {
        Write-Host '  （-SkipBuild：不编译，直接用现成产物）'
    } else {
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
        foreach ($工程 in @('Tools/Dashboard/Dashboard.csproj', 'Tools/Cli/CommandHost/CommandHost.csproj')) {
            $名 = [System.IO.Path]::GetFileNameWithoutExtension($工程)
            $退出码 = Invoke-带进度 -标题 ("编译 " + $名) -文件 'dotnet' -参数 @(
                'build', (Join-Path $repositoryRoot $工程), '-v', 'q', '--nologo'
            ) -工作目录 $repositoryRoot -日志路径 (Join-Path $logDirectory ("panel-build-" + $名 + ".log"))
            if ($退出码 -ne 0) {
                Write-Host ''
                Write-Host "编译失败：$工程"
                Write-Host '  DLL 被占用的话，先 pwsh Tools/stop.ps1 停干净；'
                Write-Host '  只想先把面板打开，就用现成产物起：双击 panel.bat /skip，或 pwsh Tools/panel-open.ps1 -SkipBuild'
                exit 1
            }
        }
    }

    # 先清零再调，否则读到的是上一条外部命令留下的旧值——
    # 这里踩过一次：start.ps1 明明把面板起起来了，这一句却判成「没起来」。
    $global:LASTEXITCODE = 0
    & (Join-Path $PSScriptRoot 'start.ps1') -Port $Port -NoAssistant -SkipBuild
    $启动退出码 = $LASTEXITCODE

    # 成没成的最终判据是**端口应答**，不是退出码：进程起来了不等于它在听端口，
    # 而退出码在跨脚本调用里本来就不牢靠（上面那句清零只是让它别更离谱）。
    $听上了 = Wait-带进度 -标题 ("等面板开始听端口 " + $Port) -超时秒 $TimeoutSeconds -判据 {
        Test-面板在跑 -探测端口 $Port
    }

    if (-not $听上了) {
        Write-Host ''
        Write-Host "面板没起来（start.ps1 退出码 $启动退出码，等了 $TimeoutSeconds 秒端口 $Port 仍无应答）。常见的三种："
        Write-Host '  1) 影子目录里还跑着上一轮的服务：上面会点名是哪个 PID，先 pwsh Tools/stop.ps1 停干净。'
        Write-Host "  2) 端口 $Port 被别的程序占了：换一个，例如 pwsh Tools/panel-open.ps1 -Port 8790"
        Write-Host '  3) 面板自己启动时炸了：看下面两份日志。'
        Write-Host "  日志：$logDirectory\dashboard.err.log、$logDirectory\dashboard.out.log"
        exit 1
    }
}

Write-Host ''
Write-Host "  面板　$panelUrl"
Write-Host "  日志页　http://localhost:$Port/"
Write-Host '  停　　双击 panel-stop.bat，或跑 pwsh Tools/stop.ps1'

if (-not $NoBrowser) {
    Start-Process $panelUrl | Out-Null
}
