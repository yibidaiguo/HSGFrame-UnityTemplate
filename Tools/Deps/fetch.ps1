<#
  取内置依赖：把 packages.txt 里每个包**及其全部传递依赖**还原、铺平到本目录。

  这就是「打包程序」。它存在的理由只有一条：手数传递依赖必然漏，
  而漏一个的症状是「换台机器才炸」。所以这里不数——让 NuGet 解闭包，脚本只负责搬。

  它一共产出四样东西，全部是**生成物**，不要手改：
    lib/            闭包里的托管 dll（铺平，一个程序集一份）
    native/<rid>/   原生库（SkiaSharp / HarfBuzzSharp 那一族，按 rid 分目录）
    props/<束>.props  给 csproj <Import> 用的引用片段
    nupkg/          铺不平的那几个包的原样 .nupkg，当本地 NuGet 源用
    LICENSES.md     两份清单里每个包的许可（内置 = 分发，这一步不能省）

  用法：
    pwsh -NoProfile -File ./fetch.ps1                      # 按清单重取全部（win-x64）
    pwsh -NoProfile -File ./fetch.ps1 -Rid linux-x64       # 换平台的原生库
    pwsh -NoProfile -File ./fetch.ps1 -Bundle SvgSkia      # 只重取一个束

  要联网（它就是「联网取一次，之后不用再联网」的那一次）。
  退出码：0 成功，1 失败。
#>
param(
    [string]$Rid = 'win-x64',
    [string]$Bundle = '',
    [string]$Tfm = 'net8.0'
)

$ErrorActionPreference = 'Stop'

# 临时工程的程序集名。刻意取一个不可能与任何 NuGet 包同名的名字，理由见下面用到它的地方。
$TempProjectName = '__deps_fetch_probe__'

$depsRoot = $PSScriptRoot
$libDir = Join-Path $depsRoot 'lib'
$nativeDir = Join-Path $depsRoot (Join-Path 'native' $Rid)
$propsDir = Join-Path $depsRoot 'props'
$nupkgDir = Join-Path $depsRoot 'nupkg'
$noticesDir = Join-Path $depsRoot 'NOTICES'
$manifestPath = Join-Path $depsRoot 'packages.txt'
$feedManifestPath = Join-Path $depsRoot 'packages-feed.txt'

function Write-Step($message) { Write-Host "[内置依赖] $message" }

if (-not (Test-Path $manifestPath)) {
    Write-Host "[内置依赖] 找不到清单：$manifestPath"
    exit 1
}

# ---- 读清单 ----------------------------------------------------------------
$bundles = [ordered]@{}
foreach ($line in Get-Content $manifestPath -Encoding UTF8) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
    $parts = $line -split "`t" | Where-Object { $_.Trim() -ne '' }
    if ($parts.Count -lt 3 -or $parts.Count -gt 4) {
        Write-Host "[内置依赖] 清单这行不是三或四列（束<TAB>包<TAB>版本[<TAB>目标框架]）：$line"
        exit 1
    }
    $name = $parts[0].Trim()
    # 第四列是目标框架，不写就按 $Tfm（net8.0）。**这一列不是摆设**：
    # 同一个包在不同目标框架下的闭包是不一样的。Roslyn 在 net8.0 只带 2 个程序集，
    # 在 netstandard2.0 要带 11 个（System.Collections.Immutable 那一串在 net8.0 是内置的）。
    # 源生成器工程只能是 netstandard2.0，按 net8.0 取就会少 9 个。
    $bundleTfm = if ($parts.Count -eq 4) { $parts[3].Trim() } else { $Tfm }
    if (-not $bundles.Contains($name)) { $bundles[$name] = @{ Tfm = $bundleTfm; Packages = @() } }
    $bundles[$name].Packages += ,@($parts[1].Trim(), $parts[2].Trim())
}

if ($Bundle -ne '') {
    if (-not $bundles.Contains($Bundle)) {
        Write-Host "[内置依赖] 清单里没有这个束：$Bundle（有的是：$($bundles.Keys -join '、')）"
        exit 1
    }
    $only = [ordered]@{}
    $only[$Bundle] = $bundles[$Bundle]
    $bundles = $only
}

