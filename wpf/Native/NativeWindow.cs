using System;
using System.Runtime.InteropServices;

namespace WinTune.Wpf
{
    /// <summary>
    /// 原生窗口可见性查询与强制隐藏。
    /// 用于兜住 WPF 与 Win32 状态可能不一致的场景（例如启动早期 Hide() 被 Show() 收尾覆盖，
    /// 表现为「WPF 认为窗口已隐藏，Win32 侧窗口仍然可见」）。
    /// </summary>
    public static class NativeWindow
    {
        public const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>原生窗口当前是否可见（不经 WPF 的 IsVisible 缓存）</summary>
        public static bool Visible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try { return IsWindowVisible(hwnd); } catch { return false; }
        }

        /// <summary>原生窗口仍可见时强制隐藏，返回是否真的执行了隐藏</summary>
        public static bool ForceHide(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                if (!IsWindowVisible(hwnd)) return false;
                ShowWindow(hwnd, SW_HIDE);
                return true;
            }
            catch { return false; }
        }
    }
}