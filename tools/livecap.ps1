<#
  ═══ WinTuneBox 进程外窗口抓取（DWM / Mica / 标题栏回归验证）═══

  为什么还需要它：`--shot` 走 RenderTargetBitmap 离屏渲染，并且会自己铺一层
  WindowBackgroundBrush 当底，所以**看不到** DWM 背板、Mica、玻璃帧、标题栏
  条带这类「合成层」问题 —— v2.0.0 浅色模式下「标题栏整条纯黑、下方正常」的
  bug 就是 `--shot` 全部通过、却肉眼可见的那一类。

  本脚本改为把窗口置顶后用 CopyFromScreen 抓取真实合成结果，并在关键位置
  （标题栏 / 导航 / 内容区）采样像素，便于比对浅色、深色、最大化与 Mica 开关。

  用法:
    powershell -ExecutionPolicy Bypass -File tools\livecap.ps1
    powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -Out D:\tmp\light.png
    powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -Max 1
    powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -ToggleMica 1
    powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -Attach    # 抓正在运行的实例（不重启）

  注意：`-Attach` 用于「用户报障时正开着窗口」的场景 —— 直接抓当前进程，
  不启动新进程也不杀进程，抓完保持原样；不带该开关时才会重启一个干净实例。

  判定参考（浅色 + Mica）: 标题栏 ≈ #F3F3F3~#EFF4F9（随壁纸偏色）
                          导航 ≈ #F6F7FB   内容 ≈ #FEFEFE
              浅色 + 纯色: 标题栏 = #F3F4F8
              **出现 #000000 即为合成失败（半透明笔刷叠在不透明黑上）**
#>
param(
    [string]$Exe = "",
    [string]$Out = "",
    [int]$Max = 0,
    [int]$ToggleMica = 0,
    [int]$WaitMs = 2400,
    [switch]$Attach
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Exe)) { $Exe = Join-Path $root 'wpf\bin\Release\WinTuneBox.exe' }
if ([string]::IsNullOrEmpty($Out)) { $Out = Join-Path $env:TEMP 'wintunebox-livecap.png' }
if (-not (Test-Path $Exe)) { throw "找不到 $Exe（先跑 build.ps1）" }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinCap {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[WinCap]::SetProcessDPIAware() | Out-Null   # 否则 CopyFromScreen 会被 DPI 虚拟化，采到错误坐标

# -Attach: 复用正在运行的实例（不启动、不杀进程），用于复现用户当前看到的画面
$p = $null
if ($Attach) {
    $p = Get-Process WinTuneBox, WinOptimizer -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p -eq $null) { throw '没有正在运行的实例（去掉 -Attach 让它自己启动一个）' }
    Write-Host ("附加到正在运行的实例: " + $p.Path) -ForegroundColor Yellow
} else {
    Get-Process WinTuneBox, WinOptimizer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    $p = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Milliseconds $WaitMs
}
$p.Refresh(); $h = $p.MainWindowHandle
for ($i = 0; $i -lt 25 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 400; $p.Refresh(); $h = $p.MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { throw '未找到主窗口' }

[WinCap]::ShowWindow($h, 9) | Out-Null                     # SW_RESTORE
if ($Max -eq 1) { [WinCap]::ShowWindow($h, 3) | Out-Null } # SW_MAXIMIZE
# 置顶比 SetForegroundWindow 可靠：不必具备前台权限也能抓到真实合成结果
[WinCap]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
[WinCap]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1600

$r = New-Object WinCap+RECT
[WinCap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
$scale = $w / 1180.0
$cap = [int](44 * $scale)
Write-Host ("窗口 {0}x{1} @ ({2},{3})  缩放 {4:N2}x  标题栏 {5}px" -f $w, $ht, $r.Left, $r.Top, $scale, $cap)

function Grab {
    $b = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $g.Dispose()
    return $b
}
function Sample($b) {
    $pts = @(
        @('标题栏-左', [int]($w * 0.2), [int]($cap * 0.5)),
        @('标题栏-中', [int]($w * 0.5), [int]($cap * 0.5)),
        @('标题栏-右', [int]($w * 0.9), [int]($cap * 0.5)),
        @('导航栏',    [int]($w * 0.08), [int]($ht * 0.12)),
        @('内容区',    [int]($w * 0.62), [int]($ht * 0.6))
    )
    $bad = 0
    foreach ($pt in $pts) {
        $x = [math]::Min([math]::Max($pt[1], 0), $w - 1)
        $y = [math]::Min([math]::Max($pt[2], 0), $ht - 1)
        $c = $b.GetPixel($x, $y)
        if ($c.R -eq 0 -and $c.G -eq 0 -and $c.B -eq 0) { $bad++ }
        Write-Host ("  {0,-8} #{1:X2}{2:X2}{3:X2}" -f $pt[0], $c.R, $c.G, $c.B)
    }
    if ($bad -gt 0) { Write-Host "  ⚠ 有 $bad 处采样为纯黑，疑似合成/背板失败" -ForegroundColor Red }
    return $bad
}

$bmp = Grab
Write-Host '── 当前状态 ──'
$bad = Sample $bmp
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "已保存: $Out"

if ($ToggleMica -eq 1) {
    # 在标题栏下方扫描蓝色开关（仅命中「Mica 材质」那一行）
    $bmp = Grab
    $ys = @(); $xs = @()
    $y0 = [int]($ht * 0.35); $y1 = [int]($ht * 0.58)
    $x0 = [int]($w * 0.80); $x1 = [int]($w * 0.98)
    for ($y = $y0; $y -lt $y1; $y += 2) {
        for ($x = $x0; $x -lt $x1; $x += 2) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.B -gt 180 -and $c.R -lt 120 -and $c.G -gt 90 -and $c.G -lt 190) { $ys += $y; $xs += $x }
        }
    }
    $bmp.Dispose()
    if ($ys.Count -eq 0) { Write-Host '⚠ 未定位到 Mica 开关，跳过切换' -ForegroundColor Yellow; exit 3 }
    $ys = $ys | Sort-Object; $xs = $xs | Sort-Object
    $cx = $xs[[int]($xs.Count / 2)]; $cy = $ys[[int]($ys.Count / 2)]
    Write-Host "点击 Mica 开关 @ 窗口坐标 ($cx,$cy)"
    [WinCap]::SetCursorPos($r.Left + $cx, $r.Top + $cy) | Out-Null
    Start-Sleep -Milliseconds 300
    [WinCap]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [WinCap]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
    $bmp = Grab
    Write-Host '── 切换后 ──'
    $bad += Sample $bmp
    $bmp.Save(($Out -replace '\.png$', '-toggled.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

[WinCap]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0043) | Out-Null
if (-not $Attach) { Get-Process WinTuneBox -ErrorAction SilentlyContinue | Stop-Process -Force }
if ($bad -gt 0) { exit 1 }
Write-Host '✅ 未发现黑色合成失败' -ForegroundColor Green