# ---- 判一个 dll 是托管还是原生 ---------------------------------------------
# 原生库（libSkiaSharp.dll 之类）没有 CLI 头，GetAssemblyName 会抛 BadImageFormatException。
# 靠文件名猜是不行的——SkiaSharp 那一族恰好带 lib 前缀，别的包不一定。
function Test-ManagedAssembly([string]$path) {
    try { [System.Reflection.AssemblyName]::GetAssemblyName($path) | Out-Null; return $true }
    catch { return $false }
}

# ---- 把包自带的第三方声明收下来 --------------------------------------------
# SkiaSharp / HarfBuzzSharp 这类包在 THIRD-PARTY-NOTICES.txt 里列了上游（Skia、HarfBuzz、
# 以及它们各自依赖的一长串）的许可。**分发这些 dll 时那份声明也要跟着走**，
# 这跟包本身写的是 MIT 不冲突——MIT 管的是这个包，notice 管的是它打包进去的东西。
function Copy-PackageNotices([string]$packageDir, [string]$packageId) {
    if (-not (Test-Path $packageDir)) { return }
    foreach ($notice in Get-ChildItem -Path $packageDir -File -Filter '*NOTICES*' -ErrorAction SilentlyContinue) {
        Copy-Item $notice.FullName (Join-Path $noticesDir "$packageId$($notice.Extension)") -Force
    }
}

