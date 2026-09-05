using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 页面基类 ═══
    public abstract class UPage : UserControl
    {
        public Panel Root;
        public LogBox Log;
        protected UPage()
        {
            BackColor = C.PageBg;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Root = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = C.PageBg,
            };
            Controls.Add(Root);
        }

        /// <summary>被切换到前台时调用</summary>
        public virtual void OnShown() { }

        /// <summary>页面标题（含说明行）。必须先调用再放卡片</summary>
        protected Label Title(string main, string sub)
        {
            var l1 = new Label
            {
                Text = main,
                Font = C.F(15f, true),
                ForeColor = C.TextMain,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, C.S(2)),
            };
            l1.Dock = DockStyle.Top;
            l1.Padding = new Padding(C.S(2), 0, 0, 0);
            Root.Controls.Add(l1);

            if (!string.IsNullOrEmpty(sub))
            {
                var l2 = new Label
                {
                    Text = sub,
                    Font = C.F(9f),
                    ForeColor = C.TextSub,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                };
                l2.Dock = DockStyle.Top;
                l2.Padding = new Padding(C.S(3), 0, 0, C.S(10));
                Root.Controls.Add(l2);
            }
            return l1;
        }

        /// <summary>卡片容器（放于最下方，内容自增高度）</summary>
        protected RCard Card(int minH = 40)
        {
            var c = new RCard
            {
                Dock = DockStyle.Top,
                Height = C.S(minH),
                Padding = new Padding(C.S(16), C.S(12), C.S(16), C.S(12)),
            };
            c.Margin = new Padding(0, 0, 0, C.S(12));
            Root.Controls.Add(c);
            return c;
        }

        /// <summary>卡片内小节标题行（宽块）</summary>
        protected Label CardTitle(RCard card, string text, Color? color = null)
        {
            var l = new Label
            {
                Text = text,
                Font = C.F(11f, true),
                ForeColor = color ?? C.TextMain,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, C.S(6)),
            };
            card.Controls.Add(l);
            return l;
        }

        protected void CardNote(RCard card, string text, Color? color = null)
        {
            var l = new Label
            {
                Text = text,
                Font = C.F(9f),
                ForeColor = color ?? C.TextSub,
                AutoSize = true,
                MaximumSize = new Size(C.S(920), 0),
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, C.S(10)),
            };
            card.Controls.Add(l);
        }

        /// <summary>底部留白，保证滚动到底不贴边</summary>
        protected void EndSpace(int h = 16)
        {
            var sp = new Panel { Dock = DockStyle.Top, Height = C.S(h) };
            Root.Controls.Add(sp);
        }

        /// <summary>页面构建完成后调用：先校正停靠顺序，再两轮收敛卡片高度与布局。
        /// 注：WinForms Dock=Top 按集合 index 降序停靠（后添加的排最上），
        /// 与页面按“标题→卡片”的添加顺序相反，故需将集合整体反转一次（幂等标记）。
        /// 顺带解决“网络工具只剩 hosts 卡/标题沉底”的倒置问题。</summary>
        public void FinishPage()
        {
            try
            {
                if (!_orderFixed)
                {
                    _orderFixed = true;
                    var list = new System.Collections.Generic.List<Control>();
                    foreach (Control c in Root.Controls) list.Add(c);
                    for (int i = 0; i < list.Count; i++)
                        Root.Controls.SetChildIndex(list[i], list.Count - 1 - i);
                }
                for (int round = 0; round < 2; round++)
                {
                    foreach (Control c in Root.Controls)
                    {
                        var rc = c as RCard;
                        if (rc != null) rc.Recalc();
                    }
                    Root.PerformLayout();
                }
                Root.Invalidate(true);
            }
            catch { }
        }
        bool _orderFixed;

        /// <summary>忙碌游标执行动作</summary>
        protected void Busy(Action a)
        {
            UseWaitCursor = true;
            try { a(); }
            finally { UseWaitCursor = false; }
        }
    }

    // ═══ 迷你进度条 ═══
    public class MBar : Panel
    {
        float _value;                 // 0-100
        public Color Fill = C.Accent;
        public MBar()
        {
            Height = C.S(8);
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        public float Value { get { return _value; } set { _value = value < 0 ? 0 : (value > 100 ? 100 : value); Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = new Rectangle(0, 1, Width - 1, Height - 2);
            using (var path = C.RoundPath(rc, rc.Height / 2))
            {
                using (var b = new SolidBrush(Color.FromArgb(232, 236, 242))) g.FillPath(b, path);
                int w = (int)(rc.Width * _value / 100f);
                if (w > 4)
                {
                    using (var p2 = C.RoundPath(new Rectangle(rc.X, rc.Y, w, rc.Height), rc.Height / 2))
                    using (var b2 = new SolidBrush(Fill)) g.FillPath(b2, p2);
                }
            }
        }
    }

    // ═══ 指标块（caption 灰小字 + value 深色大字，纵向排列）═══
    public class Metric : Panel
    {
        public readonly Label Value;
        readonly Label _cap;
        public Metric(Control parent, string caption)
        {
            Height = C.S(46);
            Width = C.S(280);
            Dock = DockStyle.Top;
            _cap = new Label
            {
                Text = caption,
                Font = C.F(9f),
                ForeColor = C.TextSub,
                AutoSize = true,
                Location = new Point(C.S(2), 0),
                BackColor = Color.Transparent,
            };
            Value = new Label
            {
                Font = C.F(11f, true),
                ForeColor = C.TextMain,
                AutoSize = true,
                Location = new Point(C.S(2), C.S(18)),
                BackColor = Color.Transparent,
            };
            Controls.Add(_cap);
            Controls.Add(Value);
            parent.Controls.Add(this);
        }
        public void SetText(string v)
        {
            C.On(this, delegate { if (!Value.IsDisposed) Value.Text = v ?? ""; });
        }
        public void SetColor(Color c) { C.On(this, delegate { Value.ForeColor = c; }); }
    }
}
