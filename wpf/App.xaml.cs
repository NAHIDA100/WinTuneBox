using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WinTune.Wpf
{
    /// <summary>
    /// 应用入口：命令行模式（--selftest / --shot / --memprobe / --startup-bench / --minimized）、
    /// 单实例、首次运行迁移、主题初始化、免责声明、全局异常兜底。
    /// </summary>
    public partial class App : Application
    {
        public static bool SkipDisclaimer =
            Environment.GetEnvironmentVariable("WTB_SKIP_DISCLAIMER") == "1";

        public static MainWindow Win { get; private set; }

        /// <summary>诊断/自动化模式：不弹出模态对话框，异常直接记录并退出</summary>
        public static bool SilentMode;
        public static readonly int StartTick = Environment.TickCount;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            DispatcherUnhandledException += OnDispatcherException;
            TaskScheduler.UnobservedTaskException += OnTaskException;

            try { string _ = AppPaths.LogDir; } catch { }
            Logger.Log("App", "==== WinTuneBox 2.0 启动 pid=" + Process.GetCurrentProcess().Id + " ====");

            Settings.Migrate();

            string[] args = e.Args ?? new string[0];
            bool minimized = Has(args, "--minimized");
            bool selfTest = Has(args, "--selftest") || Has(args, "--selftest-write");
            bool memProbe = Has(args, "--memprobe");
            bool bench = Has(args, "--startup-bench");
            bool shot = Has(args, "--shot");
            SilentMode = shot || memProbe || bench || selfTest;

            if (selfTest)
            {
                string outPath = ValueOf(args, "--selftest") ?? ValueOf(args, "--selftest-write");
                bool write = Has(args, "--selftest-write");
                string file = SelfTest.RunToFile(outPath, write);
                Logger.Log("SelfTest", "报告已写入 " + file);
                Shutdown();
                return;
            }

            if (!AppLifecycle.AcquireSingleInstance())
            {
                MessageBox.Show("WinTuneBox 已经在运行中。", "WinTuneBox", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            try { ThemeManager.Initialize(); } catch (Exception ex) { Logger.Log("Theme", "初始化异常: " + ex.Message); }

            if (!SkipDisclaimer && !minimized && !shot && !AppLifecycle.DisclaimerAccepted())
            {
                string msg = "WinTuneBox 优化工具箱 v2.0\n\n" +
                    "本工具提供系统优化、垃圾清理、启动项与服务管理等功能。\n" +
                    "所有改动都会在操作前自动备份（注册表 / 服务启动类型 / hosts），并提供快照还原中心。\n\n" +
                    "请在理解各项功能的前提下使用；因使用本工具造成的问题，可通过“备份还原中心”或系统还原恢复。\n\n" +
                    "是否继续？";
                MessageBoxResult r = MessageBox.Show(msg, "免责声明", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { Shutdown(); return; }
                AppLifecycle.SetDisclaimerAccepted();
            }

            Win = new MainWindow();
            Notify.Host = Win.ToastArea;
            Notify.TrayFallback = Win.ShowTrayBalloon;

            if (shot)
            {
                string dir = ValueOf(args, "--shot");
                Win.ContentRendered += delegate
                {
                    try
                    {
                        int n = Win.RenderShots(dir);
                        Logger.Log("Shot", "已渲染 " + n + " 张截图到 " + (dir ?? AppPaths.LogDir));
                    }
                    catch (Exception ex) { Logger.Log("Shot", "截图失败: " + ex); }
                    try
                    {
                        if (memProbe || bench) WriteDiagnostics();
                    }
                    catch { }
                    Shutdown();
                };
                Win.Show();
                return;
            }

            if (memProbe || bench)
            {
                Win.ContentRendered += delegate
                {
                    try { WriteDiagnostics(); } catch (Exception ex) { Logger.Log("Diag", ex.Message); }
                    Shutdown();
                };
                Win.Show();
                return;
            }

            if (minimized)
            {
                Win.StartHidden();
                if (bench) { }
            }
            else Win.Show();
        }

        void WriteDiagnostics()
        {
            string outDir = AppPaths.LogDir;
            string mem = Diagnostics.MemProbe();
            string path = Path.Combine(outDir, "selftest-mem.txt");
            File.WriteAllText(path, mem, new System.Text.UTF8Encoding(true));
            Logger.Log("Diag", "内存报告: " + path);

            string bench = Diagnostics.StartupBench();
            string bpath = Path.Combine(outDir, "startup-bench.txt");
            File.WriteAllText(bpath, bench, new System.Text.UTF8Encoding(true));
            Logger.Log("Diag", "启动耗时报告: " + bpath);
        }

        static bool Has(string[] args, string name)
        {
            foreach (string a in args) if (a == name) return true;
            return false;
        }

        /// <summary>取 --name value 形式的值（也支持 --name=value）</summary>
        static string ValueOf(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[i + 1];
                string prefix = name + "=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return args[i].Substring(prefix.Length);
            }
            return null;
        }

        void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try { Logger.Log("Crash(UI)", e.Exception == null ? "?" : e.Exception.ToString()); } catch { }
            if (!SilentMode)
            {
                MessageBox.Show("发生未处理异常：\n" + (e.Exception == null ? "?" : e.Exception.Message),
                    "WinTuneBox", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
            if (SilentMode && Current != null) Shutdown();
        }

        void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                Logger.Log("Crash(Domain)", ex == null ? "?" : ex.ToString());
            }
            catch { }
        }

        void OnTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try { Logger.Log("Crash(Task)", e.Exception == null ? "?" : e.Exception.ToString()); } catch { }
            e.SetObserved();
        }
    }
}