<#
  长活儿的进度与失败诊断，给 start.ps1 / panel-open.ps1 / dispatch.ps1 共用。

  两件事，都是踩出来的：

  1. **没进度就分不清「在跑」和「卡死」。** 编译要十几秒、影子拷贝要几秒，
     期间脚本一声不吭，人只能盯着一个不动的光标猜。这里每半秒刷一次同一行，
     带秒数——动着就是在跑，停了就是卡了。

  2. **robocopy 撞上锁默认是无声死等。** 它的默认重试是 /R:1000000 /W:30：
     一个被占住的文件能让它「重试一百万次、每次等三十秒」，看上去就是永远不返回。
     `Copy-影子` 一律带 /R:2 /W:1 —— 撞上锁两秒内失败，然后**把占着它的进程指出来**，
     而不是让人等到自己去按 Ctrl+C。
#>

# 输出被重定向时（写进日志、被别的脚本吃掉）不能用回车覆盖同一行：
# 那会在日志里留下一长串带 \r 的垃圾。这时改成每两秒打一行，信息一样在，只是不动画。
$script:可动画 = -not [Console]::IsOutputRedirected

function 写进度行 {
    param([string]$文字)
    if ($script:可动画) {
        Write-Host ("`r" + $文字.PadRight(78)) -NoNewline
    } else {
        Write-Host $文字
    }
}

function 收尾进度行 {
    param([string]$文字)
    if ($script:可动画) {
        Write-Host ("`r" + $文字.PadRight(78))
    } else {
        Write-Host $文字
    }
}

<#
  跑一个外部命令，边跑边转圈。返回退出码，不抛。
  标准输出与标准错误落到日志文件——失败时那份日志就是唯一能看的东西，
  边跑边打屏会把进度行冲得七零八落。
