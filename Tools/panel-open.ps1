<#
  一键打开管理面板：没起就起，起了就直接开浏览器。

  这个脚本是 panel.bat 的正身——双击那个 bat 走到这里。它做四件事：
    1. 端口已经有人应答就先问一句「你是谁的面板」（/api/panel/identity 里的仓库根）：
       是这个仓库的才叫「已经在跑」，直接开浏览器；是别的仓库的就当场说清并停下——
       只看端口不看仓库，会把人送进另一个项目的面板，而且页面看着一切正常。
    2. 没在跑就调 Tools/start.ps1 -NoAssistant 把面板拉起来。
    3. 等端口真应答了再开浏览器。起进程只说明进程起来了，不说明它在听端口——
       早开一秒，浏览器给的是「无法访问此网站」，人以为面板坏了。
    4. 起不来就把日志路径与最可能的原因说出来，不静悄悄地退。
    5. 面板确定活着之后，再单独起飞书助手（长连接旁路 + 常驻会话）。
       这一步原本不在这里，代价是真的：「双击 panel.bat」只起面板，机器人是死的，
       而长连接没连上时飞书那头的消息**当场就丢，事后重启也补不回来**——踩过一次，
       三条消息全丢，日志里一个字都没有。面板与助手本来就是一起用的，默认一并起。
       没配密钥的机器上它本来就起不来，那种情况自动跳过（原来 -NoAssistant 就是为这个留的）。

  用法：
    pwsh Tools/panel-open.ps1                 # 起面板并打开浏览器
    pwsh Tools/panel-open.ps1 -SkipBuild      # 不编译，直接用现成产物（编译被占用时的兜底）
    pwsh Tools/panel-open.ps1 -Port 8790      # 指定端口（多个项目并行时各用各的）
    pwsh Tools/panel-open.ps1 -NoBrowser      # 只起，不开浏览器
    pwsh Tools/panel-open.ps1 -NoAssistant    # 只起面板，不起飞书助手
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
    [int]$TimeoutSeconds = 60,

    # 不起飞书助手，只起面板。没配密钥的机器不必传——那种情况这个脚本自己会跳过。
    [switch]$NoAssistant
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

<#
  这台机器能不能起飞书助手。返回空串表示能起，否则返回不能起的原因（原样说给人听）。

  **只判空、不打印内容**：local.json 里装的是真的 App Secret（决策 5、78）。
#>
function Get-助手起不来的原因 {
    if ($NoAssistant) { return '传了 -NoAssistant（panel.bat /nobot）' }

    $本机配置文件 = Join-Path $repositoryRoot 'Tools/CreationPipeline/Config/local.json'
    if (-not (Test-Path $本机配置文件)) {
        return '本机没有 Tools/CreationPipeline/Config/local.json'
    }

    try {
        $本机配置 = Get-Content $本机配置文件 -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return "local.json 读不动（$($_.Exception.Message)）"
    }

    if ([string]::IsNullOrWhiteSpace($本机配置.'飞书应用密钥')) {
        return 'local.json 里没有「飞书应用密钥」'
    }

    if ([string]::IsNullOrWhiteSpace($本机配置.'下游配置'.feishu.'应用标识')) {
        return 'local.json 里没有「下游配置 → feishu → 应用标识」'
    }

    return ''
}

# 这个 PID 还是当初记下的那个进程吗。只比 PID 会认错：PID 会被回收，
# 而「助手其实死了，PID 却被别的进程占了」这个假阳性的代价，是机器人从此一直不回话。
function Test-还是那个进程 {
    param([int]$进程号, [string[]]$进程名)
    if (-not $进程号) { return $false }
    $进程 = Get-Process -Id $进程号 -ErrorAction SilentlyContinue
    if (-not $进程) { return $false }
    return $进程名 -contains $进程.ProcessName
}

# 助手（长连接旁路 + 常驻会话）现在什么状态：在跑 / 没在跑 / 半死（两个只活了一个）。
function Get-助手状态 {
    $助手pid文件 = Join-Path $repositoryRoot '_Tasks/sidecar/assistant-pids.json'
    if (-not (Test-Path $助手pid文件)) { return '没在跑' }
    try {
        $记录 = Get-Content $助手pid文件 -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return '没在跑'
    }

    $旁路活着 = Test-还是那个进程 -进程号 $记录.旁路 -进程名 @('python', 'pythonw')
    $会话活着 = Test-还是那个进程 -进程号 $记录.助手 -进程名 @('dotnet')
    if ($旁路活着 -and $会话活着) { return '在跑' }
    if ($旁路活着 -or $会话活着) { return '半死' }
    return '没在跑'
}

