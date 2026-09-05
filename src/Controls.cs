using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 调色板 + 字体 + 通用控件工厂（浅色简洁风格）═══
    public static class C
    {
        // 颜色
        public static readonly Color PageBg   = Color.FromArgb(244, 246, 250);
        public static readonly Color Card     = Color.White;
        public static readonly Color Border   = Color.FromArgb(226, 232, 240);
        public static readonly Color TextMain = Color.FromArgb(28, 32, 43);
        public static readonly Color TextSub  = Color.FromArgb(100, 110, 130);
        public static readonly Color TextDim  = Color.FromArgb(160, 168, 180);
        public static readonly Color Accent   = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentLt = Color.FromArgb(234, 241, 254);
        public static readonly Color HoverBg  = Color.FromArgb(241, 245, 249);
        public static readonly Color Green    = Color.FromArgb(5, 150, 105);
        public static readonly Color Red      = Color.FromArgb(220, 38, 38);
        public static readonly Color Orange   = Color.FromArgb(217, 119, 6);
        public static readonly Color Zebra    = Color.FromArgb(249, 250, 251);

        static double _scale = -1;
        public static int S(int v)
        {
            if (_scale < 0)
            {
                _scale = 1.0;
                try
                {
                    IntPtr dc = GetDC(IntPtr.Zero);
                    int dpi = GetDeviceCaps(dc, 88 /*LOGPIXELSX*/);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (dpi >= 120 && dpi <= 384) _scale = dpi / 96.0;
                }
                catch { }
            }
            return (int)Math.Round(v * _scale);
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")]  static extern int GetDeviceCaps(IntPtr hdc, int idx);

        static string _uiFontName;
        static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        static string UiFontName()
        {
            if (_uiFontName == null)
            {
                _uiFontName = "Microsoft YaHei UI";
                using (var f = new Font(_uiFontName, 9f)) { if (f.Name != _uiFontName) _uiFontName = "Microsoft YaHei"; }
                using (var f = new Font(_uiFontName, 9f)) { if (f.Name != _uiFontName) _uiFontName = "Microsoft Sans Serif"; }
            }
            return _uiFontName;
        }

        /// <summary>UI 字体（按磅值取，自动缓存）</summary>
        public static Font F(float pt, bool bold = false)
        {
            string key = UiFontName() + "|" + pt + "|" + bold;
            Font f;
            if (!_fonts.TryGetValue(key, out f))
            {
                f = new Font(UiFontName(), pt, bold ? FontStyle.Bold : FontStyle.Regular);
                _fonts[key] = f;
            }
            return f;
        }

        public static Font Mono(float pt)
        {
            string key = "M|" + pt;
            Font f;
            if (!_fonts.TryGetValue(key, out f)) { f = new Font("Consolas", pt); _fonts[key] = f; }
            return f;
        }

        /// <summary>创建主/次/危险按钮</summary>
        public static FlatBtn Btn(string text, int kind)   // 0=主蓝 1=次 2=红 3=文字绿
        {
            var b = new FlatBtn(text);
            switch (kind)
            {
                case 0: b.Main(); break;
                case 1: b.Soft(); break;
                case 2: b.Red(); break;
                default: b.Green(); break;
            }
            return b;
        }

        public static Label Lbl(string text, float pt, Color? color, bool bold = false)
        {
            return new Label
            {
                Text = text,
                Font = F(pt, bold),
                ForeColor = color ?? C.TextSub,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
        }

        public static GraphicsPath RoundPath(Rectangle r, int rad)
        {
            int d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>带圆角描边的卡片容器</summary>
        public static RCard CardBox(Padding pad)
        {
            var c = new RCard();
            c.Padding = pad;
            return c;
        }

        public static string Bytes(long b)
        {
            double v = b;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < 4) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "F0" : "F1") + " " + u[i];
        }

        /// <summary>在主 UI 线程执行（若需要）</summary>
        public static void On(Control ctl, Action act)
        {
            if (ctl == null || ctl.IsDisposed) return;
            try
            {
                if (ctl.InvokeRequired) { ctl.BeginInvoke(act); }
                else act();
            }
            catch { }
        }

        public static DialogResult Ask(string msg, string title = "确认", bool danger = false)
        {
            return MessageBox.Show(msg, title,
                MessageBoxButtons.YesNo,
                danger ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        }
    }

    // ── 扁平按钮 ──
    public class FlatBtn : Button
    {
        public FlatBtn(string text)
        {
            Text = text;
            Font = C.F(9.5f);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Height = C.S(30);
            MinimumSize = new Size(C.S(40), C.S(30));
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        public void Main()
        {
            BackColor = C.Accent; ForeColor = Color.White;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(59, 130, 246);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 78, 216);
        }
        public void Soft()
        {
            BackColor = Color.White; ForeColor = C.TextMain;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = C.Border;
            FlatAppearance.MouseOverBackColor = C.HoverBg;
            FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 232, 240);
        }
        public void Red()
        {
            BackColor = Color.White; ForeColor = C.Red;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Color.FromArgb(254, 205, 205);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 242, 242);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(254, 226, 226);
        }
        public void Green()
        {
            BackColor = Color.White; ForeColor = C.Green;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Color.FromArgb(187, 232, 214);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 253, 245);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(209, 250, 229);
        }
        public void Ghost()
        {
            BackColor = Color.Transparent; ForeColor = C.Accent;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = C.AccentLt;
        }
    }

    // ── 圆角卡片（AutoFit：高度自动撑满 Top 停靠子控件）──
    public class RCard : Panel
    {
        public int Radius = 8;
        bool _fitting;
        public RCard()
        {
            BackColor = C.Card;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(C.Border))
            using (var path = C.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                g.DrawPath(p, path);
        }
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Recalc();
        }

        /// <summary>按子控件重新计算高度（Top/Fill 顺序叠加，Bottom 另计在尾部）。可随时独立调用。</summary>
        public void Recalc()
        {
            if (_fitting) return;
            // 窗口在全屏切换/最小化等异常尺寸下不重算高度，防止把坏高度缓存下来
            if (ClientSize.Width < C.S(220)) return;
            _fitting = true;
            try
            {
                int total = Padding.Top + Padding.Bottom;
                int bottomH = 0;
                int w = Math.Max(200, ClientSize.Width - Padding.Horizontal);
                foreach (Control ch in Controls)
                {
                    int h = ch.Height;
                    if (ch.AutoSize)
                    {
                        try { h = ch.GetPreferredSize(new Size(w, 0)).Height; }
                        catch { }
                    }
                    h += ch.Margin.Vertical;
                    if (h <= 0) continue;
                    if (ch.Dock == DockStyle.Bottom) bottomH += h;
                    else total += h;
                }
                total += bottomH;
                int need = Math.Max(30, total);
                if (Math.Abs(Height - need) > 2) Height = need;
            }
            finally { _fitting = false; }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // ── 简洁网格 ──
    public class ExGrid : DataGridView
    {
        public ExGrid()
        {
            BackgroundColor = Color.White;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            GridColor = Color.FromArgb(238, 242, 247);
            RowHeadersVisible = false;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToResizeColumns = false;
            ReadOnly = true;
            MultiSelect = true;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            EnableHeadersVisualStyles = false;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersHeight = C.S(34);
            RowTemplate.Height = C.S(30);
            DefaultCellStyle.Font = C.F(9f);
            DefaultCellStyle.ForeColor = C.TextMain;
            DefaultCellStyle.SelectionBackColor = C.AccentLt;
            DefaultCellStyle.SelectionForeColor = C.TextMain;
            DefaultCellStyle.Padding = new Padding(C.S(6), 0, 0, 0);
            ColumnHeadersDefaultCellStyle.Font = C.F(9f, true);
            ColumnHeadersDefaultCellStyle.ForeColor = C.TextSub;
            ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            ColumnHeadersDefaultCellStyle.Padding = new Padding(C.S(6), 0, 0, 0);
            AlternatingRowsDefaultCellStyle.BackColor = C.Zebra;
            AutoGenerateColumns = false;
            try
            {
                var pi = typeof(DataGridView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pi != null) pi.SetValue(this, true, null);
            }
            catch { }
        }
        public DataGridViewTextBoxColumn Col(string name, string header, int weightPct)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weightPct,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
        }
    }

    /// <summary>单行日志输出（简易）</summary>
    public class LogBox : RichTextBox
    {
        public LogBox()
        {
            ReadOnly = true;
            BackColor = Color.FromArgb(248, 250, 252);
            BorderStyle = BorderStyle.None;
            Font = C.F(9f);
            WordWrap = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            DetectUrls = false;
        }
        public void Log(string msg, bool err = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((MethodInvoker)(() => Log(msg, err))); } catch { } return; }
            AppendText((err ? "✘ " : "· ") + msg + "\r\n");
            SelectionStart = TextLength;
            ScrollToCaret();
        }
        public void ClearAll()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((MethodInvoker)ClearAll); } catch { } return; }
            Clear();
        }
    }
}
