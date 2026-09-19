using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WinTune.Wpf
{
    public partial class FloatBall : Window
    {
        public Action RequestOpenMain;
        public Action RequestExitBall;

        bool _drag, _moved, _thisDownDouble;
        DateTime _lastDown = DateTime.MinValue;
        Point _down;
        double _startLeft, _startTop;
        DispatcherTimer _timer;
        int _tick;

        public FloatBall()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            MouseRightButtonUp += OnRightUp;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Bottom - Height - 120;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += OnTick;
            _timer.Start();
            UpdateMem();
        }

        void OnTick(object sender, EventArgs e)
        {
            _tick++;
            CheckFullscreen();
            if (_tick % 2 == 0) UpdateMem();
        }

        void UpdateMem()
        {
            try
            {
                double pct = OS.RamUsagePercent;
                MemText.Text = ((int)pct) + "%";
            }
            catch { }
        }

        void Ball_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDown).TotalMilliseconds < 320)
            {
                _thisDownDouble = true;
                if (RequestOpenMain != null) RequestOpenMain();
            }
            else _thisDownDouble = false;
            _lastDown = now;

            _drag = true; _moved = false;
            _down = PointToScreen(e.GetPosition(this));
            _startLeft = Left; _startTop = Top;
            Ball.CaptureMouse();
        }

        void Ball_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_drag) return;
            Point p = PointToScreen(e.GetPosition(this));
            double dx = p.X - _down.X, dy = p.Y - _down.Y;
            if (!_moved && Math.Abs(dx) + Math.Abs(dy) > 5) _moved = true;
            if (_moved) { Left = _startLeft + dx; Top = _startTop + dy; }
        }

        void Ball_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _drag = false;
            try { Ball.ReleaseMouseCapture(); } catch { }
            if (_moved) { Snap(); return; }
            if (_thisDownDouble) { _thisDownDouble = false; return; }
            // 单击：延迟一点以排除双击
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            timer.Tick += delegate
            {
                timer.Stop();
                DoOptimize();
            };
            timer.Start();
        }

        void Ball_MouseEnter(object sender, MouseEventArgs e) { Ball.Opacity = 1.0; }
        void Ball_MouseLeave(object sender, MouseEventArgs e) { if (!_drag) Ball.Opacity = 0.82; }

        void DoOptimize()
        {
            MemText.Text = "..";
            Task.Run(delegate
            {
                string report;
                try { report = SystemTools.OptimizeMemoryNow(); }
                catch (Exception ex) { report = "优化失败: " + ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    UpdateMem();
                    ShowToast(report);
                    Logger.Log("FloatBall", report.Replace('\n', ' '));
                });
            });
        }

        string _toast;
        void ShowToast(string text)
        {
            _toast = text;
            Ball.Opacity = 1.0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            t.Tick += delegate { t.Stop(); Ball.Opacity = 0.82; };
            t.Start();
        }

        void Snap()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var rc = WorkAreaFor(hwnd);
                double center = Left + Width / 2.0;
                double target = center - (rc.Left + rc.Width / 2) < 0 ? rc.Left + 12 : rc.Right - Width - 12;
                double top = Top;
                if (top < rc.Top + 8) top = rc.Top + 8;
                if (top > rc.Bottom - Height - 8) top = rc.Bottom - Height - 8;
                var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() };
                var b = new DoubleAnimation(top, TimeSpan.FromMilliseconds(180));
                BeginAnimation(LeftProperty, a);
                BeginAnimation(TopProperty, b);
            }
            catch { }
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            var miOpen = new System.Windows.Controls.MenuItem { Header = "打开主界面" };
            miOpen.Click += delegate { if (RequestOpenMain != null) RequestOpenMain(); };
            var miOpt = new System.Windows.Controls.MenuItem { Header = "立即优化内存" };
            miOpt.Click += delegate { DoOptimize(); };
            var miExit = new System.Windows.Controls.MenuItem { Header = "关闭悬浮球" };
            miExit.Click += delegate { if (RequestExitBall != null) RequestExitBall(); };
            menu.Items.Add(miOpen); menu.Items.Add(miOpt); menu.Items.Add(new System.Windows.Controls.Separator()); menu.Items.Add(miExit);
            menu.PlacementTarget = Ball;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        // ── 全屏检测 ──
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        struct Box { public double Left, Top, Right, Bottom, Width; }

        Box WorkAreaFor(IntPtr hwnd)
        {
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            IntPtr mon = MonitorFromWindow(hwnd, 2);
            if (GetMonitorInfo(mon, ref mi))
                return new Box { Left = mi.rcWork.Left, Top = mi.rcWork.Top, Right = mi.rcWork.Right, Bottom = mi.rcWork.Bottom, Width = mi.rcWork.Right - mi.rcWork.Left };
            var wa = SystemParameters.WorkArea;
            return new Box { Left = wa.Left, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom, Width = wa.Width };
        }

        void CheckFullscreen()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return;
                var myH = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (fg == myH) return;
                var cn = new StringBuilder(256);
                GetClassName(fg, cn, cn.Capacity);
                string cls = cn.ToString();
                if (cls == "Progman" || cls.StartsWith("Shell_") || cls == "WorkerW") { RestoreBall(); return; }
                RECT r;
                if (!GetWindowRect(fg, out r)) return;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var wa = WorkAreaFor(hwnd);
                bool covers = r.Left <= wa.Left && r.Top <= wa.Top &&
                              r.Right >= wa.Right && r.Bottom >= wa.Bottom;
                if (covers) HideSafely(); else RestoreBall();
            }
            catch { }
        }

        bool _hidden;
        void HideSafely()
        {
            if (!_hidden) { _hidden = true; Opacity = 0; IsHitTestVisible = false; }
        }
        void RestoreBall()
        {
            if (_hidden) { _hidden = false; Opacity = 0.82; IsHitTestVisible = true; }
        }
    }
}