New-Item -ItemType Directory -Force -Path $libDir, $nativeDir, $propsDir, $nupkgDir, $noticesDir | Out-Null

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("deps-fetch-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

# 闭包里每个包的许可，跨束累计（同一个包可能被两个束共用）。
$licenseRows = [ordered]@{}
# 已经落过盘的托管程序集 → 它的程序集版本。跨束撞版本要当场停，不能后写覆盖先写：
# 那种覆盖是静默的，症状要到运行时才出现。
$seenAssemblies = @{}
$failed = $false

try {
    foreach ($bundleName in $bundles.Keys) {
        $packages = $bundles[$bundleName].Packages
        $bundleTfm = $bundles[$bundleName].Tfm
        Write-Step "束 $bundleName（$bundleTfm）：$(($packages | ForEach-Object { $_[0] + '@' + $_[1] }) -join '、')"

        $projDir = Join-Path $workRoot $bundleName
        New-Item -ItemType Directory -Force -Path $projDir | Out-Null
        $refs = ($packages | ForEach-Object { "    <PackageReference Include=""$($_[0])"" Version=""$($_[1])"" />" }) -join "`n"
        # 临时工程的名字**不能等于任何包名**：NuGet 会把「工程 X 引用包 X」判成循环依赖
        # （NU1108），而束名天然就是包名（ClosedXML、Scriban 都撞过）。用一个固定的怪名字避开。
        $projPath = Join-Path $projDir "$TempProjectName.csproj"
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$bundleTfm</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <!--
      CopyLocalLockFileAssemblies：**没有这一条，输出目录里只有工程自己那个 dll**。
      类库工程默认不把 NuGet 解出来的程序集拷到输出，而这个脚本要的正是那些程序集。
      （netstandard2.0 那档尤其明显：不开这条，Roslyn 的 11 个一个都不出现。）
    -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <NoWarn>NETSDK1057;NU1701</NoWarn>
  </PropertyGroup>
  <ItemGroup>
$refs
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projPath -Encoding UTF8

        # build -o 而不是 publish：publish 要求目标框架是可运行的，
        # 而 netstandard2.0 那档（源生成器）不是。build 配上上面那条 CopyLocalLockFileAssemblies
        # 一样能把整个闭包铺到输出目录，且两档走同一条路。
        # -r 只对可运行的框架给：原生库是按 rid 挑的，netstandard 那档既没有也不认这个参数。
        $outDir = Join-Path $projDir 'out'
        $ridArgs = @()
        if ($bundleTfm -notmatch '^netstandard') { $ridArgs = @('-r', $Rid, '--self-contained', 'false') }
        # RestorePackagesPath 指到工程自己的目录：这样下面读 nuspec 与 notice 时
        # 有一个确定的地方可找，不用去猜本机全局缓存在哪（NUGET_PACKAGES 可能被改过）。
        $pkgDir = Join-Path $projDir 'packages'
        $log = & dotnet build $projPath -c Release @ridArgs -o $outDir --nologo -p:RestorePackagesPath=$pkgDir 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[内置依赖] 束 $bundleName 还原/发布失败："
            $log | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
            $failed = $true
            continue
        }

        # ---- 搬 dll ----
        $managedNames = New-Object System.Collections.Generic.List[string]
        $nativeNames = New-Object System.Collections.Generic.List[string]
        foreach ($file in Get-ChildItem -Path $outDir -Filter *.dll -File) {
            if ($file.BaseName -eq $TempProjectName) { continue }   # 临时工程自己那份
            if (Test-ManagedAssembly $file.FullName) {
                $version = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName).Version.ToString()
                if ($seenAssemblies.ContainsKey($file.Name) -and $seenAssemblies[$file.Name] -ne $version) {
                    Write-Host "[内置依赖] 撞版本：$($file.Name) 先前是 $($seenAssemblies[$file.Name])，束 $bundleName 解出 $version。"
                    Write-Host "           两个束对同一个传递依赖解出了不同版本，铺平到一个 lib/ 会静默覆盖。"
                    Write-Host "           办法是在 packages.txt 里把那个传递依赖显式提到同一版本。"
                    $failed = $true
                    continue
                }
                $seenAssemblies[$file.Name] = $version
                Copy-Item $file.FullName (Join-Path $libDir $file.Name) -Force
                $managedNames.Add($file.BaseName)
            }
            else {
                Copy-Item $file.FullName (Join-Path $nativeDir $file.Name) -Force
                $nativeNames.Add($file.Name)
            }
        }

        Write-Step "  托管 $($managedNames.Count) 个、原生 $($nativeNames.Count) 个"

        # ---- 生成 props ----
        $sorted = $managedNames | Sort-Object
        $refLines = ($sorted | ForEach-Object {
            "    <Reference Include=""$_"">`n      <HintPath>`$(DepsLibDir)$_.dll</HintPath>`n      <Private>true</Private>`n    </Reference>"
        }) -join "`n"

        $nativeBlock = ''
        if ($nativeNames.Count -gt 0) {
            $nativeLines = ($nativeNames | Sort-Object | ForEach-Object {
                "    <None Include=""`$(DepsNativeDir)$_"">`n      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`n      <Link>$_</Link>`n    </None>"
            }) -join "`n"
            $nativeBlock = @"

  <!--
    原生库：跟 dll 平级拷到输出目录。
    **不能只挂 Reference**——原生库没有 CLI 头，引用不到它；
    而 SkiaSharp 那一族是在运行时按文件名去进程目录找的，所以必须真的拷过去。
    只有 $Rid 这一份。换平台要重跑 fetch.ps1 -Rid <别的>，理由见 SOURCE.md。
  -->
  <ItemGroup>
$nativeLines
  </ItemGroup>
"@
        }

        $propsPath = Join-Path $propsDir "$bundleName.props"
        @"
<!--
  $bundleName 的引用片段——**由 Tools/Deps/fetch.ps1 生成，不要手改**。
  改动请改 Tools/Deps/packages.txt 再重跑 fetch.ps1。

  顶层包：$(($packages | ForEach-Object { $_[0] + ' ' + $_[1] }) -join '、')（目标框架 $bundleTfm）
  下面列的是**整个闭包**（顶层包 + 全部传递依赖），共 $($sorted.Count) 个程序集。
  用法：在 csproj 里 <Import Project="..\Deps\props\$bundleName.props" />
-->
<Project>
  <PropertyGroup>
    <DepsRoot Condition="'`$(DepsRoot)' == ''">`$([System.IO.Path]::GetFullPath('`$(MSBuildThisFileDirectory)..\'))</DepsRoot>
    <DepsLibDir>`$(DepsRoot)lib\</DepsLibDir>
    <DepsNativeDir>`$(DepsRoot)native\$Rid\</DepsNativeDir>
  </PropertyGroup>

  <ItemGroup>
$refLines
  </ItemGroup>
$nativeBlock
</Project>
"@ | Set-Content -Path $propsPath -Encoding UTF8

        # ---- 记许可 ----
        $assets = Join-Path $projDir 'obj/project.assets.json'
        if (Test-Path $assets) {
            $json = Get-Content $assets -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($key in $json.libraries.PSObject.Properties.Name) {
                if ($json.libraries.$key.type -ne 'package') { continue }
                $id, $ver = $key -split '/'
                if ($licenseRows.Contains($key)) { continue }
                $packageDir = Join-Path (Join-Path $pkgDir $id.ToLowerInvariant()) $ver
                $nuspec = Join-Path $packageDir "$($id.ToLowerInvariant()).nuspec"
                Copy-PackageNotices $packageDir $id
                $license = '（nuspec 没找到）'
                $project = ''
                if (Test-Path $nuspec) {
                    [xml]$spec = Get-Content $nuspec -Raw -Encoding UTF8
                    $md = $spec.package.metadata
                    if ($md.license -and $md.license.'#text') { $license = $md.license.'#text' }
                    elseif ($md.license) { $license = [string]$md.license }
                    elseif ($md.licenseUrl) { $license = [string]$md.licenseUrl }
                    else { $license = '（包里没声明）' }
                    if ($md.projectUrl) { $project = [string]$md.projectUrl }
                }
                $licenseRows[$key] = [pscustomobject]@{
                    Id = $id; Version = $ver; License = $license; Project = $project; Bundle = $bundleName
                }
            }
        }
    }
}
finally {
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# ---- 本地源那一档：铺不平的包，整个 .nupkg 收下来 ------------------------
# 只在「重取全部」时做。-Bundle 是针对 packages.txt 的某一个束的，跟这一档无关。
if ($Bundle -eq '' -and (Test-Path $feedManifestPath)) {
    $feedPackages = @()
    foreach ($line in Get-Content $feedManifestPath -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $parts = $line -split "`t" | Where-Object { $_.Trim() -ne '' }
        if ($parts.Count -ne 3) {
            Write-Host "[内置依赖] 本地源清单这行不是三列（包<TAB>版本<TAB>目标框架）：$line"
            exit 1
        }
        $feedPackages += ,@($parts[0].Trim(), $parts[1].Trim(), $parts[2].Trim())
    }

    # 按目标框架分组：同一个框架下的包一次还原，闭包才解得对。
    $byTfm = @{}
    foreach ($entry in $feedPackages) {
        if (-not $byTfm.ContainsKey($entry[2])) { $byTfm[$entry[2]] = @() }
        $byTfm[$entry[2]] += ,$entry
    }

    $feedWork = Join-Path ([System.IO.Path]::GetTempPath()) ("deps-feed-" + [System.Guid]::NewGuid().ToString('N'))
    try {
        foreach ($tfm in $byTfm.Keys) {
            $entries = $byTfm[$tfm]
            Write-Step "本地源（$tfm）：$(($entries | ForEach-Object { $_[0] + '@' + $_[1] }) -join '、')"
            $projDir = Join-Path $feedWork $tfm
            New-Item -ItemType Directory -Force -Path $projDir | Out-Null
            $refs = ($entries | ForEach-Object { "    <PackageReference Include=""$($_[0])"" Version=""$($_[1])"" />" }) -join "`n"
            $projPath = Join-Path $projDir "$TempProjectName.csproj"
            @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$tfm</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>NETSDK1057;NU1701</NoWarn>
  </PropertyGroup>
  <ItemGroup>
$refs
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projPath -Encoding UTF8

            # --packages 指到一个空目录：还原会把闭包里**每个**包连同它的 .nupkg 落到那里。
            # 这样收到的就是完整闭包，而不是「本机全局缓存里恰好有的那些」。
            $packagesDir = Join-Path $projDir 'packages'
            $log = & dotnet restore $projPath --packages $packagesDir --nologo 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[内置依赖] 本地源 $tfm 还原失败："
                $log | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
                $failed = $true
                continue
            }

            $count = 0
            foreach ($nupkg in Get-ChildItem -Path $packagesDir -Filter *.nupkg -File -Recurse) {
                Copy-Item $nupkg.FullName (Join-Path $nupkgDir $nupkg.Name) -Force
                $count++
            }
            Write-Step "  收下 $count 个 .nupkg"

            # 许可照记：这一档同样是「dll 进 git」，义务一样。
            $assets = Join-Path $projDir 'obj/project.assets.json'
            if (Test-Path $assets) {
                $json = Get-Content $assets -Raw -Encoding UTF8 | ConvertFrom-Json
                foreach ($key in $json.libraries.PSObject.Properties.Name) {
                    if ($json.libraries.$key.type -ne 'package') { continue }
                    if ($licenseRows.Contains($key)) { continue }
                    $id, $ver = $key -split '/'
                    $packageDir = Join-Path (Join-Path $packagesDir $id.ToLowerInvariant()) $ver
                    $nuspec = Join-Path $packageDir "$($id.ToLowerInvariant()).nuspec"
                    Copy-PackageNotices $packageDir $id
                    $license = '（nuspec 没找到）'
                    $project = ''
                    if (Test-Path $nuspec) {
                        [xml]$spec = Get-Content $nuspec -Raw -Encoding UTF8
                        $md = $spec.package.metadata
                        if ($md.license -and $md.license.'#text') { $license = $md.license.'#text' }
                        elseif ($md.license) { $license = [string]$md.license }
                        elseif ($md.licenseUrl) { $license = [string]$md.licenseUrl }
                        else { $license = '（包里没声明）' }
                        if ($md.projectUrl) { $project = [string]$md.projectUrl }
                    }
                    $licenseRows[$key] = [pscustomobject]@{
                        Id = $id; Version = $ver; License = $license; Project = $project; Bundle = '本地源'
                    }
                }
            }
        }
    }
    finally {
        Remove-Item $feedWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failed) {
    Write-Host '[内置依赖] 有束失败，上面写了原因。lib/ 可能是半成品，修好再跑一次。'
    exit 1
}

# ---- 写 LICENSES.md --------------------------------------------------------
# 内置意味着 dll 进 git，许可从「引用」变成「分发」。这份表是那件事的账。
$rows = ($licenseRows.Values | Sort-Object Id | ForEach-Object {
    $link = if ($_.Project) { "[$($_.Id)]($($_.Project))" } else { $_.Id }
    "| $link | $($_.Version) | $($_.License) | $($_.Bundle) |"
}) -join "`n"

@"
# 内置依赖的许可

**由 ``Tools/Deps/fetch.ps1`` 生成，不要手改。**

这份表记的是 ``lib/`` 与 ``native/`` 里那些二进制的许可。
内置化之后 dll 进了 git，我们的身份就从「引用第三方库」变成「分发第三方库」——
这两件事的许可义务不一样，所以这份账必须存在，且必须跟着 ``packages.txt`` 一起变。

共 $($licenseRows.Count) 个包（$($bundles.Keys.Count) 个束的闭包合起来，去重后）。

| 包 | 版本 | 许可 | 属于哪个束 |
|---|---|---|---|
$rows

## 要盯着的两条

- **SixLabors.ImageSharp 钉在 2.1.x** —— 2.1 是 Apache-2.0 的最后一支。
  3.x 起换成 Six Labors 分割许可（开源与小企业免费，商用要买）。
  升级这一个包等于换许可，**不是版本号问题**，要单独拍板。
- **SkiaSharp / HarfBuzzSharp 带原生库**，各自的 ``THIRD-PARTY-NOTICES.txt``
  列了 Skia 与 HarfBuzz 上游一长串依赖的许可。分发这些 ``.dll`` 时那份 notice 也要带上，
  已随包收在 ``NOTICES/`` 下（$((Get-ChildItem $noticesDir -File -ErrorAction SilentlyContinue).Count) 份）。
"@ | Set-Content -Path (Join-Path $depsRoot 'LICENSES.md') -Encoding UTF8

Write-Step "完成：lib/ $((Get-ChildItem $libDir -Filter *.dll).Count) 个托管 dll，native/$Rid/ $((Get-ChildItem $nativeDir -Filter *.dll -ErrorAction SilentlyContinue).Count) 个原生 dll，props/ $($bundles.Keys.Count) 份，nupkg/ $((Get-ChildItem $nupkgDir -Filter *.nupkg -ErrorAction SilentlyContinue).Count) 个，许可 $($licenseRows.Count) 条"
exit 0
