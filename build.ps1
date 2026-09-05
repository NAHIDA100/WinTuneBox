# ═══ WinTuneBox / Windows 优化工具箱 构建脚本 ═══
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
# 产出: dist\WinOptimizer.exe（可选: dist\Setup-Windows优化工具箱-<版本>.exe）
$ErrorActionPreference = "Stop"

$csc    = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root   = $PSScriptRoot
$src    = Join-Path $root "src"
$dist   = Join-Path $root "dist"
$icon   = Join-Path $root "assets\app.ico"
$ver    = "1.0.0"

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# ── 1. 图标 ──
if (-not (Test-Path $icon)) {
    Write-Host "生成 app.ico ..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "assets\make-icon.ps1")
}

# ── 2. 编译 ──
$out = Join-Path $dist "WinOptimizer.exe"
$argsList = New-Object System.Collections.Generic.List[string]
$fixedArgs = [string[]]@(
    "/target:winexe", "/out:$out", "/codepage:65001", "/nologo", "/optimize+",
    "/r:System.ServiceProcess.dll",
    "/win32icon:$icon", "/win32manifest:$($src)\app.manifest"
)
$argsList.AddRange($fixedArgs)
Get-ChildItem -Path $src -Filter "*.cs" | ForEach-Object { $argsList.Add($_.FullName) }
Write-Host "编译 $out ..." -ForegroundColor Cyan
& $csc $argsList
if ($LASTEXITCODE -ne 0) { throw "编译失败 (csc exit=$LASTEXITCODE)" }
Write-Host "编译成功: $out" -ForegroundColor Green

# ── 3. 自检（可选 --skip-selftest 跳过） ──
if ($args -notcontains "--skip-selftest") {
    Write-Host "运行自检 ..." -ForegroundColor Cyan
    & $out --selftest
    Start-Sleep -Milliseconds 300
    if (Test-Path (Join-Path $dist "selftest.txt")) {
        Write-Host "自检报告: dist\selftest.txt" -ForegroundColor Green
    }
}

# ── 4. 安装包 ──
if ($args -notcontains "--no-installer") {
    $iss = Join-Path $root "installer.iss"
    if (Test-Path $iss) {
        $iscc = "D:\App\InnoSetup6\ISCC.exe"
        if (Test-Path $iscc) {
            Write-Host "打安装包 ..." -ForegroundColor Cyan
            & $iscc $iss | Out-Null
            $setup = Get-ChildItem $dist -Filter "Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($setup) { Write-Host "安装包: $($setup.FullName)" -ForegroundColor Green }
        } else {
            Write-Host "跳过安装包（找不到 ISCC.exe）" -ForegroundColor Yellow
        }
    }
}
Write-Host "构建完成。" -ForegroundColor Green
