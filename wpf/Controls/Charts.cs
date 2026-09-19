using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WinTune.Wpf
{
    /// <summary>自绘圆环仪表（健康分），无图表库、无位图、支持数据绑定</summary>
    public class RingGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            "Caption", typeof(string), typeof(RingGauge),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
            "RingThickness", typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
            "RingBrush", typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            "TrackBrush", typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
            "ValueText", typeof(string), typeof(RingGauge),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public string Caption { get { return (string)GetValue(CaptionProperty); } set { SetValue(CaptionProperty, value); } }
        public double RingThickness { get { return (double)GetValue(RingThicknessProperty); } set { SetValue(RingThicknessProperty, value); } }
        public Brush RingBrush { get { return (Brush)GetValue(RingBrushProperty); } set { SetValue(RingBrushProperty, value); } }
        public Brush TrackBrush { get { return (Brush)GetValue(TrackBrushProperty); } set { SetValue(TrackBrushProperty, value); } }
        public string ValueText { get { return (string)GetValue(ValueTextProperty); } set { SetValue(ValueTextProperty, value); } }

        static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 2 || h <= 2) return;

            double thick = Math.Max(2, RingThickness);
            double r = Math.Min(w, h) / 2.0 - thick / 2.0 - 1;
            if (r <= 1) return;
            var center = new Point(w / 2, h / 2);

            Brush track = TrackBrush ?? new SolidColorBrush(Color.FromRgb(0xDF, 0xE3, 0xE8));
            dc.DrawEllipse(null, new Pen(track, thick), center, r, r);

            double val = Value;
            if (val < 0) val = 0;
            if (val > 100) val = 100;

            if (val > 0.2)
            {
                double sweep = 359.6 * val / 100.0;
                Brush ring = RingBrush ?? new SolidColorBrush(Color.FromRgb(0x2D, 0x7C, 0xF6));
                var pen = new Pen(ring, thick);
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                dc.DrawGeometry(null, pen, Arc(center, r, -90, sweep));
            }

            string big = ValueText;
            if (string.IsNullOrEmpty(big)) big = Math.Round(val).ToString(CultureInfo.InvariantCulture);

            var brush = (Brush)(TryFind("PrimaryTextBrush") ?? new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x24)));
            var ft = new FormattedText(big, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face,
                Math.Max(14, r * 0.62), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2 - (string.IsNullOrEmpty(Caption) ? 0 : 7)));

            if (!string.IsNullOrEmpty(Caption))
            {
                var sub = (Brush)(TryFind("SecondaryTextBrush") ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x74, 0x80)));
                var ft2 = new FormattedText(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face,
                    Math.Max(10, r * 0.20), sub, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft2, new Point(center.X - ft2.Width / 2, center.Y + r * 0.30));
            }
        }

        internal static object TryFind(string key)
        {
            try
            {
                if (Application.Current == null) return null;
                return Application.Current.TryFindResource(key);
            }
            catch { return null; }
        }

        internal static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(PointOn(c, r, startDeg), false, false);
                ctx.ArcTo(PointOn(c, r, startDeg + sweepDeg), new Size(r, r), 0,
                    Math.Abs(sweepDeg) > 180,
                    sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                    true, false);
            }
            g.Freeze();
            return g;
        }

        static Point PointOn(Point c, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }
    }

    /// <summary>迷你趋势线（CPU / 内存占用），自绘、无外部依赖</summary>
    public class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            "Values", typeof(IList<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
            "LineBrush", typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            "FillBrush", typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineThicknessProperty = DependencyProperty.Register(
            "LineThickness", typeof(double), typeof(Sparkline),
            new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

        public IList<double> Values { get { return (IList<double>)GetValue(ValuesProperty); } set { SetValue(ValuesProperty, value); } }
        public Brush LineBrush { get { return (Brush)GetValue(LineBrushProperty); } set { SetValue(LineBrushProperty, value); } }
        public Brush FillBrush { get { return (Brush)GetValue(FillBrushProperty); } set { SetValue(FillBrushProperty, value); } }
        public double LineThickness { get { return (double)GetValue(LineThicknessProperty); } set { SetValue(LineThicknessProperty, value); } }

        protected override void OnRender(DrawingContext dc)
        {
            IList<double> vals = Values;
            double w = ActualWidth, h = ActualHeight;
            if (vals == null || vals.Count < 2 || w <= 4 || h <= 4) return;

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < vals.Count; i++)
            {
                double v = vals[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (max - min < 12) { max = min + 12; }        // 稳定区间也给出可读波动
            if (max > 100) max = 100;
            if (min < 0) min = 0;

            double pad = LineThickness + 1;
            double spanX = (w - pad * 2) / (vals.Count - 1);
            double spanY = (h - pad * 2);
            var pts = new Point[vals.Count];
            for (int i = 0; i < vals.Count; i++)
            {
                double norm = (vals[i] - min) / (max - min);
                if (norm < 0) norm = 0;
                if (norm > 1) norm = 1;
                pts[i] = new Point(pad + spanX * i, pad + spanY * (1 - norm));
            }

            Brush line = LineBrush ?? new SolidColorBrush(Color.FromRgb(0x2D, 0x7C, 0xF6));
            Brush fill = FillBrush;
            if (fill == null)
            {
                var scb = line as SolidColorBrush;
                if (scb != null)
                {
                    var c = scb.Color;
                    fill = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));
                    fill.Freeze();
                }
            }

            if (fill != null)
            {
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].X, h - pad), true, true);
                    for (int i = 0; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
                    ctx.LineTo(new Point(pts[pts.Length - 1].X, h - pad), true, false);
                }
                area.Freeze();
                dc.DrawGeometry(fill, null, area);
            }

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
            }
            geo.Freeze();
            var pen = new Pen(line, LineThickness);
            pen.LineJoin = PenLineJoin.Round;
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            dc.DrawGeometry(null, pen, geo);
        }
    }
}