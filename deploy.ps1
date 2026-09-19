<#
  ═══ 本地部署：构建 v2 → 静默覆盖安装 → 启动 ═══

  为什么需要它：平时点桌面 / 开始菜单的「Windows优化工具箱」快捷方式，指向的是
  **安装副本** `%LOCALAPPDATA%\Programs\WinTuneBox\WinTuneBox.exe`。
  只跑 `build.ps1` 只更新发行版目录，安装副本还是旧版 —— 表现就是
  「代码明明改好了，打开界面还是老样子」。本脚本把「dev 树 → 安装副本」这步补齐，
  并在部署后校验哈希，避免再出现两处 exe 不一致。

  用法:
    powershell -ExecutionPolicy Bypass -File deploy.ps1
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -Arch x86
    powershell -ExecutionPolicy Bypass -File deploy.ps1 -SkipBuild -NoLaunch
#>
param(
    [ValidateSet('x64', 'x86')][string]$Arch = 'x64',
    [switch]$SkipBuild,
    [switch]$NoLaunch,
    [switch]$Sign,
    # 发行版根目录（构建产物所在，见 tools\paths.ps1）
    [string]$OutRoot = ''
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
. (Join-Path $root 'tools\paths.ps1') -OutRoot $OutRoot

# 版本号单一来源：build.ps1 里的 $v2ver
# （原来这里把版本写死成 2.0.0，改版本号就会找不到安装包）
$v2ver = [regex]::Match((Get-Content (Join-Path $root 'build.ps1') -Raw -Encoding UTF8),
                        '\$v2ver\s*=\s*''([\d.]+)''').Groups[1].Value
if (-not $v2ver) { throw '无法从 build.ps1 解析 v2 版本号' }

$distExe = Join-Path $OutDist "$Arch\WinTuneBox.exe"
$setup   = Join-Path $OutDist "Setup-Windows优化工具箱-$v2ver-$Arch.exe"
$instDir = Join-Path $env:LOCALAPPDATA 'Programs\WinTuneBox'
$instExe = Join-Path $instDir 'WinTuneBox.exe'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host $m -ForegroundColor Green }
function Warn($m) { Write-Host $m -ForegroundColor Yellow }

# 1) 构建（含只读自检与安装包）
if (-not $SkipBuild) {
    Step "构建 v2 / $Arch ..."
    $bp = @('-Arch', $Arch)
    if ($Sign) { $bp += '-Sign' }
    if ($OutRoot) { $bp += @('-OutRoot', $OutRoot) }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') @bp
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 失败 (exit=$LASTEXITCODE)" }
} else {
    Warn "跳过构建，直接使用现有产物: $OutDist"
}

if (-not (Test-Path $distExe)) { throw "找不到 $distExe" }
if (-not (Test-Path $setup))   { throw "找不到安装包 $setup（去掉 -SkipBuild 或补跑 build.ps1）" }

# 2) 关掉占用文件的运行实例（否则 Inno 无法覆盖 exe）
$running = Get-Process WinTuneBox, WinOptimizer -ErrorAction SilentlyContinue
if ($running) {
    Step "关闭正在运行的实例（$($running.Count) 个）..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 900
}

# 3) 静默覆盖安装
Step "静默安装 $([IO.Path]::GetFileName($setup)) ..."
$p = Start-Process -FilePath $setup -ArgumentList '/SILENT', '/NORESTART', '/SUPPRESSMSGBOXES', '/NOCANCEL' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "安装失败 (exit=$($p.ExitCode))" }
Start-Sleep -Milliseconds 800

# 4) 校验 dev 树与安装副本一致
$want = (Get-FileHash $distExe -Algorithm SHA256).Hash
$got  = if (Test-Path $instExe) { (Get-FileHash $instExe -Algorithm SHA256).Hash } else { '<缺失>' }
if ($want -ne $got) { throw "安装副本哈希不一致`n  dist     : $want`n  installed: $got" }
Ok "安装副本已更新: $instExe"
Ok "SHA256 一致: $want"

# 5) 启动
if (-not $NoLaunch) {
    Step '启动 ...'
    Start-Process -FilePath $instExe | Out-Null
    Ok '已启动'
}