using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 主窗体：左侧导航 + 内容区 ═══
    public class FrmMain : Form
    {
        public const string Ver = "1.0.0";
        public const string AppName = "Windows 优化工具箱";

        const int SIDEBAR_W = 186;

        readonly UPage[] _pages;
        readonly NavBtn[] _nav;
        readonly Panel _host = new Panel();
        readonly Label _perm = new Label();
        int _cur = -1;

        public FrmMain()
        {
            Text = AppName + "  v" + Ver;
            BackColor = C.PageBg;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(C.S(1000), C.S(650));
            Size = new Size(C.S(1180), C.S(760));
            StartPosition = FormStartPosition.CenterScreen;
            Font = C.F(9f);
            DoubleBuffered = true;

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

            // 标题区
            var logo = new Label
            {
                Text = AppName,
                Font = C.F(14f, true),
                ForeColor = C.TextMain,
                Dock = DockStyle.Top,
                Height = C.S(34),
                Padding = new Padding(C.S(22), C.S(20), 0, 0),
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
            var ov = new PgOverview();
            var pages = new UPage[] {
                ov,
                new PgOneKey(),
                new PgClean(),
                new PgStartup(),
                new PgServices(),
                new PgNet(),
                new PgSoftware(),
                new PgTools(),
            };
            _pages = pages;
            ov.GotoPage = Go;
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
                if (!OS.IsElevated)
                    MessageBox.Show(this,
                        "当前未以管理员身份运行。\n\n绝大多数优化/清理功能（服务、注册表 HKLM、hosts、系统修复）需要管理员权限。\n可在左侧底部“以管理员身份运行”处一键提权重启，普通浏览不受影响。",
                        "权限提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
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
            _pages[idx].Visible = true;
            _pages[idx].BringToFront();
            _nav[idx].Active = true;
            UpdatePerm();
            _pages[idx].OnShown();
        }

        /// <summary>以管理员身份重启自身</summary>
        public static void ElevateSelf()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                };
                Process.Start(psi);
                Application.Exit();
            }
            catch
            {
                MessageBox.Show("已取消提权或提权失败。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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
