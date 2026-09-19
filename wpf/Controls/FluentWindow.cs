using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WinTune.Wpf
{
    /// <summary>
    /// Fluent 窗口基类：无边框 + WindowChrome 自绘标题栏（保留缩放/贴靠/双击最大化）+
    /// DWM Mica 背景与圆角；不支持的系统自动降级为纯色背景，不引入亚克力/模糊以保持低占用。
    /// </summary>
    public class FluentWindow : Window
    {
        bool _hooked;
        WindowChrome _chrome;

        public FluentWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            _chrome = new WindowChrome();
            WindowChrome chrome = _chrome;
            chrome.CaptionHeight = 44;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            SourceInitialized += OnSourceInitialized;
            StateChanged += OnStateChanged;
            Loaded += OnLoadedOnce;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        }

        /// <summary>标题栏高度（与 WindowChrome.CaptionHeight 对应，供派生类布局使用）</summary>
        public double CaptionAreaHeight { get { return 44; } }

        /// <summary>是否允许 Mica 背景（用户设置 + 系统能力）</summary>
        public bool MicaAllowed
        {
            get { return MicaAvailable && Settings.GetBool(Settings.KeyMica, true); }
        }

        /// <summary>当前系统是否具备 Mica 能力</summary>
        public static bool MicaAvailable { get { return Dwm.MicaAvailable; } }

        /// <summary>实际生效的后台类型（Dwm.BackdropNone 表示自绘纯色）</summary>
        public int AppliedBackdrop { get; private set; }

        void OnLoadedOnce(object sender, RoutedEventArgs e)
        {
            ApplyAppearance();
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            ApplyAppearance();
        }

        void OnStateChanged(object sender, EventArgs e)
        {
            // 最大化/还原后 DWM 可能丢失背景，重新应用
            ApplyAppearance();
            OnAppearanceChanged();
        }

        void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            ApplyAppearance();
        }

        /// <summary>重新应用 DWM 外观与窗口底色</summary>
        public void ApplyAppearance()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (!_hooked)
                {
                    HwndSource src = HwndSource.FromHwnd(hwnd);
                    if (src != null) { src.AddHook(WndHook); _hooked = true; }
                }

                bool mica = MicaAllowed;
                SetGlassFrame(mica);
                AppliedBackdrop = Dwm.Apply(hwnd, ThemeManager.IsDark, mica);

                if (AppliedBackdrop == Dwm.BackdropMica)
                {
                    Background = Brushes.Transparent;   // 由 DWM 绘制 Mica
                }
                else
                {
                    object b = TryFind("WindowBackgroundBrush");
                    Background = (b as Brush) ?? new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF9));
                }
                OnAppearanceChanged();
            }
            catch (Exception ex) { Logger.Log("Window", "应用外观失败: " + ex.Message); }
        }

        IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 系统主题（浅色/深色）变化时刷新标题栏与 Mica
            if (msg == 0x001A /*WM_SETTINGCHANGE*/ || msg == 0x031A /*WM_THEMECHANGED*/)
            {
                ThemeManager.OnSystemSettingChanged();
            }
            return IntPtr.Zero;
        }

        internal object TryFind(string key)
        {
            try { return Application.Current != null ? Application.Current.TryFindResource(key) : null; }
            catch { return null; }
        }

        /// <summary>主题/外观变化回调，派生类可重写以刷新自绘元素</summary>
        protected virtual void OnAppearanceChanged() { }

        /// <summary>
        /// 切换 DWM 玻璃帧范围。Mica 需要整窗玻璃帧（DwmExtendFrameIntoClientArea(-1)）才能生效：
        /// 无边框 WPF 窗口在非整窗玻璃时会把透明像素合成成不透明黑，浅色主题下表现为黑色标题条带。
        /// </summary>
        void SetGlassFrame(bool whole)
        {
            if (_chrome == null) return;
            Thickness want = whole ? new Thickness(-1) : new Thickness(0);
            if (_chrome.GlassFrameThickness == want) return;
            _chrome.GlassFrameThickness = want;
            WindowChrome.SetWindowChrome(this, _chrome);
        }

        public void MinimizeWindow() { WindowState = WindowState.Minimized; }

        public void ToggleMaximizeWindow()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        public string CaptionGlyph(bool dark)
        {
            return "";   // 预留给派生类
        }
    }
}