<#
  ═══ WinTuneBox 构建脚本 ═══
  v1 = 冻结的 WinForms 版（src\，只保证可复现构建）
  v2 = 当前主线 WPF 版（wpf\WinTuneBox.csproj，真 MVVM + 自绘 Fluent UI）

  用法:
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Target v1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Arch both
    powershell -ExecutionPolicy Bypass -File build.ps1 -SkipSelfTest -NoInstaller

  参数:
    -Target      v1 | v2            默认 v2
    -OutRoot     <路径>             发行版根目录（构建产物落在这里，默认与源码目录同级）
    -Arch        x64 | x86 | both   默认 x64（v1 为 AnyCPU 单文件，忽略该项）
    -SkipSelfTest                    跳过构建后自检
    -NoInstaller                     不生成 Inno 安装包
    -Sign                            构建后给 dist\ 产物加代码签名（证书见 tools\make-cert.ps1）
    -InstallerOnly                   跳过编译，只把现有 dist\<arch>\WinTuneBox.exe 打成安装包
                                     （SignPath 流程需要：先签主程序 → 再打安装包 → 再签安装包）
    -AppPublisher / -AppCopyright    覆盖安装包里的发布者与版权声明（留空 = 用 installer.iss 默认值）

  依赖: MSBuild 4.0（.NET Framework 4.8，无需 .NET SDK）、csc.exe、D:\App\InnoSetup6\ISCC.exe
#>
[CmdletBinding()]
param(
    [ValidateSet('v1', 'v2')][string]$Target = 'v2',
    [ValidateSet('x64', 'x86', 'both')][string]$Arch = 'x64',
    [switch]$SkipSelfTest,
    [switch]$NoInstaller,
    [switch]$Sign,
    [switch]$InstallerOnly,
    # 安装包元数据覆盖（默认值 = installer.iss 里的默认值，不影响产物可复现性）
    [string]$AppPublisher = '',
    [string]$AppCopyright = '',
    # 发行版根目录：源码仓库里不产生任何构建产物（见 tools\paths.ps1）
    [string]$OutRoot = ''
)

$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
. (Join-Path $root 'tools\paths.ps1') -OutRoot $OutRoot
$dist   = $OutDist                      # 产物全部落在发行版目录，源码树保持干净
$icon   = Join-Path $root 'assets\app.ico'
$csc    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$msb    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
$msb32  = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
function Find-Iscc {
    if ($env:ISCC -and (Test-Path $env:ISCC)) { return $env:ISCC }
    foreach ($c in @(
        'D:\App\InnoSetup6\ISCC.exe',
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return 'D:\App\InnoSetup6\ISCC.exe'   # 保持旧默认值，让后续 Test-Path 给出原有提示
}
$iscc   = Find-Iscc
$v1ver  = '1.0.15'
$v2ver  = '2.0.0'

function Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }

# 本机优先用 .NET Framework 自带的编译器；找不到时回退到 PATH（例如 CI 上的 VS 版 MSBuild）
function Resolve-Tool([string]$preferred, [string]$name) {
    if (Test-Path $preferred) { return $preferred }
    $alt = Get-Command $name -ErrorAction SilentlyContinue
    if ($alt) { Write-Host "  使用 PATH 中的 $name : $($alt.Source)" -ForegroundColor Yellow; return $alt.Source }
    return $preferred
}
function Ok($msg)    { Write-Host $msg -ForegroundColor Green }
function Warn($msg)  { Write-Host $msg -ForegroundColor Yellow }

$csc  = Resolve-Tool $csc  'csc.exe'
$msb  = Resolve-Tool $msb  'MSBuild.exe'
$msb32 = Resolve-Tool $msb32 'MSBuild.exe'

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

function Ensure-Icon {
    if (Test-Path $icon) { return }
    Step '生成 app.ico ...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'assets\make-icon.ps1')
    if (-not (Test-Path $icon)) { throw 'app.ico 生成失败' }
}

function Get-ArchList {
    if ($Arch -eq 'both') { return @('x64', 'x86') }
    return @($Arch)
}

# ────────────────────────────── v1 ──────────────────────────────
function Build-V1 {
    Step '构建 v1（WinForms，csc）'
    Ensure-Icon
    $src = Join-Path $root 'src'
    $out = Join-Path $dist 'WinOptimizer.exe'
    $a = New-Object System.Collections.Generic.List[string]
    $a.AddRange([string[]]@(
        '/target:winexe', "/out:$out", '/codepage:65001', '/nologo', '/optimize+',
        '/r:System.ServiceProcess.dll',
        "/win32icon:$icon", "/win32manifest:$(Join-Path $src 'app.manifest')"
    ))
    Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $a.Add($_.FullName) }
    & $csc $a 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "v1 编译失败 (csc exit=$LASTEXITCODE)" }
    Ok "v1 编译成功: $out"
    return @{ Exe = $out; Ver = $v1ver; ExeName = 'WinOptimizer.exe'; SrcDir = 'dist' }
}

function SelfTest-V1($exe) {
    Step '运行 v1 自检 ...'
    Push-Location $dist
    try { & $exe --selftest } finally { Pop-Location }
    Start-Sleep -Milliseconds 300
    if (Test-Path (Join-Path $dist 'selftest.txt')) { Ok '自检报告: dist\selftest.txt' }
}

function Installer-V1 {
    if (-not (Test-Path $iscc)) { Warn '跳过安装包（找不到 ISCC.exe）'; return }
    Step '打 v1 安装包 ...'
    Push-Location $root
    try {
        $defs = @("/DAppVer=$v1ver", "/DSrcDir=$dist", "/DOutDir=$dist")
        if ($AppPublisher) { $defs += "/DAppPublisher=$AppPublisher" }
        if ($AppCopyright) { $defs += "/DAppCopyright=$AppCopyright" }
        & $iscc @defs installer.iss 2>&1 | ForEach-Object { Write-Host $_ }
    } finally { Pop-Location }
    Ok "v1 安装包: $dist"
}

