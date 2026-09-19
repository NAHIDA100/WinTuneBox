using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    /// <summary>设置页：外观主题 / 强调色 / Mica 材质 / 常驻行为 / 数据目录 / 维护与关于</summary>
    public partial class SettingsPage : UserControl
    {
        static readonly string[] SwatchHex = { "#2D7CF6", "#7C5CFF", "#0E8EA8", "#1F9D55", "#D98200", "#D6336C" };

        Border[] _swatches;
        bool _built;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        void Build()
        {
            StackPanel content; WrapPanel actions;
            Root.Children.Add(Ui.PageRoot("设置", "外观主题 · 常驻行为 · 数据目录 · 维护", out content, out actions));

            actions.Children.Add(Ui.Btn("导出诊断报告", "BtnDefault", ExportDiag));
            actions.Children.Add(Ui.Btn("运行功能自检", "BtnPrimary", delegate { SelfTest_Click(null, null); }));

            content.Children.Add(AppearanceCard());
            content.Children.Add(BehaviorCard());
            content.Children.Add(DataCard());
            content.Children.Add(MaintenanceCard());
            content.Children.Add(AboutCard());
        }

        // ── 外观 ──
        Border AppearanceCard()
        {
            StackPanel body;
            var card = Ui.Card("外观", "主题与强调色即时生效并自动保存；Mica 材质仅 Win11 22H2 及以上可用", out body);

            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            string[] keys = { "light", "dark", "system" };
            string[] names = { "浅色", "深色", "跟随系统" };
            for (int i = 0; i < keys.Length; i++)
            {
                string mode = keys[i];
                var rb = new RadioButton
                {
                    Content = names[i],
                    GroupName = "ThemeMode",
                    Margin = new Thickness(0, 0, 18, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = ThemeManager.Mode == mode,
                };
                rb.Checked += delegate { if (ThemeManager.Mode != mode) { ThemeManager.SetMode(mode); RefreshSwatches(); } };
                modes.Children.Add(rb);
            }
            body.Children.Add(Ui.Row("主题模式", "浅色 / 深色 / 跟随系统设置", modes));
            body.Children.Add(Ui.Divider());

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _swatches = new Border[ThemeManager.AccentNames.Length];
            for (int i = 0; i < ThemeManager.AccentNames.Length; i++)
            {
                int idx = i;
                Brush fill;
                if (i < SwatchHex.Length)
                {
                    fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(SwatchHex[i]));
                }
                else
                {
                    Color? sys = Dwm.SystemAccentColor();
                    fill = new SolidColorBrush(sys.HasValue ? sys.Value : (Color)ColorConverter.ConvertFromString(SwatchHex[0]));
                }
                var dot = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = fill,
                };
                var sw = new Border
                {
                    Padding = new Thickness(3),
                    CornerRadius = new CornerRadius(13),
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = ThemeManager.AccentNames[i],
                    Child = dot,
                };
                sw.MouseLeftButtonUp += delegate { ThemeManager.SetAccent(idx); RefreshSwatches(); };
                _swatches[i] = sw;
                wrap.Children.Add(sw);
            }
            body.Children.Add(Ui.Row("强调色", "用于按钮、链接、开关与选中态", wrap));
            RefreshSwatches();
            body.Children.Add(Ui.Divider());

            var micaSw = Ui.Switch(Settings.GetBool(Settings.KeyMica, true), delegate(bool on)
            {
                Settings.SetBool(Settings.KeyMica, on);
                if (App.Win != null) App.Win.ApplyAppearance();
                Notify.Success(on ? "已启用 Mica 材质" : "已关闭 Mica 材质（使用纯色背景）");
            });
            micaSw.IsEnabled = FluentWindow.MicaAvailable;
            string micaDesc = FluentWindow.MicaAvailable
                ? "半透明云母背景，随桌面壁纸与系统主题变化"
                : "当前系统不支持 Mica（需要 Windows 11 22H2 及以上，已使用纯色降级）";
            body.Children.Add(Ui.Row("Mica 材质", micaDesc, micaSw));
            return card;
        }

        void RefreshSwatches()
        {
            if (_swatches == null) return;
            for (int i = 0; i < _swatches.Length; i++)
            {
                bool sel = i == ThemeManager.AccentIndex;
                _swatches[i].BorderBrush = sel ? Ui.Brand : Brushes.Transparent;
            }
        }

        // ── 常驻与启动 ──
        Border BehaviorCard()
        {
            StackPanel body;
            var card = Ui.Card("常驻与启动", "托盘、悬浮球与开机自启行为", out body);

            var auto = Ui.Switch(Settings.AutoStartEnabled(), delegate(bool on)
            {
                string err = Settings.SetAutoStart(on);
                if (err != null) { Notify.Warning("设置失败：" + err); return; }
                Notify.Success(on ? "已开启开机自启（最小化到托盘）" : "已关闭开机自启");
            });
            body.Children.Add(Ui.Row("开机自动启动", "登录 Windows 后最小化启动到托盘，不弹出窗口", auto));
            body.Children.Add(Ui.Divider());

            var tray = Ui.Switch(Settings.GetBool(Settings.KeyMinToTray, true), delegate(bool on)
            {
                Settings.SetBool(Settings.KeyMinToTray, on);
            });
            body.Children.Add(Ui.Row("关闭窗口时最小化到托盘", "关闭后保持后台运行，可从托盘右键菜单彻底退出", tray));
            body.Children.Add(Ui.Divider());

            var ball = Ui.Switch(Settings.GetBool(Settings.KeyFloatBall, true), delegate(bool on)
            {
                Settings.SetBool(Settings.KeyFloatBall, on);
                if (App.Win != null) App.Win.SetFloatBall(on);
            });
            body.Children.Add(Ui.Row("显示加速悬浮球", "桌面边缘小球：单击整理内存，双击打开主界面，全屏时自动隐藏", ball));
            return card;
        }

        // ── 数据目录 ──
        Border DataCard()
        {
            StackPanel body;
            var card = Ui.Card("数据目录", "设置、备份、快照与日志统一存放，卸载程序不会自动删除", out body);
            AddDir(body, "程序数据", AppPaths.Root);
            AddDir(body, "操作备份", AppPaths.BackupDir);
            AddDir(body, "系统快照", AppPaths.SnapshotDir);
            AddDir(body, "运行日志", AppPaths.LogDir);

            var purge = Ui.Btn("清理历史日志", "BtnDefault", PurgeLogs);
            body.Children.Add(Ui.Row("日志清理", "删除 30 天前的历史日志文件，不影响设置、备份与快照", purge));
            return card;
        }

        void AddDir(StackPanel body, string label, string path)
        {
            string p = path;
            var btn = Ui.Btn("打开", "BtnDefault", delegate { AppPaths.OpenInExplorer(p); });
            body.Children.Add(Ui.Row(label, p, btn));
            body.Children.Add(Ui.Divider());
        }

        void PurgeLogs()
        {
            int n = 0;
            try
            {
                DateTime limit = DateTime.Now.AddDays(-30);
                foreach (string f in Directory.GetFiles(AppPaths.LogDir, "*.txt"))
                {
                    try { if (File.GetLastWriteTime(f) < limit) { File.Delete(f); n++; } } catch { }
                }
                foreach (string f in Directory.GetFiles(AppPaths.LogDir, "*.log"))
                {
                    try { if (File.GetLastWriteTime(f) < limit) { File.Delete(f); n++; } } catch { }
                }
            }
            catch (Exception ex) { Notify.Error("清理失败：" + ex.Message); return; }
            Notify.Success("已清理 " + n + " 个历史日志文件");
        }

        // ── 维护 ──
        Border MaintenanceCard()
        {
            StackPanel body;
            var card = Ui.Card("维护", "自检覆盖全部只读检测项，可在不修改系统的前提下验证功能可用性", out body);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Btn("只读自检", "BtnPrimary", delegate { SelfTest_Click(null, null); }));
            var write = Ui.Btn("写入验证（含还原）", "BtnDefault", delegate { SelfTestWrite_Click(null, null); });
            write.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(write);
            var latest = Ui.Btn("查看最近报告", "BtnDefault", delegate { LatestSelfTest_Click(null, null); });
            latest.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(latest);
            body.Children.Add(Ui.Row("功能自检", "只读检测不会修改系统；写入验证会在 HKCU 下写入后立即还原", row));

            body.Children.Add(Ui.Divider());
            var reset = Ui.Btn("恢复默认设置", "BtnDanger", ResetSettings);
            body.Children.Add(Ui.Row("重置偏好", "恢复主题、托盘与悬浮球的默认值（不影响已创建的备份与快照）", reset));
            return card;
        }

        void ResetSettings()
        {
            if (MessageBox.Show("将恢复主题、托盘与悬浮球等偏好为默认值，是否继续？", "恢复默认设置",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Settings.Set(Settings.KeyTheme, "system");
            Settings.SetInt(Settings.KeyAccent, 0);
            Settings.SetBool(Settings.KeyMica, true);
            Settings.SetBool(Settings.KeyMinToTray, true);
            Settings.SetBool(Settings.KeyFloatBall, true);
            ThemeManager.SetMode("system");
            ThemeManager.SetAccent(0);
            if (App.Win != null) { App.Win.ApplyAppearance(); App.Win.SetFloatBall(true); }
            Notify.Success("已恢复默认设置");
        }

        // ── 关于 ──
        Border AboutCard()
        {
            StackPanel body;
            var card = Ui.Card("关于", "WinTuneBox 优化工具箱 v" + SelfTest.Version + " · 单文件绿色版，无 .NET SDK 依赖", out body);

            body.Children.Add(Ui.InfoRow("版本", SelfTest.Version));
            body.Children.Add(Ui.InfoRow("系统", OS.WinVerFull + "（Build " + OS.Build + "）"));
            body.Children.Add(Ui.InfoRow("架构", (OS.IsX64 ? "x64" : "x86") + " 进程 · " + (Environment.Is64BitOperatingSystem ? "64 位系统" : "32 位系统")));
            body.Children.Add(Ui.InfoRow("处理器", OS.CpuName ?? "—"));
            body.Children.Add(Ui.InfoRow("显卡", OS.GpuAll ?? "—"));
            body.Children.Add(Ui.InfoRow("权限", OS.IsElevated ? "管理员模式" : "标准用户（部分功能受限）",
                OS.IsElevated ? Ui.Ok : Ui.Warn));
            body.Children.Add(Ui.Divider());

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Btn("免责声明", "BtnDefault", delegate { Disclaimer_Click(null, null); }));
            var log = Ui.Btn("打开日志目录", "BtnDefault", delegate { AppPaths.OpenInExplorer(AppPaths.LogDir); });
            log.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(log);
            if (!OS.IsElevated)
            {
                var el = Ui.Btn("以管理员重启", "BtnPrimary", delegate
                {
                    if (MessageBox.Show("将以管理员身份重新启动本工具，是否继续？", "提权",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        AppLifecycle.RestartElevated();
                });
                el.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(el);
            }
            body.Children.Add(Ui.Row("许可与支持", "绿色版可整目录拷贝；安装包提供卸载入口", row));
            return card;
        }

        // ── 动作 ──
        void ExportDiag()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("===== WinTuneBox 诊断报告 =====");
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("版本: " + SelfTest.Version);
                sb.AppendLine("系统: " + OS.WinVerFull + " (Build " + OS.Build + ")");
                sb.AppendLine("架构: " + (OS.IsX64 ? "x64" : "x86") + " 进程");
                sb.AppendLine("CPU: " + OS.CpuName);
                sb.AppendLine("管理员: " + (OS.IsElevated ? "是" : "否"));
                sb.AppendLine("显卡: " + OS.GpuAll);
                sb.AppendLine("主题: " + ThemeManager.Mode + " (dark=" + ThemeManager.IsDark + ", accent=" + ThemeManager.AccentIndex + ")");
                sb.AppendLine("Mica: 可用=" + FluentWindow.MicaAvailable + " 启用=" + Settings.GetBool(Settings.KeyMica, true));
                sb.AppendLine("数据目录: " + AppPaths.Root);
                sb.AppendLine("");
                sb.AppendLine("----- settings.json -----");
                try { sb.AppendLine(File.ReadAllText(Settings.FilePath)); } catch { sb.AppendLine("(无)"); }

                string path = Path.Combine(AppPaths.LogDir, "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Notify.Success("诊断报告已导出");
                try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
            }
            catch (Exception ex) { Notify.Error("导出失败：" + ex.Message); }
        }

        void SelfTest_Click(object sender, RoutedEventArgs e) { RunSelfTest(false); }
        void SelfTestWrite_Click(object sender, RoutedEventArgs e) { RunSelfTest(true); }

        void RunSelfTest(bool write)
        {
            Notify.Info(write ? "正在运行写入验证（写-读-还原，约数秒）…" : "正在运行只读自检（约数秒）…");
            Ui.RunAsync(delegate
            {
                string file;
                try { file = SelfTest.RunToFile(null, write); }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(delegate { Notify.Error("自检失败：" + ex.Message); });
                    return;
                }
                Dispatcher.Invoke(delegate
                {
                    Notify.Success("自检完成：" + Path.GetFileName(file));
                    if (MessageBox.Show("自检报告已保存到：\n" + file + "\n\n是否立即打开？", "自检完成",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        try { Process.Start("notepad.exe", "\"" + file + "\""); } catch { }
                    }
                });
            });
        }

        void LatestSelfTest_Click(object sender, RoutedEventArgs e)
        {
            string f = Path.Combine(AppPaths.LogDir, "selftest-latest.txt");
            if (!File.Exists(f)) { Notify.Info("还没有自检报告，请先运行功能自检"); return; }
            try { Process.Start("notepad.exe", "\"" + f + "\""); } catch (Exception ex) { Notify.Warning(ex.Message); }
        }

        void Disclaimer_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "WinTuneBox 优化工具箱 v" + SelfTest.Version + "\n\n" +
                "本工具提供系统优化、垃圾清理、服务与启动项管理等功能。\n" +
                "部分操作会修改系统设置，工具会在操作前自动备份注册表/文件，并提供快照还原中心。\n\n" +
                "请在充分理解各项功能的前提下使用；因使用本工具造成的问题，请使用“备份还原中心”或系统还原恢复。",
                "免责声明", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}