<#
  起飞书助手：长连接旁路收消息，常驻会话取消息、回话。

  为什么归这个脚本管：以前双击 panel.bat 只起面板，机器人是死的，
  而长连接没连上时飞书那头的消息**当场就丢**，事后再起也补不回来。
  面板与助手本来就是一起用的，分成两条命令只会让人以为「面板开着＝机器人活着」。

  这里出的任何岔子都不算失败：这个脚本的主职是把面板打开，不该被助手拖垮。
#>
function Start-助手 {
    $状态 = Get-助手状态
    if ($状态 -eq '在跑') {
        Write-Host '  飞书助手已经在跑，不重起（重起会多一份，同一条消息回两遍）。'
        return
    }

    if ($状态 -eq '半死') {
        Write-Host ''
        Write-Host '  飞书助手只剩半条命：旁路与常驻会话活了一个死了一个，没敢在这个状态上再起一份。'
        Write-Host '    先停干净再来：双击 panel-stop.bat，或 pwsh Tools/stop.ps1'
        return
    }

    # 助手要用的两个桥在这里编。assistant-start.ps1 是带 -SkipBuild 起的
    # （常驻期间一行 MSBuild 输出都不许有——那会插在协议 JSON 前面，整次调用被判不合协议），
    # 而上面那段只编面板要跑的工程。命令宿主一并带上：面板已经在跑的那条路径根本没走到上面的编译。
    if (-not $SkipBuild) {
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
        foreach ($工程 in @(
            'Tools/Cli/CommandHost/CommandHost.csproj',
            'Bridges/feishu/src/BridgeFeishu/BridgeFeishu.csproj',
            'Bridges/oaicompat/src/BridgeOaicompat/BridgeOaicompat.csproj')) {
            $名 = [System.IO.Path]::GetFileNameWithoutExtension($工程)
            $退出码 = Invoke-带进度 -标题 ("编译 " + $名) -文件 'dotnet' -参数 @(
                'build', (Join-Path $repositoryRoot $工程), '-v', 'q', '--nologo'
            ) -工作目录 $repositoryRoot -日志路径 (Join-Path $logDirectory ("panel-build-" + $名 + ".log"))
            if ($退出码 -ne 0) {
                Write-Host ''
                Write-Host "  飞书助手没起：编译失败（$工程）。面板不受影响，照常能用。"
                Write-Host "    日志：$logDirectory\panel-build-$名.log"
                return
            }
        }
    }

    Write-Host '起飞书助手（长连接旁路 + 常驻会话）...'
    $global:LASTEXITCODE = 0
    try {
        & (Join-Path $PSScriptRoot 'start.ps1') -NoDashboard -SkipBuild
    } catch {
        Write-Host ''
        Write-Host "  飞书助手没起来：$($_.Exception.Message)"
        Write-Host '    面板不受影响，照常能用。'
        return
    }

    # 判据是「两个进程都还在」，不是退出码——退出码在跨脚本调用里本来就不牢靠。
    if ((Get-助手状态) -eq '在跑') {
        Write-Host '  飞书助手起来了：机器人现在收得到消息、回得了话。'
    } else {
        Write-Host ''
        Write-Host '  飞书助手没起来（两个常驻进程没能都留下）。面板不受影响，照常能用。'
        Write-Host "    日志：$(Join-Path $repositoryRoot 'Logs/assistant')　serve.out.log 与 sidecar.err.log"
    }
}

# 「要不要起助手」先定下来，后面几处都看它。
$助手起不来的原因 = Get-助手起不来的原因
$要起助手 = [string]::IsNullOrEmpty($助手起不来的原因)

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

# 走到这里面板一定是活的了，助手才单独起——顺序不许换：
# 助手那一步慢（要编两个桥、要连长连接），排在前面会让「打开面板」白等。
if ($要起助手) {
    Start-助手
} else {
    Write-Host ''
    Write-Host "飞书助手没起：$助手起不来的原因"
    Write-Host '  只看面板的话这条不必管；要机器人能回话，把密钥配进 Tools/CreationPipeline/Config/local.json 再来。'
}

Write-Host ''
Write-Host "  面板　$panelUrl"
Write-Host "  日志页　http://localhost:$Port/"
Write-Host '  停　　双击 panel-stop.bat，或跑 pwsh Tools/stop.ps1（面板与助手一起停）'

if (-not $NoBrowser) {
    Start-Process $panelUrl | Out-Null
}
