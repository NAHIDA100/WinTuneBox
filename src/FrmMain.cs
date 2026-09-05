using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 主窗体：左侧导航 + 内容区 ═══
    public class FrmMain : Form
    {
        public const string Ver = "1.0.13";
        public const string AppName = "Windows 优化工具箱";

        const int SIDEBAR_W = 186;

        readonly UPage[] _pages;
        readonly NavBtn[] _nav;
        readonly Panel _host = new Panel();
        readonly Label _perm = new Label();
        int _cur = -1;
        PgOverview _ov;
        readonly Timer _relay = new Timer { Interval = 260 };   // Resize/显示变化后延迟整页重排

        // ── 托盘常驻（内存优化，PCL 风格）──
        NotifyIcon _tray;
        bool _trayOptBusy;
        DateTime _lastTrayLeft = DateTime.MinValue;
        bool _trayPendingClick;                    // 单击防抖：350ms 内第二次单击视为双击
        bool _startMin;                            // --minimized：静默启动到托盘
        bool _trayCloseHinted;                     // 关闭到托盘首次提示
        FloatBall _ball;                           // 加速悬浮球
        uint _taskbarMsg;                          // TaskbarCreated：Explorer 重启后需重注册托盘图标

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint RegisterWindowMessageW(string lpString);

        public FrmMain(bool minimized = false)
        {
            _startMin = minimized;
            Text = AppName + "  v" + Ver;
            BackColor = C.PageBg;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(C.S(1000), C.S(650));
            Size = new Size(C.S(1180), C.S(760));
            StartPosition = FormStartPosition.CenterScreen;
            Font = C.F(9f);
            DoubleBuffered = true;
            if (_startMin)
            {
                // 静默启动：先不透明度归零再隐藏，避免开机瞬间闪窗
                Opacity = 0;
                ShowInTaskbar = false;
            }

            // ── 侧边栏 ──
            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = C.S(SIDEBAR_W),
                BackColor = Color.White,
                Padding = new Padding(0, 0, 0, 0),
            };
            var sbBorder = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = C.Border };
            sidebar.Controls.Add(sbBorder);

            // 标题区（Padding 顶部过大曾导致大字纵向被裁，改为足够高度 + 合适内边距）
            var logo = new Label
            {
                Text = AppName,
                Font = C.F(14f, true),
                ForeColor = C.TextMain,
                Dock = DockStyle.Top,
                Height = C.S(46),
                Padding = new Padding(C.S(22), C.S(12), C.S(6), 0),
                AutoEllipsis = true,
            };
            var logoSub = new Label
            {
                Text = "Win7 x86 → Win11 x64",
                Font = C.F(8.5f),
                ForeColor = C.TextDim,
                Dock = DockStyle.Top,
                Height = C.S(26),
                Padding = new Padding(C.S(23), 0, 0, C.S(8)),
            };
            sidebar.Controls.Add(logoSub);
            sidebar.Controls.Add(logo);

            // 导航按钮
            _nav = new NavBtn[] {
                new NavBtn("概览"),
                new NavBtn("一键优化"),
                new NavBtn("垃圾清理"),
                new NavBtn("启动项管理"),
                new NavBtn("服务优化"),
                new NavBtn("网络工具"),
                new NavBtn("软件管理"),
                new NavBtn("更多工具"),
            };
            var navHolder = new Panel { Dock = DockStyle.Top, Height = C.S(_nav.Length * 44 + 10) };
            for (int i = 0; i < _nav.Length; i++)
            {
                _nav[i].Bounds = new Rectangle(C.S(10), C.S(8 + i * 44), C.S(SIDEBAR_W - 20), C.S(36));
                int idx = i;
                _nav[i].Click += delegate { Go(idx); };
                navHolder.Controls.Add(_nav[i]);
            }
            sidebar.Controls.Add(navHolder);

            // 权限条 + 底部
            _perm.Dock = DockStyle.Bottom;
            _perm.Height = C.S(30);
            _perm.TextAlign = ContentAlignment.MiddleCenter;
            _perm.Font = C.F(9f);
            _perm.Cursor = Cursors.Hand;
            _perm.Click += delegate
            {
                if (!OS.IsElevated)
                {
                    if (C.Ask("以管理员身份重启本工具？\n（重启后需要 UAC 确认）", "提权") == DialogResult.Yes)
                        ElevateSelf();
                }
            };
            sidebar.Controls.Add(_perm);

            var footer = new Label
            {
                Text = "开源 · 仅个人/学习使用\n操作前自动备份，可一键撤销",
                Font = C.F(8f),
                ForeColor = C.TextDim,
                Dock = DockStyle.Bottom,
                Height = C.S(52),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            sidebar.Controls.Add(footer);
            sidebar.Controls.SetChildIndex(footer, 0);
            sidebar.Controls.SetChildIndex(_perm, 0);
            sidebar.Controls.SetChildIndex(navHolder, 0);
            sidebar.Controls.SetChildIndex(logo, 0);

            Controls.Add(_host);
            Controls.Add(sidebar);

            // 内容宿主
            _host.Dock = DockStyle.Fill;
            _host.Padding = new Padding(C.S(20), C.S(16), C.S(16), C.S(8));

            // ── 页面 ──
            _ov = new PgOverview();
            var pages = new UPage[] {
                _ov,
                new PgOneKey(),
                new PgClean(),
                new PgStartup(),
                new PgServices(),
                new PgNet(),
                new PgSoftware(),
                new PgTools(),
            };
            _pages = pages;
            _ov.GotoPage = Go;
            _ov.TrayToggle = TraySet;
            _ov.FloatToggle = FloatBallSet;
            foreach (var p in _pages)
            {
                p.Visible = false;
                p.Dock = DockStyle.Fill;
                p.Margin = new Padding(0);
                _host.Controls.Add(p);
            }

            Shown += delegate
            {
                Go(0);
                UpdatePerm();
                if (_startMin)
                {
                    // 静默模式：确保托盘在 → 隐藏主窗口（右下角只留托盘图标）
                    if (_tray == null) TraySet(true);
                    try
                    {
                        Opacity = 1;
                        Hide();
                    }
                    catch { }
                    return;
                }
                if (!OS.IsElevated)
                    MessageBox.Show(this,
                        "当前未以管理员身份运行。\n\n绝大多数优化/清理功能（服务、注册表 HKLM、hosts、系统修复）需要管理员权限。\n可在左侧底部“以管理员身份运行”处一键提权重启，普通浏览不受影响。",
                        "权限提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // 托盘启用时：点 X = 最小化到托盘（右键托盘→退出 才是真正退出）
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && _tray != null)
                {
                    e.Cancel = true;
                    Hide();
                    if (!_trayCloseHinted)
                    {
                        _trayCloseHinted = true;
                        try
                        {
                            _tray.BalloonTipTitle = "已在后台继续运行";
                            _tray.BalloonTipText = "点击右上角 × 只会最小化到托盘，内存优化/悬浮球等功能仍在工作。\n要完全退出请右键托盘图标 → 退出。";
                            _tray.ShowBalloonTip(3000);
                        }
                        catch { }
                    }
                    return;
                }
            };

            // 若上次启用了托盘/悬浮球常驻，本次自动恢复
            try
            {
                int? v = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default,
                    PgOverview.TrayRegKey, "TrayIcon");
                if (v != null && v.Value == 1) TraySet(true);
                int? b = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default,
                    PgOverview.TrayRegKey, "FloatBall");
                if (b != null && b.Value == 1) FloatBallSet(true);
            }
            catch { }
            FormClosed += delegate
            {
                DisposeTray();
                DisposeBall();
                try { _relay.Stop(); _relay.Dispose(); } catch { }
            };

            // 尺寸/显示环境变化 → 延迟整页强制重排（修复全屏切换/还原后布局不恢复的问题）
            _relay.Tick += delegate
            {
                _relay.Stop();
                RelayoutPages();
            };
            Resize += delegate
            {
                if (Width > 0 && Height > 0 && Visible) _relay.Start();
            };
        }

        /// <summary>强制所有页面重新布局（卡片 AutoFit 按当前宽度重算高度）</summary>
        void RelayoutPages()
        {
            if (_pages == null) return;
            foreach (var p in _pages)
            {
                if (p == null || p.IsDisposed) continue;
                try
                {
                    p.SuspendLayout();
                    p.Root.SuspendLayout();
                    p.Root.ResumeLayout(true);
                    p.ResumeLayout(true);
                    p.PerformLayout();
                    p.Invalidate(true);
                }
                catch { }
            }
            try { Invalidate(true); } catch { }
        }

        // ═══ 加速悬浮球开关 ═══
        void FloatBallSet(bool on)
        {
            if (on)
            {
                if (_ball != null) return;
                _ball = new FloatBall();
                _ball.OnOpenMain = ShowMain;
                _ball.OnReport = delegate(string s) { if (_ov != null) _ov.SetMemResultText(s); };
                _ball.OnBallClosed = delegate
                {
                    try
                    {
                        Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default,
                            PgOverview.TrayRegKey, "FloatBall");
                    }
                    catch { }
                    _ball = null;
                    if (_ov != null) _ov.SyncBallCheck(false);
                };
                _ball.Show();
                try { _ball.Activate(); } catch { }
            }
            else
            {
                DisposeBall();
            }
        }

        void DisposeBall()
        {
            if (_ball != null)
            {
                try { _ball.CloseBall(); } catch { }
                _ball = null;
            }
        }

        // ═══ 托盘常驻：单击=内存优化，双击=打开主界面，右键=菜单 ═══
        void TraySet(bool on)
        {
            if (on)
            {
                if (_tray != null) return;
                if (_taskbarMsg == 0)
                    _taskbarMsg = RegisterWindowMessageW("TaskbarCreated");  // Explorer 重启恢复托盘
                _tray = new NotifyIcon();
                try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { _tray.Icon = SystemIcons.Application; }
                _tray.Text = "Windows 优化工具箱（左键单击=内存优化，双击=打开）";
                _tray.Visible = true;
                _tray.MouseClick += TrayMouseClick;
                _tray.MouseDoubleClick += TrayMouseDoubleClick;
                _tray.ContextMenuStrip = TrayMenu();
                _tray.ShowBalloonTip(2500, "Windows 优化工具箱",
                    "已常驻通知区域：左键单击 = 内存优化，双击 = 打开工具箱。\n若图标被折叠，请把它从 ^ 区拖出一次即可固定；Win11 也可在 设置→个性化→任务栏→任务栏角落图标 中设为显示。",
                    ToolTipIcon.Info);
            }
            else
            {
                DisposeTray();
            }
        }

        /// <summary>收到 TaskbarCreated（资源管理器重启/崩溃恢复）→ 重新注册托盘图标；
        /// WM_DISPLAYCHANGE（分辨率/全屏切换）→ 整页重排并把悬浮球拉回屏幕内</summary>
        protected override void WndProc(ref Message m)
        {
            if (_taskbarMsg != 0 && m.Msg == (int)_taskbarMsg)
            {
                var n = _tray;
                if (n != null)
                {
                    try { n.Visible = false; n.Visible = true; } catch { }
                }
            }
            else if (m.Msg == 0x007E /*WM_DISPLAYCHANGE*/)
            {
                var b = _ball;
                if (b != null)
                {
                    try { b.SnapInside(); } catch { }
                }
                _relay.Start();
            }
            base.WndProc(ref m);
        }

        void DisposeTray()
        {
            if (_tray != null)
            {
                try { _tray.Visible = false; } catch { }
                try { _tray.Dispose(); } catch { }
                _tray = null;
            }
        }

        ContextMenuStrip TrayMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = C.F(9f);
            m.Items.Add("立即内存优化", null, delegate { TrayOptimize(); });
            m.Items.Add("打开工具箱", null, delegate { ShowMain(); });
            m.Items.Add("隐藏窗口（保持常驻）", null, delegate { Hide(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("退出", null, delegate { Application.Exit(); });
            return m;
        }

        void ShowMain()
        {
            if (InvokeRequired) { try { BeginInvoke((MethodInvoker)ShowMain); } catch { } return; }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;         // 静默模式关闭了任务栏按钮，打开主界面时恢复
            try { Opacity = 1; } catch { }
            Show();
            BringToFront();
            Activate();
        }

        void TrayMouseClick(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // 防抖：双击会触发两次单击；350ms 内的第二次单击视为双击的一部分，不执行优化
            if ((DateTime.Now - _lastTrayLeft).TotalMilliseconds < 350)
            {
                _lastTrayLeft = DateTime.Now;
                _trayPendingClick = false;
                return;
            }
            _lastTrayLeft = DateTime.Now;
            _trayPendingClick = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                System.Threading.Thread.Sleep(380);
                if (!_trayPendingClick) return;      // 后续来了双击/第二次单击 → 放弃
                TrayOptimize();
            });
        }

        void TrayMouseDoubleClick(object s, MouseEventArgs e)
        {
            _trayPendingClick = false;
            ShowMain();
        }

        /// <summary>托盘触发的一次内存优化（后台执行，完成后气泡报告）</summary>
        void TrayOptimize()
        {
            if (_trayOptBusy) return;
            _trayOptBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string report;
                try { report = SysTools.OptimizeMemoryNow(); }
                catch (Exception ex) { report = "优化失败: " + ex.Message; }
                // 气泡与界面更新都回主线程执行
                C.On(this, delegate
                {
                    if (_ov != null) _ov.SetMemResultText(report);
                    NotifyIcon n = _tray;
                    if (n != null)
                    {
                        try
                        {
                            n.BalloonTipTitle = "内存优化完成";
                            n.BalloonTipText = report;
                            n.ShowBalloonTip(2200);
                        }
                        catch { }
                    }
                });
                _trayOptBusy = false;
            });
        }

        void UpdatePerm()
        {
            if (OS.IsElevated)
            {
                _perm.ForeColor = C.Green;
                _perm.Text = "✓ 以管理员身份运行中";
            }
            else
            {
                _perm.ForeColor = C.Red;
                _perm.Text = "▲ 未提权 · 点此以管理员身份重启";
            }
        }

        public void Go(int idx)
        {
            if (idx < 0 || idx >= _pages.Length) return;
            if (_cur >= 0 && _cur < _pages.Length)
            {
                _pages[_cur].Visible = false;
                _nav[_cur].Active = false;
            }
            _cur = idx;
            var page = _pages[idx];
            page.Visible = true;
            page.BringToFront();
            _nav[idx].Active = true;
            UpdatePerm();
            try { page.Root.AutoScrollPosition = new Point(0, 0); } catch { }  // 切页回顶部
            page.PerformLayout();
            page.OnShown();
        }

        /// <summary>以管理员身份重启自身。
        /// 顺序很关键：先释放单实例互斥锁再启动新进程，否则新实例会提示“已在运行”并退出；
        /// 若用户取消 UAC，则重新拿回互斥锁并恢复窗口。</summary>
        public static void ElevateSelf()
        {
            // 1. 隐藏当前窗口，准备交接
            var frm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (frm != null) frm.Hide();

            // 2. 先释放单实例互斥锁（新实例要能立即拿到锁）
            Program.ReleaseInstance();

            bool started = true;
            string failMsg = null;
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                started = false;
                failMsg = ex.Message;
            }

            if (!started)
            {
                // 3. 用户取消 UAC 或启动失败：恢复互斥锁并回到前台
                if (!Program.ReacquireInstance())
                {
                    // 期间已有人启动了别的实例，本实例退出
                    Application.Exit();
                    return;
                }
                if (frm != null) frm.Show();
                MessageBox.Show("已取消提权或提权失败。\n\n" + failMsg, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 4. 新实例已开始启动（会获得互斥锁），本实例退出
            Application.Exit();
        }
    }

    // ── 导航按钮 ──
    public class NavBtn : Control
    {
        bool _active;
        bool _hover;
        public NavBtn(string text)
        {
            Text = text;
            Font = C.F(10f);
            ForeColor = C.TextMain;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }
        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = C.RoundPath(rc, 7))
            {
                using (var b = new SolidBrush(_active ? C.AccentLt : (_hover ? C.HoverBg : Color.White)))
                    g.FillPath(b, path);
            }
            if (_active)
            {
                using (var b = new SolidBrush(C.Accent))
                    g.FillRectangle(b, C.S(2), C.S(6), 3, Height - C.S(12));
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(C.S(_active ? 15 : 13), 0, Width - C.S(18), Height),
                _active ? C.Accent : (_hover ? C.TextMain : C.TextMain),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }
}
