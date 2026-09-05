using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 概览：系统信息 / 实时状态 / 内存优化 / 快捷入口 ═══
    public class PgOverview : UPage
    {
        public Action<int> GotoPage;
        public Action<bool> TrayToggle;            // 由主窗体注入：开关托盘常驻
        public Action<bool> FloatToggle;           // 由主窗体注入：开关加速悬浮球
        readonly Timer _timer = new Timer { Interval = 2000 };
        readonly Label _cpuTxt = new Label(), _ramTxt = new Label(), _diskTxt = new Label();
        readonly MBar _cpuBar = new MBar(), _ramBar = new MBar { Fill = C.Green }, _diskBar = new MBar { Fill = C.Orange };
        readonly Label _tip = new Label();
        readonly Label _memNow = new Label();
        readonly Label _memResult = new Label();
        readonly CheckBox _chkTray = new CheckBox();
        readonly CheckBox _chkPin = new CheckBox();
        readonly CheckBox _chkBall = new CheckBox();
        static readonly ToolTip _tipBox = new ToolTip();
        FlatBtn _btnMem;
        bool _memBusy;
        bool _built;
        TableLayoutPanel _grid1;
        Metric[] _met;

        public const string TrayRegKey = @"Software\WinTuneBox";

        public PgOverview()
        {
            Title("概览", "系统信息与状态一览。绿色勾选项 = 已按推荐优化；点击右上角可切换页面。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_built) { Build(); _built = true; }
            RefreshInfo();
            _timer.Start();
        }

        void Build()
        {
            // ── 系统信息卡片 ──
            var c1 = Card();
            CardTitle(c1, "系统信息");
            _grid1 = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = C.S(4 * 56 + 8),
                ColumnCount = 2,
                RowCount = 4,
                Margin = new Padding(0, C.S(4), 0, 0),
            };
            _grid1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _grid1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 4; i++)
                _grid1.RowStyles.Add(new RowStyle(SizeType.Absolute, C.S(56)));
            _met = new Metric[] {
                MkM("操作系统"), MkM("处理器"), MkM("物理内存"), MkM("显卡"),
                MkM("系统盘 (C:)"), MkM("系统安装日期"), MkM("上次开机"), MkM("运行时长"),
            };
            int pos = 0;
            for (int r = 0; r < 4; r++)
                for (int col = 0; col < 2; col++)
                    _grid1.Controls.Add(_met[pos++], col, r);
            c1.Controls.Add(_grid1);

            // ── 实时状态 ──
            var c2 = Card();
            CardTitle(c2, "实时状态");
            var tipTop = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            var barCard = new RCard { Dock = DockStyle.Top, Height = C.S(150), Padding = new Padding(C.S(14), C.S(8), C.S(14), C.S(6)) };

            var r1 = RowWith("CPU 使用率", _cpuTxt, _cpuBar);
            var r2 = RowWith("内存使用率", _ramTxt, _ramBar);
            var r3 = RowWith("磁盘空间使用", _diskTxt, _diskBar);
            barCard.Controls.Add(r3); barCard.Controls.Add(r2); barCard.Controls.Add(r1);
            var tipLabel = new Label
            {
                Text = "提示：内存与磁盘数据来自系统实时查询；CPU 占用为最近 2 秒平均值。",
                Font = C.F(8.5f), ForeColor = C.TextDim, AutoSize = true, Dock = DockStyle.Top,
            };
            var spacer = new Panel { Dock = DockStyle.Top, Height = C.S(4) };
            c2.Controls.Add(spacer);
            c2.Controls.Add(tipLabel);
            c2.Controls.Add(barCard);

            // ── 快捷操作 ──
            var c3 = Card();
            CardTitle(c3, "快捷操作");
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
            };
            AddJump(flow, "一键优化（按推荐执行）", 1, true);
            AddJump(flow, "垃圾清理", 2, false);
            AddJump(flow, "启动项管理", 3, false);
            AddJump(flow, "服务优化建议", 4, false);
            AddJump(flow, "卸载软件", 6, false);
            AddJump(flow, "系统修复工具", 7, false);
            var btnReport = C.Btn("导出系统报告", 1); btnReport.Click += delegate { ExportReport(); };
            flow.Controls.Add(btnReport);
            var btnBak = C.Btn("打开备份目录", 1); btnBak.Click += delegate { App.OpenBackupDir(); };
            flow.Controls.Add(btnBak);
            c3.Controls.Add(flow);

            _tip.Font = C.F(9f);
            _tip.ForeColor = C.Green;
            _tip.AutoSize = true;
            _tip.Dock = DockStyle.Top;
            _tip.Padding = new Padding(0, 0, 0, C.S(4));
            c3.Controls.Add(_tip);

            // ── 内存优化卡（添加顺序=显示顺序：快捷卡之后、结尾留白之前）──
            BuildMemCard();

            EndSpace(20);
            _timer.Tick += delegate { TickLive(); };
            FinishPage();
        }

        // ═══ 内存优化（PCL 风格：点击即优化并报告释放量）═══
        RCard BuildMemCard()
        {
            var cm = Card();
            CardTitle(cm, "内存优化");

            var row1 = new Panel { Dock = DockStyle.Top, Height = C.S(40) };
            row1.Controls.Add(C.Lbl("当前可用物理内存", 9f, C.TextSub, false));
            _memNow.Text = "—";
            _memNow.Font = C.F(12f, true);
            _memNow.ForeColor = C.Accent;
            _memNow.AutoSize = true;
            _memNow.Location = new Point(C.S(130), 0);
            _btnMem = C.Btn("立即优化内存", 0);
            _btnMem.AutoSize = false;
            _btnMem.Width = C.S(140);
            _btnMem.Location = new Point(C.S(300), 0);
            _btnMem.Click += delegate { RunMemOptimize(null); };
            row1.Controls.Add(_memNow);
            row1.Controls.Add(_btnMem);
            cm.Controls.Add(row1);

            _memResult.Font = C.F(9.5f);
            _memResult.ForeColor = C.Green;
            _memResult.AutoSize = true;
            _memResult.Dock = DockStyle.Top;
            _memResult.Padding = new Padding(0, 0, 0, C.S(6));
            cm.Controls.Add(_memResult);

            var row2 = new Panel { Dock = DockStyle.Top, Height = C.S(30) };
            _chkTray.Text = "在任务栏通知区域常驻图标（左键单击=执行一次内存优化，双击=打开工具箱）";
            _chkTray.Font = C.F(9f);
            _chkTray.AutoSize = true;
            _chkTray.ForeColor = C.TextMain;
            _chkTray.Location = new Point(0, C.S(2));
            try
            {
                int? v = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "TrayIcon");
                _chkTray.Checked = v != null && v.Value == 1;
            }
            catch { }
            _chkTray.CheckedChanged += delegate
            {
                try
                {
                    if (_chkTray.Checked)
                        Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "TrayIcon", 1);
                    else
                        Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "TrayIcon");
                }
                catch { }
                if (TrayToggle != null) TrayToggle(_chkTray.Checked);
            };
            row2.Controls.Add(_chkTray);
            cm.Controls.Add(row2);

            // 让图标固定在托盘可见区：系统级“始终显示所有托盘图标”
            var row3 = new Panel { Dock = DockStyle.Top, Height = C.S(30) };
            _chkPin.Text = "让本工具图标固定显示在托盘可见区（开启系统“始终显示所有图标”，无需再点 ^ 展开）";
            _chkPin.Font = C.F(9f);
            _chkPin.AutoSize = true;
            _chkPin.ForeColor = C.TextMain;
            _chkPin.Location = new Point(0, C.S(2));
            try
            {
                int? v = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray");
                _chkPin.Checked = v != null && v.Value == 1;
            }
            catch { }
            _chkPin.CheckedChanged += delegate
            {
                try
                {
                    if (_chkPin.Checked)
                        Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default,
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray", 1);
                    else
                        Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default,
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray");
                }
                catch { }
            };
            row3.Controls.Add(_chkPin);
            var pinBtn = C.Btn("重启资源管理器生效", 1);
            pinBtn.AutoSize = false;
            pinBtn.Width = C.S(170);
            pinBtn.Location = new Point(C.S(560), 0);
            pinBtn.Click += delegate
            {
                if (C.Ask("将重启资源管理器使托盘设置生效。\n桌面上会短暂刷新，已打开的资源管理器窗口会关闭。确定？",
                        "重启资源管理器", true) != DialogResult.Yes) return;
                SysTools.RestartExplorer();
            };
            // Win11 无 EnableAutoTray 总开关：置灰并引导手动设置
            bool pinOk = !OS.IsWin11;
            _chkPin.Enabled = pinOk;
            pinBtn.Enabled = pinOk;
            _chkPin.ForeColor = pinOk ? C.TextMain : C.TextDim;
            if (!pinOk)
                _tipBox.SetToolTip(_chkPin, "Win11 请使用：设置 → 个性化 → 任务栏 → 任务栏角落图标，把本程序设为“显示”，或将图标拖出 ^ 折叠区一次");
            row3.Controls.Add(pinBtn);
            cm.Controls.Add(row3);

            // 加速悬浮球
            var row4 = new Panel { Dock = DockStyle.Top, Height = C.S(30) };
            _chkBall.Text = "启用加速悬浮球（闲置自动贴边隐藏并半透明；拖动移动，单击=内存优化，双击=打开工具箱）";
            _chkBall.Font = C.F(9f);
            _chkBall.AutoSize = true;
            _chkBall.ForeColor = C.TextMain;
            _chkBall.Location = new Point(0, C.S(2));
            try
            {
                int? v = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "FloatBall");
                _chkBall.Checked = v != null && v.Value == 1;
            }
            catch { }
            _chkBall.CheckedChanged += delegate
            {
                try
                {
                    if (_chkBall.Checked)
                        Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "FloatBall", 1);
                    else
                        Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, TrayRegKey, "FloatBall");
                }
                catch { }
                if (FloatToggle != null) FloatToggle(_chkBall.Checked);
            };
            row4.Controls.Add(_chkBall);
            cm.Controls.Add(row4);

            CardNote(cm, "优化原理与 PCL 启动器一致：清空各进程的工作集内存，系统会把这些页面转为可用内存。程序重新读写时会自然恢复，属正常现象。\n说明：“固定显示”会同时展开系统其他被隐藏的托盘图标（Win10/7 有效）；Win11 请到 设置→个性化→任务栏→任务栏角落图标 中把本程序设为“显示”，或将图标从 ^ 折叠区拖出一次即可固定。悬浮球常驻内存占用极小（空闲仅 500ms 低频监测，动画仅在滑入滑出时运行）。");
            return cm;
        }

        /// <summary>悬浮球被右键“关闭”时同步勾选状态（由主窗体回调）</summary>
        public void SyncBallCheck(bool on)
        {
            C.On(this, delegate { if (!_chkBall.IsDisposed) _chkBall.Checked = on; });
        }

        /// <summary>执行一次内存优化（后台线程，完成后更新结果标签）。fromTray 来自托盘时顺带弹气泡交给主窗体处理。</summary>
        public void RunMemOptimize(Action<string> onDone)
        {
            if (_memBusy) return;
            _memBusy = true;
            if (_btnMem != null) { _btnMem.Enabled = false; _btnMem.Text = "优化中…"; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string report;
                try { report = SysTools.OptimizeMemoryNow(); }
                catch (Exception ex) { report = "优化失败: " + ex.Message; }
                C.On(this, delegate
                {
                    if (_btnMem != null) { _btnMem.Enabled = true; _btnMem.Text = "立即优化内存"; }
                    _memResult.Text = "最近一次：" + report;
                    if (onDone != null) onDone(report);
                    _memBusy = false;
                });
            });
        }

        public void SetMemResultText(string report)
        {
            C.On(this, delegate { _memResult.Text = "最近一次：" + report; });
        }

        Metric MkM(string cap)
        {
            Control parent = _grid1 == null ? (Control)this : (Control)_grid1;
            var m = new Metric(parent, cap);
            m.Dock = DockStyle.Fill;
            m.Value.MaximumSize = new Size(C.S(560), 0);
            return m;
        }

        Panel RowWith(string cap, Label num, MBar bar)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = C.S(38) };
            var l = new Label { Text = cap, Font = C.F(9f), ForeColor = C.TextSub, AutoSize = true, Location = new Point(0, C.S(4)) };
            num.Font = C.F(9.5f, true);
            num.ForeColor = C.TextMain;
            num.AutoSize = true;
            num.Location = new Point(C.S(130), C.S(2));
            bar.Width = C.S(320);
            bar.Height = C.S(8);
            bar.Location = new Point(C.S(130), C.S(24));
            p.Controls.Add(l); p.Controls.Add(num); p.Controls.Add(bar);
            return p;
        }

        void AddJump(FlowLayoutPanel flow, string text, int page, bool main)
        {
            var b = C.Btn(text, main ? 0 : 1);
            b.Click += delegate { if (GotoPage != null) GotoPage(page); };
            flow.Controls.Add(b);
        }

        // ── 刷新 ──
        public void RefreshInfo()
        {
            OS.CollectInfo();
            OS.RefreshMem();
            ulong free, total;
            OS.DiskInfo(OS.SystemDrive, out free, out total);
            _tip.Text = RefreshTipText();

            SetGridMetric(0, 0, OS.WinVerFull + (OS.IsX64 ? "  (64 位)" : "  (32 位)"));
            SetGridMetric(1, 0, OS.CpuName + "\r\n" + OS.CpuCores + " 个逻辑处理器");
            SetGridMetric(0, 1, C.Bytes((long)OS.TotalRam) + "（当前可用 " + C.Bytes((long)OS.FreeRam) + "）");
            SetGridMetric(1, 1, string.IsNullOrEmpty(OS.GpuName) ? "（未检测到独立显卡信息）" : OS.GpuName);
            string used = total == 0 ? "-" : C.Bytes((long)(total - free)) + " / " + C.Bytes((long)total) + " 可用"
                + string.Format("（{0:P0}）", (double)(total - free) / total);
            SetGridMetric(0, 2, used);
            SetGridMetric(1, 2, OS.InstallDate == DateTime.MinValue ? "-" : OS.InstallDate.ToString("yyyy-MM-dd"));
            DateTime bt = OS.BootTime;
            if (bt != DateTime.MinValue)
            {
                SetGridMetric(0, 3, bt.ToString("yyyy-MM-dd HH:mm:ss"));
                TimeSpan up = DateTime.Now - bt;
                SetGridMetric(1, 3, string.Format("{0} 天 {1} 小时 {2} 分", up.Days, up.Hours, up.Minutes));
            }
            else { SetGridMetric(0, 3, "-"); SetGridMetric(1, 3, "-"); }
            TickLive();
        }

        void SetGridMetric(int col, int row, string text)
        {
            if (_met == null) return;
            int idx = row * 2 + col;
            if (idx >= 0 && idx < _met.Length) _met[idx].SetText(text);
        }

        string RefreshTipText()
        {
            int canDo = 0, done = 0;
            try
            {
                OpData.Build();
                foreach (var op in OpData.All)
                {
                    if (op.Supported == null || !op.Supported()) continue;
                    try
                    {
                        int st = op.Detect();
                        if (st == 1) done++;
                        else if (st == 0) canDo++;
                    }
                    catch { }
                }
            }
            catch { }
            return string.Format("系统检测：{0} 项待优化 · {1} 项已按推荐设置", canDo, done);
        }

        void TickLive()
        {
            int cpu = OS.CpuPercent();
            OS.RefreshMem();
            ulong free, total;
            OS.DiskInfo(OS.SystemDrive, out free, out total);
            _cpuTxt.Text = cpu + " %";
            _ramTxt.Text = OS.RamLoad + " %";
            if (_memNow != null) _memNow.Text = (OS.FreeRam / 1073741824.0).ToString("F2") + " GB";
            _cpuBar.Value = cpu;
            _ramBar.Value = OS.RamLoad;
            if (total > 0)
            {
                double usedPct = (double)(total - free) / total * 100;
                _diskBar.Value = (float)usedPct;
                _diskTxt.Text = usedPct.ToString("F0") + " %";
            }
        }

        // ── 导出报告 ──
        void ExportReport()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt",
                    FileName = "系统报告-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("════ Windows 系统报告（WinTuneBox）════");
                sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("操作系统: " + OS.WinVerFull + (OS.IsX64 ? "  (64 位)" : "  (32 位)"));
                sb.AppendLine("处理器:   " + OS.CpuName + "  ·  " + OS.CpuCores + " 逻辑处理器");
                sb.AppendLine("内存:     " + C.Bytes((long)OS.TotalRam));
                sb.AppendLine("显卡:     " + (string.IsNullOrEmpty(OS.GpuName) ? "未知" : OS.GpuName));
                ulong free, total;
                if (OS.DiskInfo(OS.SystemDrive, out free, out total))
                    sb.AppendLine("系统盘:   " + C.Bytes((long)total) + "（可用 " + C.Bytes((long)free) + "）");
                sb.AppendLine("系统安装: " + (OS.InstallDate == DateTime.MinValue ? "-" : OS.InstallDate.ToString("yyyy-MM-dd")));
                sb.AppendLine("开机时间: " + (OS.BootTime == DateTime.MinValue ? "-" : OS.BootTime.ToString("yyyy-MM-dd HH:mm:ss")));
                sb.AppendLine();
                sb.AppendLine("已装软件数: " + SoftMgr.ReadAll().Count);
                sb.AppendLine("启动项数: " + StartupMgr.GetAll().Count);
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(this, "已保存到:\n" + dlg.FileName, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
