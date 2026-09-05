using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 加速悬浮球：低占用、闲置自动贴边隐藏 + 半透明 ═══
    // 交互：拖动(自由移动) / 单击=内存优化 / 双击=打开主界面 / 右键=菜单
    // 低占用：空闲时仅 500ms 低频轮询鼠标；动画/淡入淡出期间才启用 16ms 定时器
    public class FloatBall : Form
    {
        public Action OnOpenMain;      // 双击打开主界面
        public Action<string> OnReport;// 优化完成汇报（主界面概览页标签）

        const int BALL = 58;           // 逻辑尺寸（S 缩放）
        const int STRIP = 10;          // 贴边后露出宽度
        const float DOCK_OPACITY = 0.4f;

        enum St { Free, Docked, Moving }
        St _st = St.Free;
        double _x, _y, _op = 1.0;      // 当前位置与不透明度
        double _animToX, _animToY;     // 动画目标位置
        float _animOpTarget;
        St _animEnd;
        bool _autoHide = true;
        bool _hovering;
        int _idleTicks;
        bool _dragging;
        Point _downAt;                 // 鼠标按下点（屏幕坐标）
        Point _winAt;                  // 按下时窗口位置
        bool _optBusy, _clickPending;
        DateTime _lastLeft = DateTime.MinValue;

        readonly Timer _slow = new Timer();      // 空闲监测 500ms
        Timer _anim;                             // 动画期间临时创建
        Timer _clickW;                           // 单击判定延迟

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        static Point CursorScreen()
        {
            POINT p;
            GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }

        public FloatBall()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "加速悬浮球";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;               // 被 Region 裁剪，黑底无碍
            int s = C.S(BALL);
            Size = new Size(s, s);
            var path = new GraphicsPath();
            path.AddEllipse(0, 0, s, s);
            Region = new Region(path);
            // 初始放屏幕右下角内侧
            var wa = Screen.PrimaryScreen.WorkingArea;
            _x = wa.Right - s - C.S(24);
            _y = wa.Bottom - s - C.S(24);
            Location = new Point((int)_x, (int)_y);

            _slow.Interval = 500;
            _slow.Tick += SlowTick;
            _slow.Start();

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            MouseLeave += delegate { _hovering = false; _idleTicks = 0; };
            MouseEnter += delegate { _hovering = true; _idleTicks = 0; };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;                      // WS_EX_TOOLWINDOW：不进任务栏/Alt-Tab
                cp.ExStyle |= 0x08000000;                // WS_EX_NOACTIVATE：不抢焦点
                return cp;
            }
        }

        // ═══ 绘制：蓝紫渐变圆 + 白色内存条 ═══
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Width;
            var rc = new Rectangle(C.S(3), C.S(3), s - C.S(6), s - C.S(6));
            // 外圈白描边 + 渐变底
            using (var b = new LinearGradientBrush(rc, Color.FromArgb(96, 165, 250), Color.FromArgb(29, 78, 216), 60f))
                g.FillEllipse(b, rc);
            using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), 1.4f))
                g.DrawEllipse(p, rc);
            // 高光
            using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                g.FillEllipse(b, new Rectangle(rc.X + C.S(8), rc.Y + C.S(7), C.S(20), C.S(9)));
            // 白色“内存条”三竖条
            int bw = C.S(6), gap = C.S(5), baseY = s / 2 + C.S(9);
            int[] hs = { C.S(9), C.S(16), C.S(13) };
            using (var b = new SolidBrush(Color.White))
            {
                for (int i = 0; i < 3; i++)
                {
                    int x = s / 2 - (bw * 3 + gap * 2) / 2 + i * (bw + gap);
                    var pr = new Rectangle(x, baseY - hs[i], bw, hs[i]);
                    using (var p2 = new GraphicsPath())
                    {
                        int r2 = bw / 2;
                        p2.AddArc(pr.X, pr.Y, r2 * 2, r2 * 2, 180, 90);
                        p2.AddArc(pr.Right - r2 * 2, pr.Y, r2 * 2, r2 * 2, 270, 90);
                        p2.AddArc(pr.Right - r2 * 2, pr.Bottom - r2 * 2, r2 * 2, r2 * 2, 0, 90);
                        p2.AddArc(pr.X, pr.Bottom - r2 * 2, r2 * 2, r2 * 2, 90, 90);
                        p2.CloseFigure();
                        g.FillPath(b, p2);
                    }
                }
            }
            // 状态点：正在优化 → 绿色小圆点右上
            if (_optBusy)
                using (var b = new SolidBrush(Color.FromArgb(240, 80, 220, 90)))
                    g.FillEllipse(b, s - C.S(16), C.S(5), C.S(8), C.S(8));
        }

        // ═══ 鼠标：拖动 / 单击 / 双击 ═══
        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _downAt = CursorScreen();
            _winAt = Location;
            _dragging = false;
            _clickPending = false;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _downAt == Point.Empty) return;
            Point mp = CursorScreen();
            if (!_dragging && Dist(mp, _downAt) > C.S(4))
            {
                _dragging = true;
                if (_st != St.Free) { _st = St.Free; Fade(1f); }
                if (_clickW != null) _clickW.Stop();
            }
            if (_dragging)
            {
                int nx = _winAt.X + (mp.X - _downAt.X);
                int ny = _winAt.Y + (mp.Y - _downAt.Y);
                Location = new Point(nx, ny);
                _x = nx; _y = ny;
                _idleTicks = 0;
            }
        }

        void OnUp(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            bool wasDrag = _dragging;
            _downAt = Point.Empty;
            _dragging = false;
            if (wasDrag)
            {
                ClampInside();
                _idleTicks = 0;      // 拖动完重新计时贴边
                return;
            }
            // 单击/双击判定
            var now = DateTime.Now;
            if ((now - _lastLeft).TotalMilliseconds < 380)
            {
                _lastLeft = now;
                _clickPending = false;
                if (OnOpenMain != null) OnOpenMain();
                return;
            }
            _lastLeft = now;
            _clickPending = true;
            if (_clickW == null)
            {
                _clickW = new Timer { Interval = 380 };
                _clickW.Tick += delegate
                {
                    _clickW.Stop();
                    if (_clickPending)
                    {
                        _clickPending = false;
                        StartOptimize();
                    }
                };
            }
            _clickW.Start();
        }

        static int Dist(Point a, Point b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return (int)Math.Sqrt(dx * (double)dx + dy * dy);
        }

        void ClampInside()
        {
            var wa = Screen.FromPoint(Location).WorkingArea;
            int x = Math.Min(Math.Max(Location.X, wa.X), Math.Max(wa.X, wa.Right - Width));
            int y = Math.Min(Math.Max(Location.Y, wa.Y), Math.Max(wa.Y, wa.Bottom - Height));
            Location = new Point(x, y);
            _x = x; _y = y;
        }

        // ═══ 低占用空闲监测（500ms 一次）═══
        void SlowTick(object s, EventArgs e)
        {
            if (_dragging || _st == St.Moving) return;
            Point mp = CursorScreen();
            Rectangle near = new Rectangle(Location.X - C.S(36), Location.Y - C.S(36),
                Width + C.S(72), Height + C.S(72));
            bool nearby = near.Contains(mp);

            if (nearby || _hovering)
            {
                _idleTicks = 0;
                if (_st == St.Docked)
                {
                    Undock();                                // 鼠标靠近 → 滑出全显
                    return;
                }
                if (_op < 1f) Fade(1f);
                return;
            }

            _idleTicks++;
            if (_autoHide && _st == St.Free && _idleTicks >= 5)   // 约 2.5 秒没人碰
                DockToNearestEdge();
        }

        // ═══ 贴边 / 滑出（带透明度动画，动画期短时高帧率，空闲零开销）═══
        void DockToNearestEdge()
        {
            if (!IsHandleCreated) return;
            var wa = Screen.FromPoint(Location).WorkingArea;
            double dL = Location.X - wa.X, dR = wa.Right - (Location.X + Width);
            double dT = Location.Y - wa.Y, dB = wa.Bottom - (Location.Y + Height);
            double min = Math.Min(Math.Min(dL, dR), Math.Min(dT, dB));
            double tx = _x, ty = _y;
            int strip = C.S(STRIP);
            if (min == dL) tx = wa.X - Width + strip;
            else if (min == dR) tx = wa.Right - strip;
            else if (min == dT) ty = wa.Y - Height + strip;
            else ty = wa.Bottom - strip;
            StartAnim(tx, ty, DOCK_OPACITY, St.Docked);
        }

        void Undock()
        {
            if (_st != St.Docked) return;
            var wa = Screen.FromPoint(Location).WorkingArea;
            // 按贴边侧滑回屏幕内侧（留 8px 边距）
            double tx = _x, ty = _y;
            int m = C.S(8);
            if (_x < wa.X) tx = wa.X + m;
            else if (_x + Width > wa.Right - 1) tx = wa.Right - Width - m;
            else if (_y < wa.Y) ty = wa.Y + m;
            else if (_y + Height > wa.Bottom - 1) ty = wa.Bottom - Height - m;
            StartAnim(tx, ty, 1f, St.Free);
        }

        void Fade(float target)
        {
            _op = target;
            try { Opacity = _op; } catch { }
        }

        void StartAnim(double toX, double toY, float opTarget, St end)
        {
            if (_anim == null)
            {
                _anim = new Timer { Interval = 16 };
                _anim.Tick += AnimTick;
            }
            _animToX = toX;
            _animToY = toY;
            _animOpTarget = opTarget;
            _animEnd = end;
            _st = St.Moving;
            _anim.Start();
        }

        void AnimTick(object s, EventArgs e)
        {
            double dx = _animToX - _x, dy = _animToY - _y;
            _x += dx * 0.22;
            _y += dy * 0.22;
            _op += (_animOpTarget - _op) * 0.22;
            Location = new Point((int)_x, (int)_y);
            try { Opacity = Math.Max(0.15, Math.Min(1.0, _op)); } catch { }
            if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2)
            {
                _anim.Stop();
                _x = _animToX; _y = _animToY;
                Location = new Point((int)_x, (int)_y);
                try { Opacity = _animOpTarget; } catch { }
                _st = _animEnd;
                if (_st == St.Free) _idleTicks = 0;
            }
        }

        // ═══ 内存优化（PCL 风格）═══
        public void StartOptimize()
        {
            if (_optBusy) return;
            _optBusy = true;
            Invalidate();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string report;
                try { report = SysTools.OptimizeMemoryNow(); }
                catch (Exception ex) { report = "优化失败: " + ex.Message; }
                C.On(this, delegate
                {
                    _optBusy = false;
                    Invalidate();
                    if (OnReport != null) OnReport(report);
                    // 球上方气泡式提示
                    try
                    {
                        var tip = new ToolTip();
                        tip.Show(report, this, Width / 2, -C.S(8), 2600);
                    }
                    catch { }
                });
            });
        }

        // ═══ 右键菜单 ═══
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var m = new ContextMenuStrip();
                m.Font = C.F(9f);
                m.Items.Add("立即内存优化", null, delegate { StartOptimize(); });
                m.Items.Add("打开工具箱", null, delegate { if (OnOpenMain != null) OnOpenMain(); });
                var chk = new ToolStripMenuItem("闲置自动贴边隐藏");
                chk.Checked = _autoHide;
                chk.CheckOnClick = true;
                chk.Click += delegate { _autoHide = chk.Checked; };
                m.Items.Add(chk);
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("关闭悬浮球", null, delegate
                {
                    CloseBall();
                    if (OnBallClosed != null) OnBallClosed();
                });
                m.Show(this, new Point(Width / 2, Height / 2));
                return;
            }
            base.OnMouseUp(e);
        }

        public Action OnBallClosed;
        public void CloseBall()
        {
            try { _slow.Stop(); } catch { }
            try { if (_anim != null) _anim.Stop(); } catch { }
            try { if (_clickW != null) _clickW.Stop(); } catch { }
            Close();
            Dispose();
        }

        public bool AutoHideEnabled { get { return _autoHide; } }

        /// <summary>屏幕环境变化后把球拉回当前屏幕（贴边的滑出，自由的收回工作区）</summary>
        public void SnapInside()
        {
            if (_dragging || _st == St.Moving) return;
            try
            {
                if (_st == St.Docked) Undock();
                else ClampInside();
            }
            catch { }
        }
    }
}
