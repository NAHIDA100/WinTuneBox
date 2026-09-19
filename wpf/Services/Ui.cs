using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinTune.Wpf
{
    /// <summary>
    /// Fluent 构件工厂：页面统一用它拼装 UI。所有颜色走主题资源（SetResourceReference），
    /// 因此浅色/深色与强调色切换可实时生效，无需重建页面。
    /// </summary>
    public static class Ui
    {
        public static readonly CornerRadius CardRadius = new CornerRadius(8);
        public static readonly CornerRadius BtnRadius = new CornerRadius(6);

        // ── 资源解析 ──
        public static Brush Res(string key)
        {
            try
            {
                object o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                if (o is Brush) return (Brush)o;
            }
            catch { }
            return Brushes.Gray;
        }

        /// <summary>把元素属性绑定到主题资源（等价于 XAML 的 DynamicResource）</summary>
        public static T Themed<T>(this T el, DependencyProperty prop, string resKey) where T : FrameworkElement
        {
            try
            {
                if (Application.Current != null) el.SetResourceReference(prop, resKey);
            }
            catch { }
            return el;
        }

        public static Brush TextBrush { get { return Res("PrimaryTextBrush"); } }
        public static Brush SubBrush { get { return Res("SecondaryTextBrush"); } }
        public static Brush BorderBrushRes { get { return Res("BorderBrush"); } }
        public static Brush Region { get { return Res("RegionBrush"); } }
        public static Brush SecondaryRegion { get { return Res("SecondaryRegionBrush"); } }
        public static Brush Tertiary { get { return Res("TextTertiaryBrush"); } }
        public static Brush Brand { get { return Res("BrandBrush"); } }
        public static Brush Ok { get { return Res("OkBrush"); } }
        public static Brush Warn { get { return Res("WarnBrush"); } }
        public static Brush Err { get { return Res("ErrBrush"); } }

        /// <summary>取脚本用字体族</summary>
        public static FontFamily Icon
        {
            get { return (FontFamily)new FontFamilyConverter().ConvertFromInvariantString("Segoe Fluent Icons, Segoe MDL2 Assets"); }
        }

        public static FontFamily AppFontFamily
        {
            get { return (FontFamily)new FontFamilyConverter().ConvertFromInvariantString("Segoe UI, Microsoft YaHei UI, Microsoft YaHei"); }
        }

        public static FontFamily MonoFontFamily
        {
            get { return (FontFamily)new FontFamilyConverter().ConvertFromInvariantString("Cascadia Mono, Consolas, Courier New"); }
        }

        // ── 文本 ──
        public static TextBlock T(string text, double size = 12.8, Brush color = null, bool bold = false)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontFamily = AppFontFamily,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            ApplyForeground(t, color);
            return t;
        }

        /// <summary>可作为前景色的主题资源键（用于把本地画刷还原为动态引用）</summary>
        static readonly string[] ColorKeys = {
            "PrimaryTextBrush", "SecondaryTextBrush", "TextTertiaryBrush", "TextDisabledBrush",
            "BrandBrush", "OkBrush", "WarnBrush", "ErrBrush", "InfoBrush", "TextOnAccentBrush",
        };

        /// <summary>若画刷正是某个主题资源的实例，返回其键名，否则返回 null</summary>
        internal static string KeyOf(Brush b)
        {
            if (b == null || Application.Current == null) return null;
            for (int i = 0; i < ColorKeys.Length; i++)
            {
                object o;
                try { o = Application.Current.TryFindResource(ColorKeys[i]); } catch { o = null; }
                if (ReferenceEquals(o, b)) return ColorKeys[i];
            }
            return null;
        }

        /// <summary>
        /// 设置前景色：能映射到主题资源的画刷改用动态引用，
        /// 保证运行时切换浅色/深色或强调色后已创建的控件也会同步刷新。
        /// </summary>
        internal static void ApplyForeground(TextBlock t, Brush color)
        {
            if (color == null) { t.Themed(TextBlock.ForegroundProperty, "PrimaryTextBrush"); return; }
            string key = KeyOf(color);
            if (key != null) t.Themed(TextBlock.ForegroundProperty, key);
            else t.Foreground = color;
        }

        /// <summary>主题资源着色的文本</summary>
        public static TextBlock Tk(string text, double size, string resKey, bool bold = false)
        {
            var t = T(text, size, null, bold);
            t.Themed(TextBlock.ForegroundProperty, resKey);
            return t;
        }

        /// <summary>图标字形</summary>
        public static TextBlock Glyph(string glyph, double size, string resKey, bool bold = false)
        {
            var t = new TextBlock
            {
                Text = glyph,
                FontFamily = Icon,
                FontSize = size,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.Themed(TextBlock.ForegroundProperty, resKey);
            return t;
        }

        public static TextBlock PageTitle(string text) { var t = T(text, 22, null, true); t.Margin = new Thickness(0); return t; }
        public static TextBlock PageSub(string text) { var t = T(text, 12.8); t.Themed(TextBlock.ForegroundProperty, "SecondaryTextBrush"); t.Margin = new Thickness(0, 4, 0, 0); return t; }
        public static TextBlock CardTitle(string text) { var t = T(text, 14.5, null, true); return t; }
        public static TextBlock CardSub(string text) { var t = T(text, 11.5); t.Themed(TextBlock.ForegroundProperty, "SecondaryTextBrush"); t.Margin = new Thickness(0, 3, 0, 0); return t; }

        // ── 基础容器 ──
        public static Button Btn(string text, string styleKey, Action onClick)
        {
            var b = new Button { Content = text };
            try { b.Style = (Style)Application.Current.FindResource(styleKey); } catch { }
            if (onClick != null) b.Click += delegate { onClick(); };
            return b;
        }

        public static Button IconBtn(string glyph, string tooltip, Action onClick)
        {
            var b = new Button { Content = Glyph(glyph, 13, "PrimaryTextBrush") };
            b.Style = (Style)Application.Current.FindResource("BtnGhost");
            b.MinWidth = 32;
            b.Width = 32;
            b.Height = 32;
            b.Padding = new Thickness(0);
            if (!string.IsNullOrEmpty(tooltip)) b.ToolTip = tooltip;
            if (onClick != null) b.Click += delegate { onClick(); };
            return b;
        }

        /// <summary>
        /// 页头响应式：宽度不足时把操作区从标题右侧折到标题下方，并让按钮自动换行。
        /// 只在页面构建期挂一次，运行期无额外开销。
        /// </summary>
        public static void ResponsiveHeader(Grid head, StackPanel titles, Panel actions)
        {
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool stacked = false;

            head.SizeChanged += delegate
            {
                // 仅累加子元素自然宽度（与容器宽度无关），避免在折行/展开之间来回抖动
                double need = 320;
                foreach (UIElement c in actions.Children)
                {
                    var f = c as FrameworkElement;
                    if (f != null) need += f.DesiredSize.Width + 8;
                }

                bool want = head.ActualWidth > 0 && head.ActualWidth < need;
                if (want == stacked) return;
                stacked = want;

                if (want)
                {
                    Grid.SetRow(actions, 1);
                    Grid.SetColumn(actions, 0);
                    Grid.SetColumnSpan(actions, 2);
                    actions.HorizontalAlignment = HorizontalAlignment.Left;
                    actions.Margin = new Thickness(0, 10, 0, 0);
                }
                else
                {
                    Grid.SetRow(actions, 0);
                    Grid.SetColumn(actions, 1);
                    Grid.SetColumnSpan(actions, 1);
                    actions.HorizontalAlignment = HorizontalAlignment.Right;
                    actions.Margin = new Thickness(0);
                }
            };
        }
        /// <summary>页面根：标题行 + 副标题 + 右侧操作区 + 可滚动内容</summary>
        public static Grid PageRoot(string title, string sub, out StackPanel content, out WrapPanel actions)
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(PageTitle(title));
            if (!string.IsNullOrEmpty(sub)) titles.Children.Add(PageSub(sub));
            Grid.SetColumn(titles, 0);
            head.Children.Add(titles);

            actions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);
            ResponsiveHeader(head, titles, actions);

            Grid.SetRow(head, 0);
            root.Children.Add(head);

            content = new StackPanel { Margin = new Thickness(0, 0, 4, 20) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 8, 0),
                Content = content,
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            return root;
        }

        /// <summary>卡片：返回 Border，body 为内容容器</summary>
        public static Border Card(string title, string sub, out StackPanel body, UIElement headerRight = null)
        {
            body = new StackPanel();
            var outer = new Border();
            try { outer.Style = (Style)Application.Current.FindResource("Card"); } catch { }
            outer.Margin = new Thickness(0, 0, 0, 14);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(sub) ? 10 : 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBox = new StackPanel();
            titleBox.Children.Add(CardTitle(title));
            if (!string.IsNullOrEmpty(sub)) titleBox.Children.Add(CardSub(sub));
            Grid.SetColumn(titleBox, 0);
            head.Children.Add(titleBox);

            if (headerRight != null)
            {
                Grid.SetColumn(headerRight, 1);
                head.Children.Add(headerRight);
            }

            Grid.SetRow(head, 0);
            Grid.SetRow(body, 1);
            grid.Children.Add(head);
            grid.Children.Add(body);
            outer.Child = grid;
            return outer;
        }

        /// <summary>卡片（无标题，纯内容）</summary>
        public static Border Panel(UIElement child)
        {
            var outer = new Border();
            try { outer.Style = (Style)Application.Current.FindResource("Card"); } catch { }
            outer.Margin = new Thickness(0, 0, 0, 14);
            outer.Child = child;
            return outer;
        }

        /// <summary>设置/优化行：标题 + 描述（右侧放控件）</summary>
        public static Grid Row(string title, string desc, UIElement right, bool admin = false)
        {
            var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(T(title, 13.2, null, true));
            if (admin) titlePanel.Children.Add(AdminBadge());
            left.Children.Add(titlePanel);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = T(desc, 11.8);
                d.Themed(TextBlock.ForegroundProperty, "SecondaryTextBrush");
                d.Margin = new Thickness(0, 3, 0, 0);
                left.Children.Add(d);
            }
            Grid.SetColumn(left, 0);

            var holder = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (right != null) holder.Children.Add(right);
            Grid.SetColumn(holder, 1);

            g.Children.Add(left);
            g.Children.Add(holder);
            return g;
        }

        /// <summary>分组小标题</summary>
        public static TextBlock GroupHeader(string text)
        {
            var t = T(text, 11.5, null, true);
            t.Themed(TextBlock.ForegroundProperty, "BrandBrush");
            t.Margin = new Thickness(0, 10, 0, 2);
            return t;
        }

        public static Border AdminBadge()
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Glyph("\uE7EF", 10.5, "WarnBrush"),
            };
            b.Themed(Border.BackgroundProperty, "WarnLightBrush");
            return b;
        }

        /// <summary>状态/语义徽标</summary>
        public static Border Badge(string text, string kind)
        {
            string bg = "BadgeBrush";
            string fg = "SecondaryTextBrush";
            if (kind == "ok") { bg = "OkLightBrush"; fg = "OkBrush"; }
            else if (kind == "warn") { bg = "WarnLightBrush"; fg = "WarnBrush"; }
            else if (kind == "err") { bg = "ErrLightBrush"; fg = "ErrBrush"; }
            else if (kind == "brand") { bg = "BrandLightBrush"; fg = "BrandBrush"; }
            else if (kind == "risk") { bg = "ErrLightBrush"; fg = "ErrBrush"; }

            var txt = T(text, 11, null, false);
            txt.Themed(TextBlock.ForegroundProperty, fg);

            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = txt,
            };
            b.Themed(Border.BackgroundProperty, bg);
            return b;
        }

        public static Border Tag(string text, string bgKey, string fgKey)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Tk(text, 11, fgKey),
            };
            b.Themed(Border.BackgroundProperty, bgKey);
            return b;
        }

        /// <summary>iOS 风开关</summary>
        public static ToggleButton Switch(bool isOn, Action<bool> onToggle)
        {
            var s = new ToggleButton { IsChecked = isOn, VerticalAlignment = VerticalAlignment.Center };
            try { s.Style = (Style)Application.Current.FindResource("Switch"); } catch { }
            if (onToggle != null)
            {
                s.Checked += delegate { onToggle(true); };
                s.Unchecked += delegate { onToggle(false); };
            }
            return s;
        }

        /// <summary>统计卡（大字号文本通过返回 Border.Tag 取回）</summary>
        public static Border Stat(string glyph, string big, string label, string accentKey)
        {
            var accent = Res(accentKey.EffectiveBrushKey());
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 14, 14),
                Padding = new Thickness(16, 13, 16, 13),
                MinHeight = 92,
                BorderThickness = new Thickness(1),
            };
            card.Themed(Border.BackgroundProperty, "RegionBrush");
            card.Themed(Border.BorderBrushProperty, "CardBorderBrush");

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new StackPanel();
            var lb = T(label, 11.8);
            lb.Themed(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            box.Children.Add(lb);

            var bigText = T(big, 21, null, true);
            bigText.Margin = new Thickness(0, 6, 0, 0);
            box.Children.Add(bigText);
            card.Tag = bigText;
            Grid.SetColumn(box, 0);

            var icoWrap = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(9),
                VerticalAlignment = VerticalAlignment.Top,
                Child = Glyph(glyph, 17, accentKey),
            };
            icoWrap.Themed(Border.BackgroundProperty, accentKey.LightVariantKey());
            Grid.SetColumn(icoWrap, 1);

            g.Children.Add(box);
            g.Children.Add(icoWrap);
            card.Child = g;
            return card;
        }

        static string EffectiveBrushKey(this string key) { return key; }

        static string LightVariantKey(this string accentKey)
        {
            if (accentKey == "WarnBrush") return "WarnLightBrush";
            if (accentKey == "ErrBrush" || accentKey == "DangerBrush") return "ErrLightBrush";
            if (accentKey == "OkBrush") return "OkLightBrush";
            return "BrandLightBrush";
        }

        public static Grid InfoRow(string label, string value, Brush valueBrush = null)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l = T(label, 12.2);
            l.Themed(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            var v = T(value, 12.2, valueBrush);
            Grid.SetColumn(l, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(l);
            g.Children.Add(v);
            return g;
        }

        public static Border Divider()
        {
            var b = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
            b.Themed(Border.BackgroundProperty, "DividerBrush");
            return b;
        }

        public static StackPanel VStack(double spacing)
        {
            return new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0) };
        }

        public static Grid HStack(params UIElement[] children)
        {
            var g = new Grid();
            for (int i = 0; i < children.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(children[i], i);
                FrameworkElement fe = children[i] as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
                g.Children.Add(children[i]);
            }
            return g;
        }

        /// <summary>统一日志框</summary>
        public static TextBox LogBox(double height)
        {
            var tb = new TextBox { Height = height };
            try { tb.Style = (Style)Application.Current.FindResource("LogBox"); } catch { }
            return tb;
        }

        public static void AppendLog(TextBox box, string line)
        {
            if (box == null) return;
            try
            {
                box.AppendText((box.Text.Length == 0 ? "" : Environment.NewLine) + line);
                box.ScrollToEnd();
            }
            catch { }
        }

        static int _pending;

        /// <summary>仍在执行中的后台任务数（供截图/诊断等待页面加载完成）</summary>
        public static int PendingWork { get { return System.Threading.Volatile.Read(ref _pending); } }

        /// <summary>后台执行 + 完成后回 UI 线程（异常自动提示）</summary>
        public static void RunAsync(Action work, Action onDone = null)
        {
            System.Threading.Interlocked.Increment(ref _pending);
            Task.Run(delegate
            {
                try
                {
                    try { work(); }
                    catch (Exception ex)
                    {
                        Logger.Log("UI", "后台任务异常: " + ex);
                        if (Application.Current != null)
                            Application.Current.Dispatcher.Invoke(delegate { Notify.Error("操作失败: " + ex.Message); });
                    }
                    // 先让 UI 线程把结果画上去，再宣告本次任务结束
                    if (onDone != null && Application.Current != null)
                        Application.Current.Dispatcher.Invoke(onDone);
                }
                finally { System.Threading.Interlocked.Decrement(ref _pending); }
            });
        }
    }
}