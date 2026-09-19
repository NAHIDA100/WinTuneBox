using System;
using System.Runtime.InteropServices;

namespace WinTune.Wpf
{
    /// <summary>DWM 互操作：Mica 背景 / 深色标题栏 / 圆角，按系统版本分级降级（Win10 自动退回纯色）</summary>
    public static class Dwm
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmIsCompositionEnabled(out bool enabled);

        // DWMWINDOWATTRIBUTE
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // DWM_WINDOW_CORNER_PREFERENCE
        public const int CornerDefault = 0;
        public const int CornerDoNotRound = 1;
        public const int CornerRound = 2;

        // DWM_SYSTEMBACKDROP_TYPE
        public const int BackdropAuto = 0;
        public const int BackdropNone = 1;
        public const int BackdropMica = 2;
        public const int BackdropAcrylic = 3;
        public const int BackdropTabbed = 4;

        public static readonly bool SupportsMica = OS.Build >= 22621;      // Win11 22H2+
        public static readonly bool SupportsBackdrop = OS.Build >= 22000;  // Win11 21H2
        public static readonly bool SupportsCorner = OS.Build >= 22000;
        public static readonly bool SupportsDarkTitleBar = OS.Build >= 17763;

        /// <summary>Mica 是否可用（系统版本 + 合成器开启）</summary>
        public static bool MicaAvailable
        {
            get { return SupportsMica && CompositionEnabled; }
        }

        public static bool CompositionEnabled
        {
            get
            {
                try { bool e; DwmIsCompositionEnabled(out e); return e; }
                catch { return false; }
            }
        }

        public static bool SetImmersiveDarkMode(IntPtr hwnd, bool on)
        {
            if (hwnd == IntPtr.Zero || !SupportsDarkTitleBar) return false;
            int v = on ? 1 : 0;
            try { return DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4) == 0; }
            catch { return false; }
        }

        public static bool SetCornerPreference(IntPtr hwnd, int pref)
        {
            if (hwnd == IntPtr.Zero || !SupportsCorner) return false;
            int v = pref;
            try { return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4) == 0; }
            catch { return false; }
        }

        public static bool SetBackdrop(IntPtr hwnd, int type)
        {
            if (hwnd == IntPtr.Zero || !SupportsBackdrop) return false;
            int v = type;
            try { return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, 4) == 0; }
            catch { return false; }
        }

        /// <summary>统一应用窗口外观；返回实际生效的后台类型（BackdropNone 表示需自绘背景）</summary>
        public static int Apply(IntPtr hwnd, bool dark, bool mica)
        {
            if (hwnd == IntPtr.Zero) return BackdropNone;
            SetImmersiveDarkMode(hwnd, dark);
            SetCornerPreference(hwnd, CornerRound);
            int want = (mica && MicaAvailable) ? BackdropMica : BackdropNone;
            bool ok = SetBackdrop(hwnd, want);
            if (!ok && want == BackdropMica)
            {
                // 老版本 DWM 不支持 backdrop 属性时退回纯色
                SetBackdrop(hwnd, BackdropAuto);
                return BackdropNone;
            }
            return want;
        }

        /// <summary>系统是否处于“跟随系统”暗色模式</summary>
        public static bool SystemUsesDarkMode()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                if (v == null) return false;
                return Convert.ToInt32(v) == 0;
            }
            catch { return false; }
        }

        /// <summary>系统强调色（AccentColor 为 ABGR）</summary>
        public static System.Windows.Media.Color? SystemAccentColor()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v == null) return null;
                int abgr = Convert.ToInt32(v);
                return System.Windows.Media.Color.FromRgb(
                    (byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
            }
            catch { return null; }
        }
    }
}