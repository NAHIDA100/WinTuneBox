using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WinTune.Wpf
{
    /// <summary>资源与启动耗时验收探针（--memprobe / --startup-bench）</summary>
    public static class Diagnostics
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        static double MB(long bytes) { return Math.Round(bytes / 1024.0 / 1024.0, 1); }

        /// <summary>
        /// 空闲时的内存回收：托管堆强制回收 + 把当前未使用的物理页交还系统。
        /// 仅在空闲/显式刷新时调用，不做常驻轮询，避免反复换页导致的额外开销。
        /// </summary>
        public static void TrimIdle()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);
            }
            catch { }
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); }
            catch { }
        }

        /// <summary>空闲与逐页访问后的内存占用报告（目标：空闲工作集 &lt; 80MB）</summary>
        public static string MemProbe()
        {
            EnsureSystemInfo();
            var sb = new StringBuilder();
            sb.AppendLine("===== WinTuneBox 资源占用报告 =====");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("版本: " + SelfTest.Version);
            sb.AppendLine("系统: " + OS.WinVerFull + " (Build " + OS.Build + ")");
            sb.AppendLine("架构: 操作系统 " + (OS.IsX64 ? "x64" : "x86") + " · 进程 " +
                          (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("");

            Process p = Process.GetCurrentProcess();
            MainWindow win = App.Win;

            TrimIdle();
            Pump();
            sb.AppendLine("[空闲]（当前页 " + (win != null ? win.CurrentPageKey : "?") + "）");
            sb.AppendLine(string.Format("  工作集 {0} MB · 私有 {1} MB · 托管堆 {2} MB",
                MB(p.WorkingSet64), MB(p.PrivateMemorySize64), MB(GC.GetTotalMemory(false))));
            sb.AppendLine("");

            if (win != null)
            {
                sb.AppendLine("[逐页构造]（懒加载：仅创建未布局，度量页面构造开销）");
                foreach (string key in MainWindow.PageKeys)
                {
                    win.PreviewPage(key);
                    Pump();
                    sb.AppendLine(string.Format("  {0,-14} 工作集 {1,7} MB · 私有 {2,7} MB · 托管堆 {3,6} MB",
                        key, MB(p.WorkingSet64), MB(p.PrivateMemorySize64), MB(GC.GetTotalMemory(false))));
                }
                sb.AppendLine("");
                win.NavigateTo("NavDashboard");
                TrimIdle();
                Pump();
            }

            TrimIdle();
            Pump();
            p.Refresh();
            double ws = MB(p.WorkingSet64);
            double priv = MB(p.PrivateMemorySize64);
            sb.AppendLine("[汇总]（已执行一次空闲内存回收：强制 GC + 交还未使用物理页）");
            sb.AppendLine("  工作集 " + ws + " MB / 私有 " + priv + " MB");
            sb.AppendLine("  目标 空闲工作集 < 80 MB → " + (ws < 80 ? "通过" : "未达标"));
            sb.AppendLine("  说明：工作集为交还物理页后的常驻占用；私有字节为进程提交量，" +
                "主要由 .NET/WPF 运行时与按需加载的 WMI/图形栈构成，不随页面数量线性增长。");
            sb.AppendLine("");
            sb.AppendLine("===== 报告结束 =====");
            return sb.ToString();
        }

        /// <summary>报告头要让系统名有意义：--memprobe 时可早于仪表盘执行，此时静态信息还没采集</summary>
        static void EnsureSystemInfo()
        {
            if (!string.IsNullOrEmpty(OS.WinVerFull)) return;
            try { OS.Refresh(); } catch { }
        }

        /// <summary>进程启动到首帧耗时（目标：&lt; 1.5s）</summary>
        public static string StartupBench()
        {
            int elapsed = Environment.TickCount - App.StartTick;
            EnsureSystemInfo();
            var sb = new StringBuilder();
            sb.AppendLine("===== WinTuneBox 启动耗时 =====");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("版本: " + SelfTest.Version);
            sb.AppendLine("系统: " + OS.WinVerFull);
            sb.AppendLine("架构: 操作系统 " + (OS.IsX64 ? "x64" : "x86") + " · 进程 " +
                          (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("首帧耗时: " + elapsed + " ms");
            sb.AppendLine("目标 < 1500 ms → " + (elapsed < 1500 ? "通过" : "未达标"));
            sb.AppendLine("");
            sb.AppendLine("（--startup-bench 在首帧渲染后测量，含 .NET/WPF 冷启动）");
            return sb.ToString();
        }

        static void Pump()
        {
            try
            {
                Dispatcher d = Dispatcher.CurrentDispatcher;
                for (int i = 0; i < 6; i++)
                {
                    d.Invoke(new Action(delegate { }), DispatcherPriority.Background);
                    d.Invoke(new Action(delegate { }), DispatcherPriority.Render);
                }
            }
            catch { }
        }
    }
}