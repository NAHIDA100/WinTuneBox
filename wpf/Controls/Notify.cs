using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WinTune.Wpf
{
    /// <summary>右下角浮层通知容器（替代 HandyControl Growl），最多同时 3 条，仅用透明度/位移动画</summary>
    public class ToastHost : StackPanel
    {
        public int MaxCount = 3;

        public ToastHost()
        {
            Orientation = Orientation.Vertical;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 18, 18);
            IsHitTestVisible = true;
            Panel.SetZIndex(this, 999);
        }

        static Brush Res(string key, Color fallback)
        {
            try
            {
                object o = Application.Current != null ? Application.Current.FindResource(key) : null;
                if (o is Brush) return (Brush)o;
            }
            catch { }
            return new SolidColorBrush(fallback);
        }

        public void Push(string kind, string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(delegate { Push(kind, text); }), DispatcherPriority.Normal);
                return;
            }

            while (Children.Count >= MaxCount) Children.RemoveAt(0);

            string glyph = "\uE946";
            string colorKey = "BrandBrush";
            string textColorKey = "PrimaryTextBrush";
            if (kind == "success") { glyph = "\uE73E"; colorKey = "OkBrush"; }
            else if (kind == "warning") { glyph = "\uE7BA"; colorKey = "WarnBrush"; }
            else if (kind == "error") { glyph = "\uE783"; colorKey = "ErrBrush"; }

            var accent = Res(colorKey, Colors.Gray);
            var fg = Res(textColorKey, Colors.Black);

            var card = new Border
            {
                Background = Res("ToastBackgroundBrush", Color.FromRgb(255, 255, 255)),
                BorderBrush = Res("ToastBorderBrush", Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 10, 11, 10),
                Margin = new Thickness(0, 8, 0, 0),
                MaxWidth = 430,
                MinWidth = 240,
                SnapsToDevicePixels = true,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ico = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)new FontFamilyConverter().ConvertFromInvariantString("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 10, 0),
            };
            Grid.SetColumn(ico, 0);
            grid.Children.Add(ico);

            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = fg,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = 18,
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            var close = new TextBlock
            {
                Text = "\uE711",
                FontFamily = (FontFamily)new FontFamilyConverter().ConvertFromInvariantString("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 11,
                Opacity = 0.5,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);

            card.Child = grid;

            var transform = new TranslateTransform(26, 0);
            card.RenderTransform = transform;
            card.Opacity = 0;

            close.MouseLeftButtonUp += delegate { Dismiss(card); };

            Children.Add(card);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

            StartLifetime(card, kind == "error" ? 6500 : 3600);
        }

        void StartLifetime(Border card, int ms)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate
            {
                timer.Stop();
                Dismiss(card);
            };
            timer.Start();
        }

        void Dismiss(Border card)
        {
            if (card == null || !Children.Contains(card)) return;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += delegate
            {
                try { Children.Remove(card); } catch { }
            };
            card.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>通知门面：优先用主窗口内浮层，窗口不可见时退回托盘气泡</summary>
    public static class Notify
    {
        public static ToastHost Host;

        /// <summary>托盘气泡兜底（由主窗口注入）</summary>
        public static Action<string, string, int> TrayFallback;

        public static void Info(string text) { Show("info", text); }
        public static void Success(string text) { Show("success", text); }
        public static void Warning(string text) { Show("warning", text); }
        public static void Error(string text) { Show("error", text); }

        public static void Show(string kind, string text)
        {
            try
            {
                if (Host != null && Host.IsVisible)
                {
                    Host.Push(kind, text);
                    return;
                }
                Action<string, string, int> fb = TrayFallback;
                if (fb != null)
                {
                    int k = kind == "error" ? ShellNotifyIcon.BalloonError
                          : kind == "warning" ? ShellNotifyIcon.BalloonWarning
                          : ShellNotifyIcon.BalloonInfo;
                    fb(DisplayName(kind), text, k);
                }
            }
            catch { }
        }

        public static void Show(string text) { Info(text); }

        static string DisplayName(string kind)
        {
            if (kind == "error") return "WinTuneBox 错误";
            if (kind == "warning") return "WinTuneBox 提示";
            if (kind == "success") return "WinTuneBox";
            return "WinTuneBox";
        }
    }
}