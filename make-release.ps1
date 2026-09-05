# ═══ GitHub 发行打包脚本 ═══
# 1) 编译绿色版 + Inno 安装包（build.ps1）
# 2) 整理到 release/：
#    - WinTuneBox-<版本>-绿色版.zip（exe + README + LICENSE + 免责条款）
#    - Setup-Windows优化工具箱-<版本>.exe（安装包副本）
#    - SHA256SUMS.txt（校验和）
# 3) 手动把上述文件上传到 GitHub Releases 即可
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$rel  = Join-Path $root "release"
$dist = Join-Path $root "dist"

# 版本号从 installer.iss 读取
$iss = Get-Content (Join-Path $root "installer.iss") -Raw -Encoding UTF8
$ver = [regex]::Match($iss, '#define AppVer "([\d.]+)"').Groups[1].Value
if (-not $ver) { throw "无法从 installer.iss 解析版本号" }
Write-Host "发行版本: $ver" -ForegroundColor Cyan

# ── 1. 构建 ──
Write-Host "==> 构建绿色版与安装包 ..." -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1") --skip-selftest

# ── 2. 整理发行目录 ──
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Path $rel | Out-Null

$exe = Join-Path $dist "WinOptimizer.exe"
$setup = Get-ChildItem $dist -Filter "Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not (Test-Path $exe) -or -not $setup) { throw "构建产物缺失" }

# 绿色版 zip：exe + 文档
$zipTmp = Join-Path $rel "zip-tmp"
New-Item -ItemType Directory -Path $zipTmp | Out-Null
Copy-Item $exe (Join-Path $zipTmp "WinOptimizer.exe")
Copy-Item (Join-Path $root "README.md")    (Join-Path $zipTmp "README.md")
Copy-Item (Join-Path $root "LICENSE")      (Join-Path $zipTmp "LICENSE")
Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $zipTmp "CHANGELOG.md")
Copy-Item (Join-Path $root "disclaimer.txt") (Join-Path $zipTmp "免责声明.txt")
$zipName = "WinTuneBox-v$ver-绿色版.zip"
Compress-Archive -Path (Join-Path $zipTmp "*") -DestinationPath (Join-Path $rel $zipName) -CompressionLevel Optimal
Remove-Item $zipTmp -Recurse -Force

# 安装包副本（规范命名）
$setupName = "WinTuneBox-v$ver-安装版.exe"
Copy-Item $setup.FullName (Join-Path $rel $setupName)

# SHA256 校验和（GBK 编码，兼容 Windows 记事本/老工具打开中文文件名）
$files = Get-ChildItem $rel -File
$sb = New-Object System.Text.StringBuilder
foreach ($f in $files) {
    $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
    [void]$sb.AppendLine("$hash  $($f.Name)")
}
[IO.File]::WriteAllText((Join-Path $rel "SHA256SUMS.txt"), $sb.ToString(),
    [System.Text.Encoding]::GetEncoding(936))

Write-Host ""
Write-Host "════════ 发行文件已就绪 ════════" -ForegroundColor Green
Get-ChildItem $rel | ForEach-Object { Write-Host ("  {0,-46} {1,10:N0} B" -f $_.Name, $_.Length) }
Write-Host ""
Write-Host "上传到 GitHub Releases（Tag: v$ver）即可发布。" -ForegroundColor Cyan
