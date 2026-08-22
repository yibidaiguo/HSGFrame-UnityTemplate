<#
  一键打开管理面板：没起就起，起了就直接开浏览器。

  这个脚本是 panel.bat 的正身——双击那个 bat 走到这里。它做四件事：
    1. 端口已经有人应答就先问一句「你是谁的面板」（/api/panel/identity 里的仓库根）：
       是这个仓库的才叫「已经在跑」，直接开浏览器；是别的仓库的就当场说清并停下——
       只看端口不看仓库，会把人送进另一个项目的面板，而且页面看着一切正常。
    2. 没在跑就调 Tools/start.ps1 -NoAssistant。只起面板不起助手是有意的：
       助手要飞书密钥，没配密钥的机器上它起不来，而那跟「看面板」这件事无关。
    3. 等端口真应答了再开浏览器。起进程只说明进程起来了，不说明它在听端口——
       早开一秒，浏览器给的是「无法访问此网站」，人以为面板坏了。
    4. 起不来就把日志路径与最可能的原因说出来，不静悄悄地退。

  用法：
    pwsh Tools/panel-open.ps1                 # 起面板并打开浏览器
    pwsh Tools/panel-open.ps1 -SkipBuild      # 不编译，直接用现成产物（编译被占用时的兜底）
    pwsh Tools/panel-open.ps1 -Port 8790      # 指定端口（多个项目并行时各用各的）
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

# 端口应答不应答：面板**在不在**的判据。PID 文件会过期（进程被杀、机器重启），端口不会。
function Test-面板在跑 {
    param([int]$探测端口)
    try {
        $响应 = Invoke-WebRequest -Uri "http://localhost:$探测端口/panel" -UseBasicParsing -TimeoutSec 2
        return $响应.StatusCode -eq 200
    } catch {
        return $false
    }
}

<#
  问一句「你是谁的面板」。

  只探端口是不够的：一台机器上并行开几个项目时，8766 上很可能跑着**另一个仓库**的面板。
  只看端口的脚本会说「已经在跑」，然后把人送进别人的项目——页面一切正常，数据全是别人的。
  这条踩过一次，所以探活必须连仓库根一起比。

  返回身份对象；端口没人应答、或应答的东西认不出自己是谁（旧版面板没有这个接口），返回 $null。
  这两种情况在调用处要分开处理：前者是「可以起」，后者是「不知道是谁，不许当成自己的」。
#>
function Get-面板身份 {
    param([int]$探测端口)
    try {
        $响应 = Invoke-WebRequest -Uri "http://localhost:$探测端口/api/panel/identity" -UseBasicParsing -TimeoutSec 3
        if ($响应.StatusCode -ne 200) { return $null }
        return $响应.Content | ConvertFrom-Json
    } catch {
        return $null
    }
}

# 两个路径指的是不是同一个地方：大小写不敏感、忽略末尾分隔符（Windows 上 D:\X 与 D:\X\ 是一个地方）。
function Test-同一个仓库 {
    param([string]$甲, [string]$乙)
    if (-not $甲 -or -not $乙) { return $false }
    $规范 = { param($路径) $路径.TrimEnd([char]92, [char]47).Replace([char]47, [char]92) }
    return [string]::Equals((& $规范 $甲), (& $规范 $乙), [StringComparison]::OrdinalIgnoreCase)
}

$端口有人应答 = Test-面板在跑 -探测端口 $Port
$占着端口的 = if ($端口有人应答) { Get-面板身份 -探测端口 $Port } else { $null }
if ($端口有人应答) {
    # 端口有人应答了，先问清楚是谁的：是自己的才叫「已经在跑」。
    if ($null -eq $占着端口的) {
        Write-Host ''
        Write-Host "端口 $Port 上有东西在应答，但它认不出自己属于哪个仓库（多半是改造前的旧版面板，没有身份接口）。"
        Write-Host '  不敢当成这个仓库的面板——那可能把你送进别的项目。'
        Write-Host '  要么去停掉它，要么换个端口：panel.bat 8790（或 pwsh Tools/panel-open.ps1 -Port 8790）'
        exit 1
    }

    if (-not (Test-同一个仓库 $占着端口的.仓库根 $repositoryRoot)) {
        Write-Host ''
        Write-Host "端口 $Port 上跑的是**另一个仓库**的面板，不是这一个："
        Write-Host ("    它挂着：{0}（{1}）" -f $占着端口的.仓库根, $占着端口的.仓库名)
        Write-Host ("    这里是：{0}" -f $repositoryRoot)
        Write-Host '  没给你开浏览器：开了你看到的会是另一个项目的数据，而且看着一切正常。'
        Write-Host '  两条路：去那个仓库跑 panel-stop.bat 停掉它，或者这里换端口——panel.bat 8790'
        exit 1
    }

    Write-Host "面板已经在跑（端口 $Port，仓库 $($占着端口的.仓库名)），直接开浏览器。"
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

    if ($听上了) {
        # 起完再核一次身份：等端口的那几秒里，抢先占上这个端口的完全可能是别人。
        $起来的 = Get-面板身份 -探测端口 $Port
        if ($null -ne $起来的 -and -not (Test-同一个仓库 $起来的.仓库根 $repositoryRoot)) {
            Write-Host ''
            Write-Host ("端口 $Port 现在应答的是另一个仓库的面板（{0}），不是刚起的这一份。" -f $起来的.仓库根)
            Write-Host '  多半是端口被抢了。换个端口再来：panel.bat 8790'
            exit 1
        }
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
