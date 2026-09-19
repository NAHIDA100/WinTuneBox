using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinTune.Wpf
{
    /// <summary>
    /// 主窗口：Fluent 标题栏 + 分组导航（数据驱动）+ 命令面板（Ctrl+K）+ 托盘 + 页面懒加载与切换动效。
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public static readonly string[] PageKeys = {
            "NavDashboard", "NavTweak", "NavClean", "NavStartup", "NavService",
            "NavNetwork", "NavSoftware", "NavStorage", "NavHealth", "NavTools",
            "NavSnapshot", "NavSettings"
        };

        static readonly string[] PageTitles = {
            "概览", "一键优化", "垃圾清理", "启动项管理", "服务优化",
            "网络工具", "软件管理", "存储分析", "安全体检", "更多工具",
            "备份还原中心", "设置"
        };

        readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        readonly List<NavGroup> _groups = new List<NavGroup>();
        readonly List<NavItem> _flat = new List<NavItem>();

        ShellNotifyIcon _tray;
        FloatBall _ball;
        bool _reallyExit;
        bool _shuttingDown;
        bool _startHidden;
        bool _compact;
        bool _navReady;
        string _currentKey = "NavDashboard";

        public MainWindow()
        {
            InitializeComponent();
            BuildNav();
            Loaded += OnLoaded;
            Closing += OnClosing;
            SizeChanged += OnSizeChanged;
            StateChanged += OnWindowStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public string CurrentPageKey { get { return _currentKey; } }

        // ── 导航构建 ──
        void BuildNav()
        {
            _groups.Add(new NavGroup("优化", new List<NavItem> {
                new NavItem("NavDashboard", "概览", "\uE80F", "优化"),
                new NavItem("NavTweak", "一键优化", "\uE945", "优化"),
                new NavItem("NavClean", "垃圾清理", "\uE74D", "优化"),
            }));
            _groups.Add(new NavGroup("管理", new List<NavItem> {
                new NavItem("NavStartup", "启动项管理", "\uE7C1", "管理"),
                new NavItem("NavService", "服务优化", "\uE9D9", "管理"),
                new NavItem("NavSoftware", "软件管理", "\uE71D", "管理"),
                new NavItem("NavNetwork", "网络工具", "\uE774", "管理"),
            }));
            _groups.Add(new NavGroup("系统", new List<NavItem> {
                new NavItem("NavStorage", "存储分析", "\uEDA3", "系统"),
                new NavItem("NavHealth", "安全体检", "\uE83D", "系统"),
                new NavItem("NavTools", "更多工具", "\uE90F", "系统"),
                new NavItem("NavSnapshot", "备份还原中心", "\uE72C", "系统"),
                new NavItem("NavSettings", "设置", "\uE713", "系统"),
            }));
            foreach (NavGroup g in _groups) foreach (NavItem it in g.Items) _flat.Add(it);
            NavList.ItemsSource = _groups;
        }

        bool _idleTrimmed;

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitTray();
            RefreshAdminBar();
            RefreshThemeButton();
            NavigateTo(Settings.Get(Settings.KeyLastPage, "NavDashboard"));
            _navReady = true;
            if (Settings.GetBool(Settings.KeyFloatBall, true)) ShowFloatBall();
            // 隐藏启动时窗口尚未布局，ActualWidth 还是 0，按宽度判断会把导航误判成窄窗口模式；
            // 真正的宽度要等 ShowMain() 显示出来（或尺寸变化）时才知道。
            if (_startHidden) ShowInTaskbar = false;
            else ApplyCompact(ActualWidth < 900);

            // 首帧渲染完成后做一次空闲内存回收（把未使用的物理页交还系统），
            // 之后不再周期性调用，避免反复换页带来额外开销。
            DispatcherTimer once = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(4),
            };
            once.Tick += delegate
            {
                once.Stop();
                if (_idleTrimmed) return;
                _idleTrimmed = true;
                Diagnostics.TrimIdle();
            };
            once.Start();
        }

        /// <summary>
        /// 静默启动（开机自启 --minimized）：把主界面藏到托盘，只留托盘图标与悬浮球。
        /// </summary>
        public void StartHidden()
        {
            _startHidden = true;
            ShowInTaskbar = false;
            ShowActivated = false;   // 登录时不抢焦点
            Opacity = 0;             // Show() 到 Hide() 之间不可见，避免启动瞬间闪一下
            Show();

            // Hide() 必须放在 Show() 返回之后。
            // Loaded 是在 Show() 内部的 CreateSourceWindow 里同步触发的，此时 Show() 还没收尾；
            // 在 Loaded 里调 Hide() 会先清掉 WS_VISIBLE，紧接着被收尾的 ShowWindow(SW_SHOW) 重新盖上，
            // 结果是「WPF 认为已隐藏（IsVisible=false）、Win32 侧窗口仍然可见（WS_VISIBLE）」。
            // 开机自启时表现为：静默启动失效，弹出一个只有 Mica 底色、导航与文字完全不渲染的空窗口。
            Hide();

            // 兜底：确认原生窗口真的不可见，避免上面这种状态不一致残留一个空窗。
            ForceHideNative();
        }

        /// <summary>WPF 状态与 Win32 不一致时，直接对原生窗口补一次隐藏</summary>
        void ForceHideNative()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (NativeWindow.ForceHide(hwnd))
                    Logger.Log("Window", "WPF 已隐藏但原生窗口仍可见，已强制隐藏");
            }
            catch (Exception ex) { Logger.Log("Window", "强制隐藏失败: " + ex.Message); }
        }

        // ── 页面切换 ──
        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (Host == null) return;
            RadioButton rb = e.OriginalSource as RadioButton;
            if (rb == null) return;
            NavItem item = rb.DataContext as NavItem;
            if (item == null) return;
            _navReady = true;
            ShowPage(item.Key);
        }

        void ShowPage(string key)
        {
            UserControl page;
            if (!_pages.TryGetValue(key, out page) || page == null)
            {
                page = CreatePage(key);
                _pages[key] = page;
            }
            Host.Content = page;
            _currentKey = key;
            TitlePage.Text = "/  " + PageTitleOf(key);
            Settings.Set(Settings.KeyLastPage, key);
            AnimatePageIn(page);
        }

        public void NavigateTo(string key)
        {
            foreach (NavItem it in _flat)
            {
                if (it.Key == key) { it.IsSelected = true; return; }
            }
            ShowPage(key);
        }

        /// <summary>仅创建页面（内存探针用，不改变当前页）</summary>
        public void PreviewPage(string key)
        {
            if (_pages.ContainsKey(key)) return;
            _pages[key] = CreatePage(key);
        }

        static string PageTitleOf(string key)
        {
            int i = Array.IndexOf(PageKeys, key);
            return i >= 0 ? PageTitles[i] : "概览";
        }

        UserControl CreatePage(string key)
        {
            switch (key)
            {
                case "NavDashboard": return new Views.DashboardPage();
                case "NavTweak": return new Views.TweakPage();
                case "NavClean": return new Views.CleanPage();
                case "NavStartup": return new Views.StartupPage();
                case "NavService": return new Views.ServicePage();
                case "NavNetwork": return new Views.NetworkPage();
                case "NavSoftware": return new Views.SoftwarePage();
                case "NavStorage": return new Views.StoragePage();
                case "NavHealth": return new Views.SecurityPage();
                case "NavTools": return new Views.ToolsPage();
                case "NavSnapshot": return new Views.SnapshotPage();
                case "NavSettings": return new Views.SettingsPage();
                default: return new Views.DashboardPage();
            }
        }

        void AnimatePageIn(UserControl page)
        {
            if (page == null) return;
            var tt = new TranslateTransform(14, 0);
            page.RenderTransform = tt;
            page.Opacity = 0;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            page.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        }

        // ── 外观 ──
        protected override void OnAppearanceChanged()
        {
            try
            {
                if (MaxGlyph != null) MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
                if (ThemeGlyph != null) ThemeGlyph.Text = ThemeManager.IsDark ? "\uE708" : "\uE706";
                if (RootGrid != null && RootGrid.Background == null) { }
            }
            catch { }
        }

        void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();
            RefreshThemeButton();
        }

        void RefreshThemeButton()
        {
            ThemeGlyph.Text = ThemeManager.IsDark ? "\uE708" : "\uE706";
            ThemeBtn.ToolTip = ThemeManager.IsDark ? "切换到浅色模式" : "切换到深色模式";
        }

        void Min_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }

        void Max_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            OnAppearanceChanged();
        }

        void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized) Diagnostics.TrimIdle();
            if (WindowState == WindowState.Minimized && Settings.GetBool(Settings.KeyMinToTray, true))
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyCompact(e.NewSize.Width < 900);
        }

        void ApplyCompact(bool compact)
        {
            if (_compact == compact && _navReady) return;
            _compact = compact;
            NavColumn.Width = new GridLength(compact ? 64 : 236);
            PaletteHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            // 收起文字时搜索入口与权限提示只留图标，否则会被挤成细长条
            PaletteBtn.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            PaletteBtn.Margin = compact ? new Thickness(8, 10, 8, 6) : new Thickness(12, 10, 12, 6);
            AdminText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            AdminBar.Padding = compact ? new Thickness(0, 8, 0, 8) : new Thickness(10, 8, 10, 8);
            // 收起文字后让盾牌图标横跨两列居中，否则会贴在左边缘
            AdminIcon.Width = compact ? double.NaN : 20;
            AdminIcon.SetValue(Grid.ColumnSpanProperty, compact ? 2 : 1);
            AdminIcon.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            MemLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            MemBtn.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            MemBtn.Padding = compact ? new Thickness(0) : new Thickness(10, 0, 10, 0);
            foreach (NavItem it in _flat) it.IsCompact = compact;
            foreach (NavGroup g in _groups) g.IsCompact = compact;
        }

        // ── 管理员状态 ──
        void RefreshAdminBar()
        {
            if (OS.IsElevated)
            {
                AdminBar.Background = (Brush)FindResource("OkLightBrush");
                AdminIcon.Text = "\uE7EF";
                AdminIcon.Foreground = (Brush)FindResource("OkBrush");
                AdminText.Text = "管理员模式，全部功能可用";
                AdminText.Foreground = (Brush)FindResource("OkBrush");
                AdminBar.Cursor = Cursors.Arrow;
                AdminBar.ToolTip = null;
            }
            else
            {
                AdminBar.Background = (Brush)FindResource("WarnLightBrush");
                AdminIcon.Text = "\uE72E";
                AdminIcon.Foreground = (Brush)FindResource("WarnBrush");
                AdminText.Text = "未提权，部分功能受限，点击以管理员重启";
                AdminText.Foreground = (Brush)FindResource("WarnBrush");
                AdminBar.Cursor = Cursors.Hand;
                AdminBar.ToolTip = "以管理员身份重新启动";
            }
        }

        void Admin_Click(object sender, MouseButtonEventArgs e)
        {
            if (OS.IsElevated) return;
            if (MessageBox.Show("将以管理员身份重新启动本工具，是否继续？", "提权",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (!AppLifecycle.RestartElevated())
                Notify.Warning("已取消提权，仍在普通权限下运行");
        }

        // ── 命令面板 ──
        void Palette_Click(object sender, RoutedEventArgs e) { OpenPalette(); }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OpenPalette();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && PaletteLayer.Visibility == Visibility.Visible)
            {
                ClosePalette();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CyclePage((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1);
                e.Handled = true;
            }
        }

        void CyclePage(int delta)
        {
            int i = Array.IndexOf(PageKeys, _currentKey);
            if (i < 0) i = 0;
            i = (i + delta + PageKeys.Length) % PageKeys.Length;
            NavigateTo(PageKeys[i]);
        }

        void OpenPalette()
        {
            PaletteLayer.Visibility = Visibility.Visible;
            PaletteInput.Text = "";
            FillPalette("");
            PaletteInput.Focus();
        }

        void ClosePalette()
        {
            PaletteLayer.Visibility = Visibility.Collapsed;
            Host.Focus();
        }

        void PaletteBackdrop_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == PaletteLayer) ClosePalette();
        }

        void PaletteInput_TextChanged(object sender, TextChangedEventArgs e) { FillPalette(PaletteInput.Text); }

        void FillPalette(string q)
        {
            var items = new List<string>();
            foreach (NavItem it in _flat)
            {
                if (string.IsNullOrEmpty(q) || it.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    items.Add("page|" + it.Key + "|跳转到 " + it.Title);
            }
            string[] actions = { "一键优化", "垃圾清理", "创建快照", "整理内存", "刷新 DNS", "重启资源管理器" };
            foreach (string a in actions)
            {
                if (string.IsNullOrEmpty(q) || a.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    items.Add("action|" + a + "|执行：" + a);
            }
            PaletteList.ItemsSource = items;
            if (items.Count > 0) PaletteList.SelectedIndex = 0;
        }

        void PaletteInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { RunPaletteSelection(); e.Handled = true; }
            else if (e.Key == Key.Down && PaletteList.Items.Count > 0)
            {
                PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 1, PaletteList.Items.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && PaletteList.Items.Count > 0)
            {
                PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 1, 0);
                e.Handled = true;
            }
        }

        void PaletteList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { RunPaletteSelection(); e.Handled = true; }
        }

        void PaletteList_DoubleClick(object sender, MouseButtonEventArgs e) { RunPaletteSelection(); }

        void RunPaletteSelection()
        {
            string sel = PaletteList.SelectedItem as string;
            if (sel == null) return;
            ClosePalette();
            string[] parts = sel.Split('|');
            if (parts.Length < 2) return;
            if (parts[0] == "page") { NavigateTo(parts[1]); return; }

            string action = parts[1];
            switch (action)
            {
                case "一键优化": NavigateTo("NavTweak"); break;
                case "垃圾清理": NavigateTo("NavClean"); break;
                case "创建快照": NavigateTo("NavSnapshot"); break;
                case "整理内存":
                    string memResult = null;
                    Ui.RunAsync(delegate { memResult = SystemTools.OptimizeMemoryNow(); },
                        delegate { Notify.Success(string.IsNullOrEmpty(memResult) ? "内存整理完成" : memResult); });
                    break;
                case "刷新 DNS":
                    Ui.RunAsync(delegate { NetworkService.FlushDns(); },
                        delegate { Notify.Success("DNS 缓存已刷新"); });
                    break;
                case "重启资源管理器":
                    SystemTools.RestartExplorer();
                    Notify.Info("已重启资源管理器");
                    break;
            }
        }

        // ── 托盘 ──
        void InitTray()
        {
            try
            {
                _tray = new ShellNotifyIcon("WinTuneBox 优化工具箱");
                _tray.SetIcon(AppIcon.Get(16));
                _tray.Menu = BuildTrayMenu();
                _tray.LeftClick = delegate { ShowMain(); };
                _tray.DoubleClick = delegate { ShowMain(); };
                _tray.Add();
            }
            catch (Exception ex) { Logger.Log("Tray", "初始化失败: " + ex.Message); }
        }

        ContextMenu BuildTrayMenu()
        {
            var menu = new ContextMenu();

            var open = new MenuItem { Header = "打开主界面", FontWeight = FontWeights.SemiBold };
            open.Click += delegate { ShowMain(); };
            menu.Items.Add(open);

            var mem = new MenuItem { Header = "立即优化内存" };
            mem.Click += delegate { OptimizeMemory(); };
            menu.Items.Add(mem);

            menu.Items.Add(new Separator());

            var auto = new MenuItem { Header = "开机自动启动", IsCheckable = true, IsChecked = Settings.AutoStartEnabled() };
            auto.Click += delegate
            {
                string err = Settings.SetAutoStart(auto.IsChecked);
                if (err != null) { Notify.Warning("设置开机自启失败: " + err); auto.IsChecked = !auto.IsChecked; }
                else Notify.Success(auto.IsChecked ? "已设置开机自动启动" : "已取消开机自启");
            };
            menu.Items.Add(auto);

            var ball = new MenuItem { Header = "显示加速悬浮球", IsCheckable = true, IsChecked = Settings.GetBool(Settings.KeyFloatBall, true) };
            ball.Click += delegate { SetFloatBall(ball.IsChecked); };
            menu.Items.Add(ball);

            var tray = new MenuItem { Header = "关闭窗口时最小化到托盘", IsCheckable = true, IsChecked = Settings.GetBool(Settings.KeyMinToTray, true) };
            tray.Click += delegate { Settings.SetBool(Settings.KeyMinToTray, tray.IsChecked); };
            menu.Items.Add(tray);

            menu.Items.Add(new Separator());

            var exit = new MenuItem { Header = "退出" };
            // 必须走 CleanupAndShutdown（内部会 Application.Current.Shutdown）。
            // App.xaml 是 ShutdownMode=OnExplicitShutdown，只调 Close() 只会关掉主窗口，
            // 进程与托盘图标都还在 —— 表现就是「点退出没反应」。
            exit.Click += delegate { _reallyExit = true; CleanupAndShutdown(); };
            menu.Items.Add(exit);

            _trayMenu = menu;
            return menu;
        }

        ContextMenu _trayMenu;

        void ShowMain()
        {
            try
            {
                ShowActivated = true;
                Show();
                ShowInTaskbar = true;
                Opacity = 1;
                WindowState = WindowState.Normal;
                ApplyCompact(ActualWidth < 900);
                Activate();
                Topmost = true;
                Topmost = false;
            }
            catch (Exception ex) { Logger.Log("Tray", "显示主界面失败: " + ex.Message); }
        }

        public void ShowTrayBalloon(string title, string text, int kind)
        {
            try { if (_tray != null) _tray.ShowBalloon(title, text, kind); } catch { }
        }

        void OptimizeMemory() { OptimizeMemory(null); }

        void OptimizeMemory(Action done)
        {
            Notify.Info("正在整理内存…");
            string result = null;
            Ui.RunAsync(delegate { result = SystemTools.OptimizeMemoryNow(); },
                delegate
                {
                    Notify.Success(string.IsNullOrEmpty(result) ? "内存整理完成" : result);
                    if (done != null) done();
                });
        }

        /// <summary>导航栏底部「一键整理内存」：整理期间禁用按钮并改为进行中文案</summary>
        void Mem_Click(object sender, RoutedEventArgs e)
        {
            MemBtn.IsEnabled = false;
            MemLabel.Text = "正在整理…";
            OptimizeMemory(delegate
            {
                MemLabel.Text = "一键整理内存";
                MemBtn.IsEnabled = true;
            });
        }

        // ── 悬浮球 ──
        public void SetFloatBall(bool on)
        {
            Settings.SetBool(Settings.KeyFloatBall, on);
            if (on) ShowFloatBall(); else CloseFloatBall();
            if (_trayMenu != null)
            {
                foreach (object o in _trayMenu.Items)
                {
                    MenuItem mi = o as MenuItem;
                    if (mi != null && mi.Header as string == "显示加速悬浮球") mi.IsChecked = on;
                }
            }
        }

        void ShowFloatBall()
        {
            if (_ball != null) return;
            try
            {
                _ball = new FloatBall();
                _ball.RequestOpenMain = ShowMain;
                _ball.RequestExitBall = delegate { Dispatcher.Invoke(delegate { SetFloatBall(false); }); };
                _ball.Show();
            }
            catch (Exception ex) { Logger.Log("Ball", "创建悬浮球失败: " + ex.Message); }
        }

        void CloseFloatBall()
        {
            try { if (_ball != null) _ball.Close(); } catch { }
            _ball = null;
        }

        // ── 关闭行为 ──
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (_reallyExit) return;
            if (Settings.GetBool(Settings.KeyMinToTray, true))
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                ShowTrayBalloon("WinTuneBox 仍在后台运行", "单击托盘图标可重新打开，右键可退出。", ShellNotifyIcon.BalloonInfo);
            }
            else
            {
                _reallyExit = true;
                CleanupAndShutdown();
            }
        }

        void CleanupAndShutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            try { if (_ball != null) _ball.Close(); } catch { }
            try { if (_tray != null) { _tray.Dispose(); _tray = null; } } catch { }
            try { Notify.Host = null; } catch { }
            AppLifecycle.Release();
            Application.Current.Shutdown();
        }

        // ── 离屏截图（--shot）──
        public int RenderShots(string dir)
        {
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(AppPaths.LogDir, "shots");
            Directory.CreateDirectory(dir);

            string originalMode = ThemeManager.Mode;
            WindowState = WindowState.Normal;
            double oldW = Width, oldH = Height;
            int n = 0;

            string[] themes = { "light", "dark" };
            double[] widths = { 1180, 640 };

            foreach (string theme in themes)
            {
                ThemeManager.SetMode(theme);
                // 每个主题重建页面，保证截图反映真实的首屏状态（也避免复用旧主题的本地画刷）
                _pages.Clear();
                foreach (double w in widths)
                {
                    Width = w;
                    UpdateLayout();
                    Pump(8, 30);
                    foreach (string key in PageKeys)
                    {
                        try
                        {
                            ShowPageForShot(key);
                            PumpUntilIdle(15000);
                            Pump(SettleFrames(key), 45);
                            string file = Path.Combine(dir, key + "-" + theme + "-" + ((int)w) + ".png");
                            if (CaptureTo(file)) n++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Shot", "页面 " + key + " (" + theme + "/" + (int)w + ") 渲染失败: " + ex.Message);
                        }
                    }
                }
            }

            Width = oldW;
            Height = oldH;
            ThemeManager.SetMode(originalMode);
            return n;
        }

        /// <summary>需要后台扫描/采集的页面在截图前多等一会儿，避免抓到空数据帧</summary>
        static int SettleFrames(string key)
        {
            if (key == "NavHealth" || key == "NavStorage") return 70;   // WMI / 磁盘统计较慢
            if (key == "NavStartup") return 120;   // 计划任务扫描较慢
            if (key == "NavClean" || key == "NavService" || key == "NavSoftware" || key == "NavDashboard") return 30;
            return 14;
        }

        void ShowPageForShot(string key)
        {
            foreach (NavItem it in _flat) it.IsSelected = it.Key == key;
            UserControl page;
            if (!_pages.TryGetValue(key, out page) || page == null)
            {
                page = CreatePage(key);
                _pages[key] = page;
            }
            Host.Content = page;
            _currentKey = key;
            TitlePage.Text = "/  " + PageTitleOf(key);
        }

        Brush SolidWindowBrush()
        {
            Brush b = TryFindResource("WindowBackgroundBrush") as Brush;
            if (b != null) return b;
            return new SolidColorBrush(ThemeManager.IsDark ? Color.FromRgb(0x20, 0x21, 0x24) : Color.FromRgb(0xF3, 0xF4, 0xF8));
        }

        bool CaptureTo(string file)
        {
            try
            {
                double w = RootGrid.ActualWidth, h = RootGrid.ActualHeight;
                if (w < 10 || h < 10) return false;
                Brush oldBg = RootGrid.Background;
                RootGrid.Background = SolidWindowBrush();
                UpdateLayout();
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(RootGrid);
                try { RootGrid.Background = oldBg; } catch { }

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (FileStream fs = File.Create(file)) enc.Save(fs);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Shot", "截图失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>等待页面后台加载完成（连续若干帧空闲即认为稳定），避免截到"加载中"空帧</summary>
        void PumpUntilIdle(int maxMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int stable = 0;
            while (sw.ElapsedMilliseconds < maxMs)
            {
                bool idle = Ui.PendingWork == 0;
                Pump(1, 15);
                if (idle)
                {
                    if (++stable >= 5) return;
                }
                else stable = 0;
            }
            Logger.Log("Shot", "等待页面加载超时（" + maxMs + "ms），可能存在未结束的后台任务");
        }

        void Pump(int iterations, int sleepMs)
        {
            for (int i = 0; i < iterations; i++)
            {
                Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Render);
                Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Background);
                System.Threading.Thread.Sleep(sleepMs);
            }
        }
    }
}