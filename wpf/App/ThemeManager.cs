using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinTune.Wpf
{
    /// <summary>
    /// 主题管理：浅色/深色/跟随系统 + 强调色（含跟随系统），切换即时生效并持久化到 settings.json。
    /// 通过替换合并字典实现，控件样式统一使用 DynamicResource 以便实时刷新。
    /// </summary>
    public static class ThemeManager
    {
        static readonly Uri LightUri = new Uri("Themes/Palette.Light.xaml", UriKind.Relative);
        static readonly Uri DarkUri = new Uri("Themes/Palette.Dark.xaml", UriKind.Relative);

        public static event Action Changed;

        public static bool IsDark { get; private set; }
        public static int AccentIndex { get; private set; }

        public static readonly string[] AccentNames = { "Fluent 蓝", "靛紫", "青蓝", "翠绿", "琥珀", "玫红", "跟随系统" };
        static readonly string[] AccentHex = { "#2D7CF6", "#7C5CFF", "#0E8EA8", "#1F9D55", "#D98200", "#D6336C" };

        static int _paletteIndex = -1;
        static ResourceDictionary _accentDict;

        /// <summary>主题模式：light / dark / system</summary>
        public static string Mode
        {
            get
            {
                string m = Settings.Get(Settings.KeyTheme, "system");
                return (m == "light" || m == "dark") ? m : "system";
            }
        }

        public static void Initialize()
        {
            try
            {
                string mode = Mode;
                bool dark = mode == "dark" || (mode == "system" && Dwm.SystemUsesDarkMode());
                IsDark = dark;
                SwapPalette(dark);
                AccentIndex = Settings.GetInt(Settings.KeyAccent, 0);
                ApplyAccent(AccentIndex);
                Logger.Log("Theme", "初始化完成 mode=" + mode + " dark=" + dark + " accent=" + AccentIndex);
            }
            catch (Exception ex) { Logger.Log("Theme", "初始化失败: " + ex.Message); }
        }

        /// <summary>设置主题模式（light/dark/system）并持久化</summary>
        public static void SetMode(string mode)
        {
            Settings.Set(Settings.KeyTheme, mode);
            bool dark = mode == "dark" || (mode == "system" && Dwm.SystemUsesDarkMode());
            SwapPalette(dark);
            IsDark = dark;
            ApplyAccent(AccentIndex);
            Settings.Set(Settings.KeyTheme, mode);
            Refresh();
        }

        /// <summary>浅色/深色快速互切（写入固定模式）</summary>
        public static void Toggle()
        {
            SetMode(IsDark ? "light" : "dark");
        }

        public static void SetAccent(int index)
        {
            AccentIndex = index;
            Settings.SetInt(Settings.KeyAccent, index);
            ApplyAccent(index);
            Refresh();
        }

        /// <summary>系统主题变化回调（跟随系统模式时重新评估）</summary>
        public static void OnSystemSettingChanged()
        {
            if (Mode != "system") return;
            bool dark = Dwm.SystemUsesDarkMode();
            if (dark == IsDark) return;
            IsDark = dark;
            SwapPalette(dark);
            ApplyAccent(AccentIndex);
            Refresh();
        }

        static ResourceDictionary _paletteDict;

        /// <summary>
        /// 就地更新调色板：始终复用同一个 ResourceDictionary 实例，
        /// 只替换键值，避免替换 MergedDictionaries 元素导致动态资源瞬时解析失败
        /// （表现为 ResourceReferenceKeyNotFoundException / 未解析的 NamedObject 值）。
        /// </summary>
        static void SwapPalette(bool dark)
        {
            var loaded = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
            if (_paletteDict == null)
            {
                _paletteDict = new ResourceDictionary();
                foreach (object k in loaded.Keys) _paletteDict[k] = loaded[k];
                Application.Current.Resources.MergedDictionaries.Add(_paletteDict);
                _paletteIndex = Application.Current.Resources.MergedDictionaries.Count - 1;
                return;
            }
            foreach (object k in loaded.Keys) _paletteDict[k] = loaded[k];
        }

        static void ApplyAccent(int index)
        {
            bool isDark;
            Color baseColor = ResolveAccent(index, out isDark);
            var d = new ResourceDictionary();
            Color hover = Shift(baseColor, isDark ? 22 : -18);
            Color pressed = Shift(baseColor, isDark ? -26 : -34);
            byte lightAlpha = (byte)(isDark ? 0x33 : 0x1F);
            byte selectionAlpha = (byte)(isDark ? 0x38 : 0x24);

            d["BrandColor"] = baseColor;
            d["BrandBrush"] = Freeze(new SolidColorBrush(baseColor));
            d["BrandHoverBrush"] = Freeze(new SolidColorBrush(hover));
            d["BrandPressedBrush"] = Freeze(new SolidColorBrush(pressed));
            d["BrandDarkBrush"] = Freeze(new SolidColorBrush(pressed));
            d["BrandLightBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(lightAlpha, baseColor.R, baseColor.G, baseColor.B)));
            d["FocusBrush"] = Freeze(new SolidColorBrush(baseColor));
            d["NavItemActiveBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(lightAlpha, baseColor.R, baseColor.G, baseColor.B)));
            d["NavItemActiveTextBrush"] = Freeze(new SolidColorBrush(isDark ? Shift(baseColor, 60) : pressed));
            d["SelectionBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(selectionAlpha, baseColor.R, baseColor.G, baseColor.B)));
            d["InfoBrush"] = Freeze(new SolidColorBrush(baseColor));
            d["InfoLightBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(lightAlpha, baseColor.R, baseColor.G, baseColor.B)));

            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            grad.GradientStops.Add(new GradientStop(Shift(baseColor, isDark ? 30 : 22), 0));
            grad.GradientStops.Add(new GradientStop(pressed, 1));
            d["BrandGradient"] = Freeze(grad);

            var dicts = Application.Current.Resources.MergedDictionaries;
            if (_accentDict != null) dicts.Remove(_accentDict);
            dicts.Add(d);
            _accentDict = d;
        }

        static Color ResolveAccent(int index, out bool isDark)
        {
            isDark = IsDark;
            if (index == AccentNames.Length - 1)
            {
                Color? sys = Dwm.SystemAccentColor();
                if (sys.HasValue && (sys.Value.R + sys.Value.G + sys.Value.B) > 90) return sys.Value;
                index = 0;
            }
            if (index < 0 || index >= AccentHex.Length) index = 0;
            return (Color)ColorConverter.ConvertFromString(AccentHex[index]);
        }

        static Color Shift(Color c, int delta)
        {
            return Color.FromRgb(Clamp(c.R + delta), Clamp(c.G + delta), Clamp(c.B + delta));
        }

        static byte Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        static T Freeze<T>(T brush) where T : Freezable
        {
            try { if (brush.CanFreeze) brush.Freeze(); } catch { }
            return brush;
        }

        public static void Refresh()
        {
            try
            {
                if (Application.Current == null) return;
                foreach (Window w in Application.Current.Windows)
                {
                    FluentWindow fw = w as FluentWindow;
                    if (fw != null) fw.ApplyAppearance();
                }
                Action h = Changed;
                if (h != null) h();
            }
            catch (Exception ex) { Logger.Log("Theme", "刷新失败: " + ex.Message); }
        }
    }
}