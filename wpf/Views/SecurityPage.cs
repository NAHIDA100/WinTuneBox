using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinTune.Wpf.Views
{
    /// <summary>
    /// 安全体检：只读检测系统安全机制现状并给出建议，绝不自动修改这些机制。
    /// </summary>
    public partial class SecurityPage : UserControl
    {
        RingGauge _gauge;
        TextBlock _advice, _summary;
        StackPanel _body, _issuesBody;
        Button _scanBtn;
        bool _built, _busy;

        public SecurityPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        void Build()
        {
            StackPanel content; WrapPanel actions;
            Root.Children.Add(Ui.PageRoot("安全体检", "Defender · 防火墙 · 受控文件夹 · 系统还原 · UAC · 安全启动 · 遥测 · SMBv1", out content, out actions));

            _scanBtn = Ui.Btn("重新检测", "BtnDefault", Scan);
            actions.Children.Add(_scanBtn);

            content.Children.Add(ScoreCard());
            content.Children.Add(DetailCard());
            content.Children.Add(BoundaryCard());
            Scan();
        }

        UIElement ScoreCard()
        {
            StackPanel body;
            var card = Ui.Card("安全评分", "综合各项安全机制的当前状态（只读评估）", out body);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _gauge = new RingGauge
            {
                Width = 104,
                Height = 104,
                Value = 0,
                RingThickness = 10,
                ValueText = "--",
                Caption = "安全分",
            };
            _gauge.Themed(RingGauge.TrackBrushProperty, "TrackBrush");
            _gauge.Themed(RingGauge.RingBrushProperty, "BrandBrush");
            Grid.SetColumn(_gauge, 0);
            row.Children.Add(_gauge);

            var txt = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _advice = Ui.T("正在检测…", 13.5, Ui.TextBrush, true);
            _advice.TextWrapping = TextWrapping.Wrap;
            _summary = Ui.T("", 12, Ui.SubBrush);
            _summary.TextWrapping = TextWrapping.Wrap;
            _summary.Margin = new Thickness(0, 6, 0, 0);
            txt.Children.Add(_advice);
            txt.Children.Add(_summary);
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);
            body.Children.Add(row);
            return card;
        }

        UIElement DetailCard()
        {
            StackPanel body;
            var card = Ui.Card("检测详情", "每项均显示当前状态与建议；本页不会修改任何安全设置", out body);
            _body = new StackPanel();
            body.Children.Add(_body);
            return card;
        }

        UIElement BoundaryCard()
        {
            StackPanel body;
            var card = Ui.Card("安全边界", "以下内容本工具永不自动修改，仅在体检中报告状态", out body);
            _issuesBody = new StackPanel();
            body.Children.Add(_issuesBody);

            string[] lines = {
                "Windows Defender 实时保护与防篡改设置",
                "Windows 更新策略与更新通道",
                "系统还原功能本身（仅可管理还原点占用）",
            };
            foreach (string s in lines)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(Ui.Glyph("\uE73E", 12, "OkBrush"));
                var t = Ui.T(s, 12.2, Ui.SubBrush);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                _issuesBody.Children.Add(g);
            }
            return card;
        }

        void Scan()
        {
            if (_busy) return;
            _busy = true;
            if (_scanBtn != null) _scanBtn.IsEnabled = false;
            _advice.Text = "正在检测…";
            _summary.Text = "";
            _body.Children.Clear();

            Ui.RunAsync(delegate
            {
                List<SecurityItem> items;
                int score = 0, issues = 0;
                string advice = "";
                try
                {
                    items = SecurityService.Check();
                    score = SecurityService.HealthScore(out advice, out issues);
                }
                catch (Exception ex)
                {
                    items = new List<SecurityItem>();
                    advice = "检测失败：" + ex.Message;
                    Logger.Log("Security", advice);
                }

                Dispatcher.Invoke(delegate
                {
                    _busy = false;
                    if (_scanBtn != null) _scanBtn.IsEnabled = true;

                    _gauge.Value = score;
                    _gauge.ValueText = score.ToString();
                    _gauge.Themed(RingGauge.RingBrushProperty, score >= 85 ? "OkBrush" : (score >= 60 ? "BrandBrush" : "WarnBrush"));
                    _advice.Text = string.IsNullOrEmpty(advice) ? "检测完成" : advice;
                    _summary.Text = issues > 0 ? issues + " 项建议关注" : "未发现明显问题";

                    _body.Children.Clear();
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) _body.Children.Add(Ui.Divider());
                        _body.Children.Add(ItemRow(items[i]));
                    }
                });
            });
        }

        static UIElement ItemRow(SecurityItem it)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Ui.T(it.Title, 13.2, Ui.TextBrush, true));
            if (!string.IsNullOrEmpty(it.Detail))
            {
                var d = Ui.T(it.Detail, 11.8, Ui.SubBrush);
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 3, 0, 0);
                left.Children.Add(d);
            }
            Grid.SetColumn(left, 0);

            string kind = it.Kind == "ok" ? "ok" : it.Kind == "warn" ? "warn" : it.Kind == "err" ? "err" : "idle";
            var badge = Ui.Badge(it.State ?? "—", kind);
            Grid.SetColumn(badge, 1);

            g.Children.Add(left);
            g.Children.Add(badge);
            return g;
        }
    }
}