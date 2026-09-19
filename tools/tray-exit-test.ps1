<#
  托盘「退出」端到端测试

  1) 按 PID 找到托盘消息窗口（HwndSource，标题 WinTuneBoxTraySink）
  2) 投递 WM_TRAY + WM_RBUTTONUP 弹出右键菜单
  3) 用 UI Automation 找到「退出」项，真鼠标点击
  4) 断言进程退出

  用法: powershell -ExecutionPolicy Bypass -File tools\tray-exit-test.ps1
#>
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class TrayTest {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);

  public static IntPtr FindByTitle(uint want, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
      if (t.ToString() == title) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

$proc = Get-Process WinTuneBox -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host '应用未运行，先启动它' -ForegroundColor Red; exit 3 }
Write-Host ("目标进程 PID = {0}" -f $proc.Id)

$sink = [TrayTest]::FindByTitle([uint32]$proc.Id, 'WinTuneBoxTraySink')
Write-Host ("托盘消息窗口 HWND = {0}" -f $sink)
if ($sink -eq [IntPtr]::Zero) { Write-Host '找不到托盘消息窗口' -ForegroundColor Red; exit 3 }

# WM_TRAY = 0x400 + 1024, WM_RBUTTONUP = 0x205
[void][TrayTest]::PostMessage($sink, 0x0400 + 1024, [IntPtr]::Zero, [IntPtr]0x0205)
Start-Sleep -Milliseconds 1000

$cond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '退出'))
)
$el = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $el) { Write-Host '托盘菜单里没找到「退出」项' -ForegroundColor Red; exit 4 }
$r = $el.Current.BoundingRectangle
Write-Host ("找到「退出」项 {0}x{1} @ ({2},{3})" -f [int]$r.Width, [int]$r.Height, [int]$r.Left, [int]$r.Top)

$x = [int]($r.Left + $r.Width / 2); $y = [int]($r.Top + $r.Height / 2)
[void][TrayTest]::SetCursorPos($x, $y)
Start-Sleep -Milliseconds 250
[TrayTest]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[TrayTest]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
Write-Host "已在 ($x,$y) 点击「退出」"

for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
        Write-Host ("`n[PASS] 进程 {0} 已退出（耗时 {1:N1}s）" -f $proc.Id, (($i + 1) * 0.5)) -ForegroundColor Green
        exit 0
    }
}
Write-Host ("`n[FAIL] 点击「退出」后进程 {0} 仍在运行" -f $proc.Id) -ForegroundColor Red
exit 1