# ────────────────────────────── v2 ──────────────────────────────
function Build-V2Arch([string]$a) {
    Step "构建 v2 / $a（MSBuild 4.0）"
    $proj = Join-Path $root 'wpf\WinTuneBox.csproj'
    if (-not (Test-Path $proj)) { throw "找不到 $proj" }
    # x86 必须用 32 位 MSBuild，否则引用会从 Framework64 解析并报 MSB3270 架构不匹配
    $mb = if ($a -eq 'x86') { $msb32 } else { $msb }
    # OutputPath / BaseIntermediateOutputPath 都重定向到发行版目录：
    # 源码树里不留下 bin\ obj\（命令行 /p: 会覆盖 csproj 里的同名属性）
    & $mb $proj /t:Rebuild /p:Configuration=Release "/p:PlatformTarget=$a" `
        "/p:OutputPath=$OutBin\$a\" "/p:BaseIntermediateOutputPath=$OutObj\$a\" `
        /nologo /v:m /clp:NoSummary 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "v2/$a 编译失败 (MSBuild exit=$LASTEXITCODE)" }
    $outExe = Join-Path $OutBin "$a\WinTuneBox.exe"
    if (-not (Test-Path $outExe)) { throw "未生成 $outExe" }
    $dstDir = Join-Path $dist $a
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir | Out-Null }
    Copy-Item $outExe (Join-Path $dstDir 'WinTuneBox.exe') -Force
    Ok "v2/$a 编译成功: $dstDir\WinTuneBox.exe"
    return (Join-Path $dstDir 'WinTuneBox.exe')
}

function SelfTest-V2($exe) {
    Step '运行 v2 只读自检 ...'
    $p = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { Warn "自检退出码 $($p.ExitCode)（详见日志）" } else { Ok '自检通过' }
}

function Installer-V2([string]$a) {
    if (-not (Test-Path $iscc)) { Warn '跳过安装包（找不到 ISCC.exe）'; return }
    Step "打 v2/$a 安装包 ..."
    $defs = @(
        "/DAppVer=$v2ver",
        '/DExeName=WinTuneBox.exe',
        '/DAppIdV2',
        "/DSrcDir=$dist\$a",
        "/DOutDir=$dist",
        "/DArchSuffix=-$a",
        '/DKeepUserData'
    )
    if ($a -eq 'x64') { $defs += '/DArchX64' } else { $defs += '/DArchX86' }
    if ($AppPublisher) { $defs += "/DAppPublisher=$AppPublisher" }
    if ($AppCopyright) { $defs += "/DAppCopyright=$AppCopyright" }
    Push-Location $root
    try {
        & $iscc $defs installer.iss 2>&1 | ForEach-Object { Write-Host $_ }
    } finally { Pop-Location }
    Ok "v2/$a 安装包已输出到 $dist"
}

# ────────────────────────────── 主流程 ──────────────────────────────
Write-Host ''
Step "WinTuneBox 构建：Target=$Target  Arch=$Arch"
Write-Host ''

# 签名顺序很重要：
#   先签主程序 → 再打安装包（安装包内嵌的就是已签名的 exe）→ 最后签安装包自身。
#   反过来做的话，安装包会内嵌未签名的主程序，装完又变成「未知发布者」。
function Sign-Files([string[]]$files) {
    if (-not $Sign) { return }
    $files = @($files | Where-Object { $_ -and (Test-Path $_) })
    if ($files.Count -eq 0) { return }
    Step '代码签名 ...'
    # powershell -File 传不了数组，只能把清单落盘再交给 sign.ps1
    $list = Join-Path $env:TEMP 'wintunebox-sign-list.txt'
    # 必须无 BOM：带 BOM 时第一条路径会多出 U+FEFF 前缀而找不到文件
    [IO.File]::WriteAllLines($list, [string[]]$files, (New-Object System.Text.UTF8Encoding($false)))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\sign.ps1') -ListFile $list -OutRoot $OutRoot
    if ($LASTEXITCODE -ne 0) { Warn "签名未全部成功 (exit=$LASTEXITCODE)" }
}
function Sign-Installers {
    if (-not $Sign) { return }
    Sign-Files @(Get-ChildItem (Join-Path $dist 'Setup-*.exe') -File -ErrorAction SilentlyContinue |
                 ForEach-Object { $_.FullName })
}

if ($Target -eq 'v1') {
    $r = Build-V1
    Sign-Files @($r.Exe)
    if (-not $SkipSelfTest) { SelfTest-V1 $r.Exe }
    if (-not $NoInstaller)  { Installer-V1; Sign-Installers }
} elseif ($InstallerOnly) {
    # 只打安装包：dist\<arch>\WinTuneBox.exe 必须已经存在（且通常已经签好名）
    foreach ($a in Get-ArchList) {
        $exe = Join-Path $dist "$a\WinTuneBox.exe"
        if (-not (Test-Path $exe)) { throw "缺少 $exe —— -InstallerOnly 需要先构建（或用已签名的 exe 填充 dist\$a）" }
        Installer-V2 $a
    }
    if ($Sign) { Sign-Installers }
} else {
    foreach ($a in Get-ArchList) {
        $exe = Build-V2Arch $a
        Sign-Files @($exe)
        if (-not $SkipSelfTest) { SelfTest-V2 $exe }
        if (-not $NoInstaller)  { Installer-V2 $a }
    }
    Sign-Installers
}

Write-Host ''
Ok '构建完成。'