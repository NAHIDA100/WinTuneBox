using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WinTune.Wpf
{
    /// <summary>
    /// Shell_NotifyIcon 托盘图标（自写实现，不引用 WinForms）：隐藏消息窗口 + 右键菜单 + 气泡通知，
    /// 监听 TaskbarCreated 以便 Explorer 重启后自动恢复图标。
    /// </summary>
    public sealed class ShellNotifyIcon : IDisposable
    {
        const int WM_TRAY = 0x0400 + 1024;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_LBUTTONDBLCLK = 0x0203;
        const int WM_RBUTTONUP = 0x0205;
        const int WM_CONTEXTMENU = 0x007B;
        const int WM_NULL = 0x0000;
        const int NIN_SELECT = 0x0400;
        const int NIN_BALLOONUSERCLICK = 0x0405;

        const int NIM_ADD = 0x00;
        const int NIM_MODIFY = 0x01;
        const int NIM_DELETE = 0x02;

        const int NIF_MESSAGE = 0x01;
        const int NIF_ICON = 0x02;
        const int NIF_TIP = 0x04;
        const int NIF_INFO = 0x10;

        public const int BalloonInfo = 0x01;
        public const int BalloonWarning = 0x02;
        public const int BalloonError = 0x03;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessageW(string name);

        readonly HwndSource _source;
        readonly int _taskbarCreated;
        Icon _icon;
        string _tip = "";
        bool _added;
        ContextMenu _menu;
        bool _disposed;

        public Action LeftClick;
        public Action DoubleClick;

        public ShellNotifyIcon(string tip)
        {
            _tip = tip ?? "";
            var p = new HwndSourceParameters("WinTuneBoxTraySink");
            p.WindowStyle = 0;
            p.ExtendedWindowStyle = 0;
            p.Width = 0;
            p.Height = 0;
            p.PositionX = -32000;
            p.PositionY = -32000;
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        }

        /// <summary>右键菜单（由调用方提供并负责刷新勾选状态）</summary>
        public ContextMenu Menu
        {
            get { return _menu; }
            set { _menu = value; }
        }

        public bool Visible
        {
            get { return _added; }
            set { if (value) Add(); else Remove(); }
        }

        public void SetIcon(Icon icon)
        {
            _icon = icon;
            if (_added) Modify(NIF_ICON | NIF_TIP | NIF_MESSAGE);
        }

        public void SetTip(string tip)
        {
            _tip = tip ?? "";
            if (_added) Modify(NIF_ICON | NIF_TIP | NIF_MESSAGE);
        }

        public void Add()
        {
            if (_disposed) return;
            try
            {
                var d = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
                _added = Shell_NotifyIconW(NIM_ADD, ref d);
                if (!_added) Logger.Log("Tray", "Shell_NotifyIcon(ADD) 失败: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Logger.Log("Tray", "添加托盘图标异常: " + ex.Message); }
        }

        public void Remove()
        {
            try
            {
                if (!_added) return;
                var d = NewData(0);
                Shell_NotifyIconW(NIM_DELETE, ref d);
                _added = false;
            }
            catch { }
        }

        void Modify(int flags)
        {
            try
            {
                var d = NewData(flags);
                Shell_NotifyIconW(NIM_MODIFY, ref d);
            }
            catch { }
        }

        public void ShowBalloon(string title, string text, int kind)
        {
            try
            {
                if (!_added) Add();
                var d = NewData(NIF_INFO | NIF_ICON | NIF_TIP | NIF_MESSAGE);
                d.szInfo = Truncate(text, 255);
                d.szInfoTitle = Truncate(title, 63);
                d.dwInfoFlags = kind;
                d.uTimeoutOrVersion = 5000;
                Shell_NotifyIconW(NIM_MODIFY, ref d);
            }
            catch (Exception ex) { Logger.Log("Tray", "气泡通知失败: " + ex.Message); }
        }

        NOTIFYICONDATA NewData(int flags)
        {
            var d = new NOTIFYICONDATA();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            d.hWnd = _source.Handle;
            d.uID = 1;
            d.uFlags = flags;
            d.uCallbackMessage = WM_TRAY;
            d.hIcon = (_icon != null) ? _icon.Handle : IntPtr.Zero;
            d.szTip = Truncate(_tip, 127);
            d.szInfo = "";
            d.szInfoTitle = "";
            return d;
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (_taskbarCreated != 0 && msg == _taskbarCreated)
                {
                    _added = false;
                    Add();
                    return IntPtr.Zero;
                }
                if (msg == WM_TRAY)
                {
                    int ev = (int)(lParam.ToInt64() & 0xFFFF);
                    if (ev == WM_LBUTTONUP || ev == NIN_SELECT)
                    {
                        if (LeftClick != null) LeftClick();
                    }
                    else if (ev == WM_LBUTTONDBLCLK)
                    {
                        if (DoubleClick != null) DoubleClick();
                    }
                    else if (ev == WM_RBUTTONUP || ev == WM_CONTEXTMENU)
                    {
                        ShowMenu();
                    }
                    else if (ev == NIN_BALLOONUSERCLICK)
                    {
                        if (DoubleClick != null) DoubleClick();
                    }
                }
            }
            catch (Exception ex) { Logger.Log("Tray", "回调异常: " + ex.Message); }
            return IntPtr.Zero;
        }

        void ShowMenu()
        {
            try
            {
                if (_menu == null) return;
                SetForegroundWindow(_source.Handle);
                _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _menu.HorizontalOffset = 0;
                _menu.VerticalOffset = 0;
                _menu.IsOpen = true;
                PostMessage(_source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Logger.Log("Tray", "打开菜单失败: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Remove(); } catch { }
            try { _source.RemoveHook(WndProc); } catch { }
            try { _source.Dispose(); } catch { }
            try { if (_icon != null) _icon.Dispose(); } catch { }
            _icon = null;
        }
    }
}