# ═══ WinTuneBox 发行打包脚本 ═══
# 1) 调用 build.ps1 构建 v2 的 x64 / x86 绿色版与安装包
# 2) 整理到 <发行版根目录>\release\：
#    - WinTuneBox-v2.0.0-x64-绿色版.zip / -x86-绿色版.zip
#    - Setup-Windows优化工具箱-2.0.0-x64.exe / -x86.exe
#    - SHA256SUMS.txt（校验和）
# 3) 手动把上述文件上传到 GitHub Releases 即可
#
# 用法: powershell -ExecutionPolicy Bypass -File make-release.ps1 [-Arch x64|x86|both] [-SkipSelfTest] [-Sign]
#   -Sign  给产物加代码签名。签名在 zip 打包**之前**完成（zip 里的 exe 也带签名），
#          因此 SHA256SUMS.txt 记录的是签名后的哈希。
[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'both')][string]$Arch = 'both',
    [switch]$SkipSelfTest,
    [switch]$Sign,
    # 发行版根目录（默认与源码目录同级，见 tools\paths.ps1）
    [string]$OutRoot = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'tools\paths.ps1') -OutRoot $OutRoot
$rel  = $OutRelease
$dist = $OutDist

# 版本号单一来源：build.ps1 里的 $v2ver
$build = Get-Content (Join-Path $root 'build.ps1') -Raw -Encoding UTF8
$ver = [regex]::Match($build, '\$v2ver\s*=\s*''([\d.]+)''').Groups[1].Value
if (-not $ver) { throw '无法从 build.ps1 解析 v2 版本号' }
Write-Host "发行版本: $ver" -ForegroundColor Cyan

$archs = if ($Arch -eq 'both') { @('x64', 'x86') } else { @($Arch) }

# ── 1. 构建 ──
Write-Host "==> 构建 v2 绿色版与安装包（$($archs -join ', ')）..." -ForegroundColor Cyan
$bp = @('-Target', 'v2', '-Arch', $Arch)
if ($SkipSelfTest) { $bp += '-SkipSelfTest' }
if ($Sign) { $bp += '-Sign' }
if ($OutRoot) { $bp += @('-OutRoot', $OutRoot) }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') @bp
if ($LASTEXITCODE -ne 0) { throw "build.ps1 失败 (exit=$LASTEXITCODE)" }

# ── 2. 整理发行目录 ──
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Path $rel | Out-Null

$docs = @('README.md', 'DEVELOPMENT.md', 'LICENSE', 'CHANGELOG.md')
foreach ($a in $archs) {
    $exe = Join-Path $dist "$a\WinTuneBox.exe"
    if (-not (Test-Path $exe)) { throw "缺少构建产物: $exe" }

    # 绿色版 zip：exe + 文档
    $tmp = Join-Path $rel "zip-$a"
    New-Item -ItemType Directory -Path $tmp | Out-Null
    Copy-Item $exe (Join-Path $tmp 'WinTuneBox.exe')
    foreach ($d in $docs) { Copy-Item (Join-Path $root $d) (Join-Path $tmp $d) }
    Copy-Item (Join-Path $root 'disclaimer.txt') (Join-Path $tmp '免责声明.txt')
    # 自签名方案的根证书随包分发：用户可选择导入信任（不导入也不影响使用，只是显示「未知发布者」）
    $rootCer = Join-Path $OutSigning 'WinTuneBox-RootCA.cer'
    if (Test-Path $rootCer) { Copy-Item $rootCer (Join-Path $tmp 'WinTuneBox-RootCA.cer') }

    $zipName = "WinTuneBox-v$ver-$a-绿色版.zip"
    Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath (Join-Path $rel $zipName) -CompressionLevel Optimal
    Remove-Item $tmp -Recurse -Force
    Write-Host "  绿色版: $zipName" -ForegroundColor Green

    # 安装包（规范命名，直接取 Inno 产物）
    $setup = Join-Path $dist "Setup-Windows优化工具箱-$ver-$a.exe"
    if (-not (Test-Path $setup)) { throw "缺少安装包: $setup" }
    $setupName = "Setup-Windows优化工具箱-$ver-$a.exe"
    Copy-Item $setup (Join-Path $rel $setupName)
    Write-Host "  安装包: $setupName" -ForegroundColor Green
}

# 根证书也放一份到 release\ 顶层，方便随 GitHub Release 一起上传
$rootCer = Join-Path $OutSigning 'WinTuneBox-RootCA.cer'
if (Test-Path $rootCer) {
    Copy-Item $rootCer (Join-Path $rel 'WinTuneBox-RootCA.cer')
    Write-Host '  根证书: WinTuneBox-RootCA.cer（用户可选导入）' -ForegroundColor Green
}

# ── 3. SHA256 校验和 ──
# 用 UTF-8 BOM：文件名含中文，UTF-8 在任何区域设置下都能正确显示，
# 且 Win10 1903+ 记事本、VSCode、GitHub 页面与 sha256sum 都能直接读。
$files = Get-ChildItem $rel -File | Sort-Object Name
$sb = New-Object System.Text.StringBuilder
foreach ($f in $files) {
    $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
    [void]$sb.AppendLine("$hash  $($f.Name)")
}
[IO.File]::WriteAllText((Join-Path $rel 'SHA256SUMS.txt'), $sb.ToString(),
    (New-Object System.Text.UTF8Encoding($true)))

Write-Host ''
Write-Host '════════ 发行文件已就绪 ════════' -ForegroundColor Green
Get-ChildItem $rel | ForEach-Object { Write-Host ("  {0,-52} {1,12:N0} B" -f $_.Name, $_.Length) }
Write-Host ''
Write-Host "上传到 GitHub Releases（Tag: v$ver）即可发布。" -ForegroundColor Cyan