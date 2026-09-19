using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace WinTune.Wpf.Views
{
    /// <summary>
    /// 概览：健康分圆环 + 关键指标 + 实时趋势（仅在页面可见时以 1s 采样，离开立即停止）。
    /// </summary>
    public partial class DashboardPage : UserControl
    {
        const int MaxSamples = 60;

        readonly List<double> _cpu = new List<double>();
        readonly List<double> _mem = new List<double>();

        RingGauge _gauge;
        TextBlock _advice, _issues, _cpuNow, _memNow;
        Sparkline _cpuLine, _memLine;
        TextBlock[] _stats;
        StackPanel _infoBody;
        Grid _colsGrid;
        UIElement _left, _right;
        DispatcherTimer _timer;
        bool _built, _compact;

        public DashboardPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } Reload(); };
            IsVisibleChanged += OnVisibleChanged;
            Unloaded += delegate { StopTimer(); };
        }

        // ── 构建 ──
        void Build()
        {
            StackPanel content; WrapPanel actions;
            Root.Children.Add(Ui.PageRoot("概览", "系统状态一览、健康评分与常用功能入口", out content, out actions));

            actions.Children.Add(Ui.Btn("重新评估", "BtnDefault", Reload));
            var go = Ui.Btn("立即一键优化", "BtnPrimary", delegate { App.Win.NavigateTo("NavTweak"); });
            go.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(go);

            content.Children.Add(StatCards());
            content.Children.Add(BuildColumns());
        }

        UIElement StatCards()
        {
            var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 0) };
            stats.Children.Add(Ui.Stat("\uEDA3", "—", "系统盘可用", "InfoBrush"));
            stats.Children.Add(Ui.Stat("\uE945", "—", "内存占用", "OkBrush"));
            stats.Children.Add(Ui.Stat("\uE7C1", "—", "已启用启动项", "WarnBrush"));
            stats.Children.Add(Ui.Stat("\uE9D9", "—", "待优化项", "ErrBrush"));

            _stats = new TextBlock[4];
            for (int i = 0; i < 4; i++) _stats[i] = (TextBlock)((Border)stats.Children[i]).Tag;
            return stats;
        }

        UIElement BuildColumns()
        {
            _colsGrid = new Grid();
            _colsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
            _colsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _colsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _colsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _left = LeftColumn();
            _right = RightColumn();
            Grid.SetRow(_left, 0); Grid.SetColumn(_left, 0);
            Grid.SetRow(_right, 0); Grid.SetColumn(_right, 1);
            _colsGrid.Children.Add(_left);
            _colsGrid.Children.Add(_right);
            return _colsGrid;
        }

        UIElement LeftColumn()
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };

            StackPanel hb;
            var health = Ui.Card("系统健康度", "综合安全体检、优化进度、磁盘与内存评估", out hb);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _gauge = new RingGauge
            {
                Width = 104,
                Height = 104,
                Value = 0,
                RingThickness = 10,
                ValueText = "--",
                Caption = "健康分",

            };
            _gauge.Themed(RingGauge.TrackBrushProperty, "TrackBrush");
            _gauge.Themed(RingGauge.RingBrushProperty, "BrandBrush");
            Grid.SetColumn(_gauge, 0);
            row.Children.Add(_gauge);

            var txt = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _advice = Ui.T("正在评估…", 13.5, Ui.TextBrush, true);
            _advice.TextWrapping = TextWrapping.Wrap;
            _issues = Ui.T("", 12, Ui.SubBrush);
            _issues.TextWrapping = TextWrapping.Wrap;
            _issues.Margin = new Thickness(0, 6, 0, 0);
            txt.Children.Add(_advice);
            txt.Children.Add(_issues);
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);
            hb.Children.Add(row);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            btnRow.Children.Add(Ui.Btn("一键优化", "BtnPrimary", delegate { App.Win.NavigateTo("NavTweak"); }));
            btnRow.Children.Add(Gap(Ui.Btn("垃圾清理", "BtnDefault", delegate { App.Win.NavigateTo("NavClean"); })));
            btnRow.Children.Add(Gap(Ui.Btn("安全体检", "BtnDefault", delegate { App.Win.NavigateTo("NavHealth"); })));
            btnRow.Children.Add(Gap(Ui.Btn("创建快照", "BtnDefault", delegate { App.Win.NavigateTo("NavSnapshot"); })));
            hb.Children.Add(btnRow);
            box.Children.Add(health);

            StackPanel qb;
            var quick = Ui.Card("快捷入口", "常用系统工具与功能", out qb);
            var qg = new UniformGrid { Columns = 3 };
            string[,] entries = {
                { "启动项管理", "NavStartup" }, { "服务优化", "NavService" }, { "网络工具", "NavNetwork" },
                { "软件管理", "NavSoftware" }, { "存储分析", "NavStorage" }, { "更多工具", "NavTools" },
            };
            for (int i = 0; i < entries.GetLength(0); i++)
            {
                string key = entries[i, 1];
                var b = Ui.Btn(entries[i, 0], "BtnDefault", delegate { App.Win.NavigateTo(key); });
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                b.Margin = new Thickness(0, 0, 8, 8);
                qg.Children.Add(b);
            }
            qb.Children.Add(qg);
            box.Children.Add(quick);
            return box;
        }

        static Button Gap(Button b) { b.Margin = new Thickness(8, 0, 0, 0); return b; }

        UIElement RightColumn()
        {
            var box = new StackPanel();

            StackPanel mb;
            var monitor = Ui.Card("实时监控", "仅在“概览”页面可见时每秒采样，离开页面立即停止", out mb);
            mb.Children.Add(SampleRow("\uE950", "CPU 占用", out _cpuLine, out _cpuNow, "InfoBrush"));
            mb.Children.Add(Ui.Divider());
            mb.Children.Add(SampleRow("\uE964", "内存占用", out _memLine, out _memNow, "OkBrush"));
            box.Children.Add(monitor);

            var info = Ui.Card("系统信息", "", out _infoBody);
            box.Children.Add(info);
            return box;
        }

        UIElement SampleRow(string glyph, string label, out Sparkline line, out TextBlock now, string brushKey)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Ui.Glyph(glyph, 13, brushKey));
            var lb = Ui.T(label, 12.2, Ui.SubBrush);
            lb.Margin = new Thickness(7, 0, 0, 0);
            lb.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(lb);
            Grid.SetColumn(head, 0);

            now = Ui.Tk("—", 15, brushKey, true);
            now.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(now, 1);

            var sp = new Sparkline
            {
                Height = 42,
                Margin = new Thickness(0, 8, 0, 0),
                LineThickness = 1.6,
                Values = new List<double>(),
            };
            sp.Themed(Sparkline.LineBrushProperty, brushKey);
            line = sp;
            Grid.SetColumn(sp, 0);
            Grid.SetColumnSpan(sp, 2);
            Grid.SetRow(sp, 1);
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            g.Children.Add(head);
            g.Children.Add(now);
            g.Children.Add(sp);
            return g;
        }

        // ── 采样 ──
        void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_built) return;
            if (IsVisible) StartTimer(); else StopTimer();
        }

        void StartTimer()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Sample(); };
            _timer.Start();
            Sample();
        }

        void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer = null;
        }

        void Sample()
        {
            try
            {
                OS.RefreshMem();
                Push(_cpu, OS.CpuPercent());
                Push(_mem, OS.TotalRam == 0 ? 0 : (OS.TotalRam - OS.FreeRam) * 100.0 / OS.TotalRam);
                _cpuLine.Values = new List<double>(_cpu);
                _memLine.Values = new List<double>(_mem);
                _cpuNow.Text = _cpu[_cpu.Count - 1].ToString("0") + "%";
                _memNow.Text = _mem[_mem.Count - 1].ToString("0") + "%";
            }
            catch (Exception ex) { Logger.Log("Dash", "采样失败: " + ex.Message); }
        }

        static void Push(List<double> list, double v)
        {
            list.Add(v);
            while (list.Count > MaxSamples) list.RemoveAt(0);
        }

        // ── 评估 ──
        void Reload()
        {
            Ui.RunAsync(delegate
            {
                OS.Refresh();

                string sysRoot = Environment.GetEnvironmentVariable("SystemDrive") + "\\";
                OS.DiskItem sys = null;
                foreach (OS.DiskItem d in OS.AllDisks()) if (d.Name == sysRoot) { sys = d; break; }
                double diskFreePct = sys != null ? sys.FreePercent : 0;

                int startupTotal = 0, enabled = 0;
                try
                {
                    List<StartupEntry> su = StartupService.GetAll();
                    startupTotal = su.Count;
                    foreach (StartupEntry e in su) if (e.Enabled) enabled++;
                }
                catch { }

                int total = 0, done = 0, pending = 0;
                foreach (OneOp t in TweakService.All)
                {
                    int st;
                    try { st = (t.Supported != null && !t.Supported()) ? 3 : (t.Detect == null ? 3 : t.Detect()); }
                    catch { st = 3; }
                    total++;
                    if (st == 1) done++;
                    else if (st == 0) pending++;
                }

                int secIssues = 0;
                string secAdvice = "";
                int secScore = 0;
                try { secScore = SecurityService.HealthScore(out secAdvice, out secIssues); } catch { }

                double progress = total > 0 ? (double)done / total : 0;
                double mem = OS.RamUsagePercent;
                double score = secScore * 0.45
                             + progress * 100 * 0.30
                             + Math.Min(1, diskFreePct / 15.0) * 100 * 0.15
                             + (mem < 85 ? (1 - Math.Max(0, mem - 50) / 35.0) * 100 : 0) * 0.10;
                if (score < 0) score = 0;
                if (score > 100) score = 100;

                string advice = score >= 88 ? "状态优秀，系统运行良好"
                    : score >= 75 ? "整体良好，仍有少量可优化空间"
                    : score >= 55 ? "存在可优化空间，建议执行一键优化与垃圾清理"
                    : "建议尽快执行一键优化、垃圾清理并关注安全体检建议";

                string pg, pgn;
                OS.ActivePowerScheme(out pg, out pgn);
                string gpu = OS.GpuAll ?? "—";

                List<OS.DiskItem> disks = OS.AllDisks();

                Dispatcher.Invoke(delegate
                {
                    _stats[0].Text = diskFreePct.ToString("0") + "%";
                    _stats[1].Text = mem.ToString("0") + "%";
                    _stats[2].Text = enabled + " / " + startupTotal;
                    _stats[3].Text = pending.ToString();

                    _gauge.Value = score;
                    _gauge.ValueText = score.ToString("0");
                    _gauge.Themed(RingGauge.RingBrushProperty, score >= 85 ? "OkBrush" : (score >= 60 ? "BrandBrush" : "WarnBrush"));
                    _advice.Text = advice;
                    _issues.Text = "安全体检 " + secScore + " 分" +
                        (secIssues > 0 ? " · " + secIssues + " 项待关注" : " · 无待关注项") +
                        "；优化进度 " + done + " / " + total + " 项";

                    _infoBody.Children.Clear();
                    _infoBody.Children.Add(Ui.InfoRow("操作系统", OS.WinVerFull));
                    _infoBody.Children.Add(Ui.InfoRow("内部版本", OS.Build + (OS.IsElevated ? "（管理员）" : "（标准用户）")));
                    _infoBody.Children.Add(Ui.InfoRow("处理器", OS.CpuName ?? "—"));
                    _infoBody.Children.Add(Ui.InfoRow("核心 / 线程", OS.CpuCores + " 核 / " + OS.CpuThreads + " 线程"));
                    _infoBody.Children.Add(Ui.InfoRow("内存", string.Format("{0:F1} GB（可用 {1:F1} GB）",
                        OS.TotalRam / 1024.0 / 1024 / 1024, OS.FreeRam / 1024.0 / 1024 / 1024)));
                    _infoBody.Children.Add(Ui.InfoRow("显卡", gpu));
                    _infoBody.Children.Add(Ui.InfoRow("电源计划", string.IsNullOrEmpty(pgn) ? "—" : pgn));
                    _infoBody.Children.Add(Ui.InfoRow("设备类型", OS.HasBattery ? "笔记本 / 便携设备" : "台式机"));
                    for (int i = 0; i < disks.Count; i++)
                    {
                        OS.DiskItem d = disks[i];
                        _infoBody.Children.Add(Ui.InfoRow("磁盘 " + d.Root,
                            string.Format("{0:F1} / {1:F1} GB 可用", d.FreeGB, d.SizeGB)));
                    }
                    _infoBody.Children.Add(Ui.InfoRow("数据目录", AppPaths.Root));
                });
            });
        }

        // ── 窄窗口自适应 ──
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (_colsGrid == null) return;
            bool compact = info.NewSize.Width < 860;
            if (compact == _compact) return;
            _compact = compact;
            if (compact)
            {
                ((StackPanel)_left).Margin = new Thickness(0);
                Grid.SetColumn(_left, 0);
                Grid.SetColumnSpan(_left, 2);
                Grid.SetRow(_right, 1);
                Grid.SetColumn(_right, 0);
                Grid.SetColumnSpan(_right, 2);
            }
            else
            {
                ((StackPanel)_left).Margin = new Thickness(0, 0, 9, 0);
                Grid.SetColumn(_left, 0);
                Grid.SetColumnSpan(_left, 1);
                Grid.SetRow(_right, 0);
                Grid.SetColumn(_right, 1);
                Grid.SetColumnSpan(_right, 1);
            }
        }
    }
}