<#
  门禁总编排：按由快到慢的顺序跑完全部检查，任何一道红就地停下。

  用法：
    .\gate.ps1 [-RepositoryRoot <仓库根目录>]

  检查逻辑全部在 C# 侧（Toolkit.Gates + 命令宿主），这个脚本只负责调用与汇总，
  这样执行后端也能用 dotnet test 自己验证门禁本身。

  退出码：0 全绿，1 有门禁不过，2 环境问题（工程或命令宿主找不到）。
#>
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

# 路径从脚本自身位置推，而不是写死 "Template/"：
# 阶段 14 的生成脚本会把这棵树整个复制成别的名字，写死目录名到那边就断了。
$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if (-not $RepositoryRoot) {
    # 模板可能是仓库里的一个子目录，也可能自己就是仓库根（独立模板仓库）。
    # 靠 .git 判断，别假设「模板根的上一级就是仓库根」。
    $RepositoryRoot = if (Test-Path (Join-Path $templateRoot '.git')) { $templateRoot } else { Join-Path $templateRoot '..' }
}
$RepositoryRoot = (Resolve-Path $RepositoryRoot).Path

$templateRelativeName = [System.IO.Path]::GetRelativePath($RepositoryRoot, $templateRoot).Replace('\', '/')
$solutionPath = Join-Path $templateRoot 'Solutions/Template.sln'
$commandHostProject = Join-Path $templateRoot 'Tools/Cli/CommandHost/CommandHost.csproj'

foreach ($requiredPath in @($solutionPath, $commandHostProject)) {
    if (-not (Test-Path $requiredPath)) {
        Write-Host "[gate] 找不到 $requiredPath"
        exit 2
    }
}

$failedGateNames = @()

function Write-GateHeader {
    param([string]$GateName)
    Write-Host ''
    Write-Host "[gate] ==== $GateName ===="
}

function Invoke-GateCommand {
    param([string]$CommandName, [hashtable]$CommandArguments)

    $argumentsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-{0}.json" -f ($CommandName -replace '\.', '-'))
    $CommandArguments | ConvertTo-Json -Depth 5 | Set-Content -Path $argumentsPath -Encoding utf8

    # Out-Host 是必需的：不消费掉子进程的输出，它会连同退出码一起成为函数的返回值，
    # 调用方拿到的就是数组而不是整数，判定永远为「失败」。
    & dotnet run --project $commandHostProject --verbosity quiet -- run $CommandName --arguments-file $argumentsPath | Out-Host
    return $LASTEXITCODE
}

# 秒级：纯 C# 测试。改一处就该跑的那一道。
Write-GateHeader '秒级门禁 · dotnet test'
& dotnet test $solutionPath --nologo
if ($LASTEXITCODE -ne 0) { $failedGateNames += '秒级门禁（dotnet test）' }

# 十秒级：全解决方案编译，.editorconfig 的命名规则也在这一步报出来。
Write-GateHeader '十秒级门禁 · dotnet build'
& dotnet build $solutionPath --nologo
if ($LASTEXITCODE -ne 0) { $failedGateNames += '十秒级门禁（dotnet build）' }

Write-GateHeader '命名与注释规范'
if ((Invoke-GateCommand -CommandName 'gate.naming' -CommandArguments @{ RootDirectory = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '命名检查器'
}

Write-GateHeader '模块边界'
if ((Invoke-GateCommand -CommandName 'gate.moduleboundary' -CommandArguments @{ ScriptsRootDirectory = (Join-Path $templateRoot 'UnityProject/Assets/Game/Scripts'); ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '模块边界'
}

Write-GateHeader '业务层裸日志'
if ((Invoke-GateCommand -CommandName 'gate.businesslog' -CommandArguments @{ ScriptsRootDirectory = (Join-Path $templateRoot 'UnityProject/Assets/Game/Scripts'); ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '业务层裸日志'
}

Write-GateHeader '装配对账'
if ((Invoke-GateCommand -CommandName 'gate.assemblylink' -CommandArguments @{ ProjectFilePath = (Join-Path $templateRoot 'Solutions/Logic.Core/Logic.Core.csproj'); ScriptsRootDirectory = (Join-Path $templateRoot 'UnityProject/Assets/Game/Scripts') }) -ne 0) {
    $failedGateNames += '装配对账'
}

Write-GateHeader '可选功能引用范围'
if ((Invoke-GateCommand -CommandName 'gate.featurescope' -CommandArguments @{ TemplateRoot = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '可选功能引用范围'
}

Write-GateHeader '通用性检查'
if ((Invoke-GateCommand -CommandName 'gate.generic' -CommandArguments @{ RootDirectory = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '通用性检查'
}

Write-GateHeader '测试基线锁'
if ((Invoke-GateCommand -CommandName 'gate.baseline' -CommandArguments @{ TemplateRoot = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '测试基线锁'
}

Write-GateHeader '改动文件白名单'
$changedPaths = @()
$editorOwnedPaths = @()
# 生成出来的新项目可能还没 git init，那时白名单没有输入，视为无改动而不是让整条门禁崩掉。
$statusLines = @()
# git 输出的是 UTF-8 字节，PowerShell 默认按控制台代码页（中文 Windows 上是 936）解码，
# 于是中文路径会被解成乱码，白名单前缀永远匹配不上。这里临时把解码方式钉成 UTF-8。
# 另外 core.quotepath=false 让 git 直接吐原文，而不是 \346\224\271 这样的八进制转义。
$previousOutputEncoding = [Console]::OutputEncoding
try {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
    $statusLines = & git -C $RepositoryRoot -c core.quotepath=false status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { $statusLines = @() }
} catch {
    $statusLines = @()
} finally {
    [Console]::OutputEncoding = $previousOutputEncoding
}

$editorOwnedPrefixes = @()
$gateConfigPath = Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json'
# 宿主专属的两项（白名单前缀、编辑器自有目录）住在 gate-config.host.json 里，
# 那个文件不参与模板同步。同名项以宿主那份为准，宿主没写才回落到通用配置。
$hostGateConfigPath = Join-Path $templateRoot 'Tools/Gates/Config/gate-config.host.json'
foreach ($candidatePath in @($gateConfigPath, $hostGateConfigPath)) {
    if (Test-Path $candidatePath) {
        $candidateConfig = Get-Content -Raw -Path $candidatePath | ConvertFrom-Json
        if ($candidateConfig.editorOwnedPathPrefixes) { $editorOwnedPrefixes = @($candidateConfig.editorOwnedPathPrefixes) }
    }
}

foreach ($statusLine in $statusLines) {
    $path = $statusLine.Substring(3).Trim('"')
    # 重命名条目形如 "旧路径 -> 新路径"，白名单只关心落点。
    if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }

    # 有些目录由常驻的 Unity 编辑器进程自己写（Addressables 组、场景、Library/），
    # 工具链两侧对它们都是 deny，所以在白名单判定前摘出去，只报一行知会。
    # 目录名是每个宿主项目自己的事，写在 gate-config.json 的 editorOwnedPathPrefixes 里，
    # 别焊进这个脚本——模板不该知道宿主的旧工程叫什么。
    $isEditorOwned = $false
    foreach ($prefix in $editorOwnedPrefixes) {
        if ($path.StartsWith($prefix)) { $isEditorOwned = $true; break }
    }
    if ($isEditorOwned) { $editorOwnedPaths += $path } else { $changedPaths += $path }
}
if ($editorOwnedPaths.Count -gt 0) {
    Write-Host "[gate] 知会：编辑器自有目录下有 $($editorOwnedPaths.Count) 处变化，未计入白名单判定"
}
if ((Invoke-GateCommand -CommandName 'gate.whitelist' -CommandArguments @{ ChangedPathsText = ($changedPaths -join "`n"); ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '改动文件白名单'
}

Write-GateHeader '文档长度'
if ((Invoke-GateCommand -CommandName 'gate.doc' -CommandArguments @{ RepositoryRoot = $RepositoryRoot; TemplateRoot = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '文档长度'
}

# 创作管线门禁：池子校验、扩展合法性、供给对账、下游边界、层边界五道。
# 前两道管池子数据本身，供给对账管产物与数据的一致性，下游/层边界管引擎与产品层的耦合纪律。
Write-GateHeader '池子校验'
if ((Invoke-GateCommand -CommandName 'pool.validate' -CommandArguments @{ PoolRoot = (Join-Path $templateRoot 'Pools') }) -ne 0) {
    $failedGateNames += '池子校验'
}

Write-GateHeader '扩展合法性'
if ((Invoke-GateCommand -CommandName 'schema.check' -CommandArguments @{ PoolRoot = (Join-Path $templateRoot 'Pools'); EntityName = '需求' }) -ne 0) {
    $failedGateNames += '扩展合法性'
}

Write-GateHeader '供给对账'
if ((Invoke-GateCommand -CommandName 'gate.provision' -CommandArguments @{ RepositoryRoot = $templateRoot; PoolRoot = (Join-Path $templateRoot 'Pools') }) -ne 0) {
    $failedGateNames += '供给对账'
}

Write-GateHeader '下游边界'
if ((Invoke-GateCommand -CommandName 'gate.bridgeboundary' -CommandArguments @{ RepositoryRoot = $templateRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '下游边界'
}

Write-GateHeader '层边界'
if ((Invoke-GateCommand -CommandName 'gate.layerboundary' -CommandArguments @{ RepositoryRoot = $templateRoot; UnityAssetsDirectory = (Join-Path $templateRoot 'UnityProject/Assets'); ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '层边界'
}

Write-GateHeader '资产规格'
if ((Invoke-GateCommand -CommandName 'gate.assetspec' -CommandArguments @{ RepositoryRoot = $templateRoot }) -ne 0) {
    $failedGateNames += '资产规格'
}

Write-GateHeader '配方门禁'
if ((Invoke-GateCommand -CommandName 'gate.recipe' -CommandArguments @{ RepositoryRoot = $templateRoot }) -ne 0) {
    $failedGateNames += '配方门禁'
}

Write-GateHeader '放行策略'
if ((Invoke-GateCommand -CommandName 'gate.release' -CommandArguments @{ RepositoryRoot = $templateRoot }) -ne 0) {
    $failedGateNames += '放行策略'
}

# 生成物幂等：仓库里已生成的产物必须与当前 schema / 定义一致，谁手改了产物这里就会红。
Write-GateHeader '生成物幂等'
$idempotencyFailed = $false

if ((Invoke-GateCommand -CommandName 'codegen.run' -CommandArguments @{ TemplateRoot = $templateRoot; VerifyOnly = $true }) -ne 0) {
    $idempotencyFailed = $true
}

$uiDefinitionsDirectory = Join-Path $templateRoot 'UI/Definitions'

# 产物落在 Game.View 的源码树里，不是仓库根的 UI/Generated：落在仓库根时 Unity 编译不到、
# Logic.Core 又因为零 UnityEngine 铁律链接不了，于是「UI 单一事实源」这条管线是断的——
# 幂等门禁照样绿，因为它只比对生成器输出与磁盘文件，不管有没有人编译。
# `_Generated` 在下划线白名单里，归 Game.View 合规（《结构规范-代码》第三节）。
$uiGeneratedDirectory = Join-Path $templateRoot 'UnityProject/Assets/Game/Scripts/View/_Generated'
$uiDefinitions = @()
if (Test-Path $uiDefinitionsDirectory) {
    $uiDefinitions = @(Get-ChildItem $uiDefinitionsDirectory -Filter '*.uidef.json')
}

# 刚生成的新项目还没有面板定义，这时没有可校验的产物，跳过而不是让整条门禁崩掉。
if ($uiDefinitions.Count -eq 0) {
    Write-Host "[gate] 知会：$uiDefinitionsDirectory 下没有面板定义，跳过 ui.scaffold 的幂等校验"
} else {
    foreach ($definition in $uiDefinitions) {
        if ((Invoke-GateCommand -CommandName 'ui.scaffold' -CommandArguments @{
                DefinitionPath = $definition.FullName
                OutputDirectory = $uiGeneratedDirectory
                TemplateRoot = $templateRoot
                VerifyOnly = $true
            }) -ne 0) {
            $idempotencyFailed = $true
        }
    }
}

if ($idempotencyFailed) {
    $failedGateNames += '生成物幂等'
}

# Agent 入口镜像对账（R9）：CLAUDE.md 是源，AGENTS.md 等是镜像。
# 改了源却没重跑同步脚本，各家模型看到的入口就分叉了——这一道把分叉钉在提交前。
Write-GateHeader 'Agent 入口镜像'
$agentSyncScript = Join-Path $templateRoot 'Tools/AgentSync/agent-sync.ps1'
if (Test-Path $agentSyncScript) {
    & $agentSyncScript -Verify | Out-Host
    if ($LASTEXITCODE -ne 0) { $failedGateNames += 'Agent 入口镜像' }
} else {
    Write-Host "[gate] 知会：$agentSyncScript 不在，跳过 Agent 入口镜像对账"
}

Write-Host ''
if ($failedGateNames.Count -gt 0) {
    Write-Host "[gate] FAIL —— 未通过：$($failedGateNames -join '、')"
    exit 1
}

Write-Host '[gate] PASS —— 全部门禁全绿'
exit 0