#>
function Invoke-带进度 {
    param(
        [Parameter(Mandatory)][string]$标题,
        [Parameter(Mandatory)][string]$文件,
        [Parameter(Mandatory)][string[]]$参数,
        [string]$工作目录 = (Get-Location).Path,
        [string]$日志路径
    )

    if (-not $日志路径) {
        $日志路径 = Join-Path ([System.IO.Path]::GetTempPath()) ("进度-" + [guid]::NewGuid().ToString('N') + '.log')
    }

    $错误日志 = $日志路径 + '.err'
    $进程 = Start-Process -FilePath $文件 -ArgumentList $参数 -WorkingDirectory $工作目录 `
        -RedirectStandardOutput $日志路径 -RedirectStandardError $错误日志 `
        -NoNewWindow -PassThru

    $转圈 = @('|', '/', '-', '\')
    $圈号 = 0
    $开始 = Get-Date
    while (-not $进程.HasExited) {
        Start-Sleep -Milliseconds 500
        $秒 = [int]((Get-Date) - $开始).TotalSeconds
        写进度行 ("  {0} {1}… {2}s" -f $转圈[$圈号 % 4], $标题, $秒)
        $圈号++
    }

    $用时 = [int]((Get-Date) - $开始).TotalSeconds
    $退出码 = $进程.ExitCode
    if ($退出码 -eq 0) {
        收尾进度行 ("  [完成] {0}（{1}s）" -f $标题, $用时)
    } else {
        收尾进度行 ("  [失败] {0}（{1}s，退出码 {2}）" -f $标题, $用时, $退出码)
        foreach ($份 in @($错误日志, $日志路径)) {
            if ((Test-Path $份) -and (Get-Item $份).Length -gt 0) {
                Write-Host "  ---- $份 ----"
                Get-Content $份 -Tail 15 | ForEach-Object { Write-Host "  $_" }
                break
            }
        }
    }

    return $退出码
}

<#
  等一个条件成立，边等边转圈。成立返回 $true，超时返回 $false。
  给「进程起来了但还没开始听端口」这种事用：起进程与能连上是两回事，
  不等就开浏览器，人看到的是「无法访问此网站」，以为面板坏了。
#>
function Wait-带进度 {
    param(
        [Parameter(Mandatory)][string]$标题,
        [Parameter(Mandatory)][scriptblock]$判据,
        [int]$超时秒 = 60
    )

    $转圈 = @('|', '/', '-', '\')
    $圈号 = 0
    $开始 = Get-Date
    while (((Get-Date) - $开始).TotalSeconds -lt $超时秒) {
        if (& $判据) {
            收尾进度行 ("  [完成] {0}（{1}s）" -f $标题, [int]((Get-Date) - $开始).TotalSeconds)
            return $true
        }
        Start-Sleep -Milliseconds 500
        写进度行 ("  {0} {1}… {2}s / 上限 {3}s" -f $转圈[$圈号 % 4], $标题, [int]((Get-Date) - $开始).TotalSeconds, $超时秒)
        $圈号++
    }

    收尾进度行 ("  [超时] {0}（等满 {1}s）" -f $标题, $超时秒)
    return $false
}

<#
  找出哪个进程占着某个目录底下的文件。

  Windows 上「谁锁了这个文件」没有便宜的准确答案（要 Restart Manager 或 handle.exe）。
  这里退而求其次：列出命令行里出现过这个目录的进程——我们自己起的常驻服务
  （面板、命令宿主、助手）全都是这个长相，而它们正是唯一会占住这些 DLL 的东西。
  找不到就如实说找不到，不猜。
#>
function Get-占用进程 {
    param([Parameter(Mandatory)][string]$路径)

    # 不限 dotnet.exe：占住 DLL 的确实通常是我们自己起的 dotnet 服务，但也可能是
    # 别的东西（杀毒扫描、资源管理器预览、某个编辑器）。全表扫一遍再按命令行过滤，
    # 几百行的开销换一条能直接照着做的信息，划算。
    $结果 = @()
    try {
        $候选 = Get-CimInstance Win32_Process -ErrorAction Stop
    } catch {
        return $结果
    }

    foreach ($进程 in $候选) {
        if ($进程.CommandLine -and $进程.CommandLine.Contains($路径)) {
            $结果 += [pscustomobject]@{ 进程号 = $进程.ProcessId; 名字 = $进程.Name; 命令行 = $进程.CommandLine }
        }
    }

    return $结果
}

<#
  影子拷贝一份编译产物。

  /R:2 /W:1 是这个函数存在的理由：robocopy 默认 /R:1000000 /W:30，
  撞上一个被占住的 DLL 就是无声死等（人看到的是「卡住了」，实际是它在等三十秒 × 一百万次）。
  两次重试、每次等一秒，撞上锁两秒内失败，然后把占着它的进程指出来。

  退出码 <8 都是成功（robocopy 用位标志报「拷了几类文件」，1=有文件被拷、2=有额外项…）；
  >=8 才是真失败。返回 $true / $false。
#>
function Copy-影子 {
    param(
        [Parameter(Mandatory)][string]$源,
        [Parameter(Mandatory)][string]$目标,
        [Parameter(Mandatory)][string]$标题
    )

    # 先看目标目录里有没有活着的进程在跑。有的话根本不用试：
    # 正在被映射的 DLL 在 Windows 上换不掉，robocopy 只会撞锁然后失败。
    # 与其让人从 robocopy 的错误码里反推，不如拷之前就把是谁指出来。
    $在用 = Get-占用进程 -路径 $目标
    if ($在用.Count -gt 0) {
        Write-Host ''
        Write-Host "不能往 $目标 拷：那个目录里正跑着进程，它占着自己的 DLL。"
        foreach ($条 in $在用) {
            Write-Host ("    PID {0}  {1}  {2}" -f $条.进程号, $条.名字, $条.命令行)
        }
        Write-Host '  先 pwsh Tools/stop.ps1 停干净再来（只想重起面板的话，助手不用停：起谁才镜像谁）。'
        return $false
    }

    $退出码 = Invoke-带进度 -标题 $标题 -文件 'robocopy' -参数 @(
        $源, $目标, '/MIR', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
    )

    if ($退出码 -lt 8) {
        return $true
    }

    Write-Host ''
    Write-Host "影子拷贝失败：$源 → $目标（robocopy 退出码 $退出码）"
    if ($退出码 -eq 16) {
        Write-Host '  退出码 16 = 拷贝根本没开始（源目录不存在、或目标盘写不了）。'
    } else {
        Write-Host '  多半是目标目录里有文件正被占用：常驻服务开着的时候，它自己的 DLL 就在那儿。'
    }

    $占用 = Get-占用进程 -路径 $目标
    if ($占用.Count -gt 0) {
        Write-Host '  命令行里出现过这个目录的进程（多半就是它）：'
        foreach ($条 in $占用) {
            Write-Host ("    PID {0}  {1}  {2}" -f $条.进程号, $条.名字, $条.命令行)
        }
    } else {
        Write-Host '  没找到命令行里带这个目录的进程；用这条自己查：'
        Write-Host '    Get-CimInstance Win32_Process | Where-Object CommandLine -like "*HSGFrameRun*" | Select ProcessId, CommandLine'
    }

    Write-Host '  下一步：先 pwsh Tools/stop.ps1 停干净，再重跑。'
    return $false
}
