using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace WinTune.Wpf.Views
{
    /// <summary>
    /// 一键优化：统一操作模型（Id/分组/检测/应用/还原）+ 搜索、状态筛选、
    /// 场景预设（安全推荐 / 游戏 / 办公 / 极简）与后台批量执行。
    /// </summary>
    public partial class TweakPage : UserControl
    {
        static readonly string[] GameKeys = { "游戏", "GPU", "HAGS", "硬件加速", "延迟", "Nagle", "优先级", "全屏",
            "碎片", "搜索索引", "SysMain", "页面文件", "内存压缩", "电源", "处理器", "PCIE", "硬盘空闲" };
        static readonly string[] OfficeKeys = { "动画", "通知", "最近", "自动播放", "OneDrive", "遥测", "反馈",
            "诊断", "输入", "云", "广告", "推广", "搜索", "位置", "活动历史" };
        static readonly string[] MinimalKeys = { "遥测", "广告", "推广", "动画", "通知", "反馈", "启动延迟",
            "云内容", "搜索亮点", "自动播放", "最近使用" };

        readonly List<TweakItemVM> _all = new List<TweakItemVM>();
        CollectionViewSource _cvs;
        ListBox _list;
        TextBox _search;
        ComboBox _filter;
        TextBox _log;
        ProgressBar _progress;
        TextBlock _status, _selection;
        Button _applyBtn, _restoreBtn;
        bool _built, _busy;

        public TweakPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        // ── 构建 ──
        void Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(BuiltHeader(0));
            root.Children.Add(BuiltToolbar(1));
            root.Children.Add(BuiltList(2));
            root.Children.Add(BuiltFooter(3));
            root.Children.Add(BuiltLog(4));
            Root.Children.Add(root);

            Reload();
        }

        UIElement BuiltHeader(int row)
        {
            var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Ui.PageTitle("一键优化"));
            titles.Children.Add(Ui.PageSub("统一操作模型：每项均支持检测 / 应用 / 还原，改动前自动备份注册表"));

            Grid.SetColumn(titles, 0);
            head.Children.Add(titles);

            var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(Ui.Btn("先创建快照", "BtnDefault", delegate { App.Win.NavigateTo("NavSnapshot"); }));
            actions.Children.Add(Spaced(Ui.Btn("重新检测", "BtnDefault", delegate { RescanAll(); })));
            actions.Children.Add(Spaced(Ui.Btn("全选待优化", "BtnDefault", SelectPending)));
            actions.Children.Add(Spaced(Ui.Btn("全不选", "BtnDefault", ClearAll)));
            _restoreBtn = Ui.Btn("还原勾选项", "BtnDefault", delegate { RunBatch(Gather(false), false); });
            actions.Children.Add(Spaced(_restoreBtn));
            _applyBtn = Ui.Btn("一键优化勾选", "BtnPrimary", delegate { RunBatch(Gather(true), true); });
            actions.Children.Add(Spaced(_applyBtn));

            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);
            Ui.ResponsiveHeader(head, titles, actions);
            Grid.SetRow(head, row);
            return head;
        }

        static Button Spaced(Button b) { b.Margin = new Thickness(8, 0, 0, 0); return b; }

        /// <summary>带占位提示的搜索框（WPF TextBox 无原生 Placeholder）</summary>
        static Grid SearchWithHint(TextBox box)
        {
            var g = new Grid();
            g.Children.Add(box);
            var hint = Ui.T("搜索优化项…", 12.2, Ui.Tertiary);
            hint.Margin = new Thickness(11, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.HorizontalAlignment = HorizontalAlignment.Left;
            hint.IsHitTestVisible = false;
            hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += delegate { hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed; };
            g.Children.Add(hint);
            return g;
        }

        UIElement BuiltToolbar(int row)
        {
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _search = new TextBox { Style = (Style)FindResource("SearchBox"), Width = 230, ToolTip = "搜索标题、说明或分组" };
            _search.TextChanged += delegate { ApplyFilter(); };
            ToolTipService.SetShowDuration(_search, 20000);
            Grid.SetColumn(_search, 0);
            bar.Children.Add(SearchWithHint(_search));

            _filter = new ComboBox { Width = 108, Margin = new Thickness(8, 0, 0, 0), SelectedIndex = 0, VerticalAlignment = VerticalAlignment.Center };
            _filter.ItemsSource = new string[] { "全部状态", "待优化", "已优化", "部分", "不支持" };
            _filter.SelectionChanged += delegate { ApplyFilter(); };
            Grid.SetColumn(_filter, 1);
            bar.Children.Add(_filter);

            var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var lbl = Ui.T("预设", 11.5, Ui.SubBrush);
            lbl.VerticalAlignment = VerticalAlignment.Center;
            lbl.Margin = new Thickness(0, 0, 8, 0);
            presets.Children.Add(lbl);
            presets.Children.Add(Ui.Btn("安全推荐", "BtnDefault", delegate { Preset("safe"); }));
            presets.Children.Add(Spaced(Ui.Btn("游戏", "BtnDefault", delegate { Preset("game"); })));
            presets.Children.Add(Spaced(Ui.Btn("办公", "BtnDefault", delegate { Preset("office"); })));
            presets.Children.Add(Spaced(Ui.Btn("极简", "BtnDefault", delegate { Preset("minimal"); })));
            Grid.SetColumn(presets, 3);
            bar.Children.Add(presets);

            _selection = Ui.T("", 11.8, Ui.SubBrush);
            _selection.VerticalAlignment = VerticalAlignment.Center;
            _selection.Margin = new Thickness(12, 0, 12, 0);
            _selection.TextWrapping = TextWrapping.NoWrap;
            _selection.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_selection, 2);
            bar.Children.Add(_selection);

            Grid.SetRow(bar, row);
            return bar;
        }

        UIElement BuiltList(int row)
        {
            var card = new Border();
            try { card.Style = (Style)FindResource("Card"); } catch { }
            card.Padding = new Thickness(6, 6, 6, 6);
            card.Margin = new Thickness(0, 0, 0, 10);

            _list = new ListBox { SelectionMode = SelectionMode.Single, BorderThickness = new Thickness(0) };
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_list, true);
            ScrollViewer.SetCanContentScroll(_list, true);
            try
            {
                GroupStyle gs = (GroupStyle)FindResource("RowGroupStyle");
                if (gs != null) _list.GroupStyle.Add(gs);
            }
            catch (Exception ex) { Logger.Log("Tweak", "分组样式加载失败: " + ex.Message); }
            card.Child = _list;
            Grid.SetRow(card, row);
            return card;
        }

        UIElement BuiltFooter(int row)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _progress = new ProgressBar { Style = (Style)FindResource("ThinProgress"), Height = 4, Minimum = 0, Maximum = 100, Value = 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_progress, 0);
            g.Children.Add(_progress);
            _status = Ui.T("", 11.8, Ui.SubBrush);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(10, 0, 2, 0);
            Grid.SetColumn(_status, 1);
            g.Children.Add(_status);
            Grid.SetRow(g, row);
            return g;
        }

        UIElement BuiltLog(int row)
        {
            _log = new TextBox { Style = (Style)FindResource("LogBox"), Height = 108, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(_log, row);
            return _log;
        }

        // ── 数据 ──
        void Reload()
        {
            if (_all.Count == 0)
            {
                foreach (OneOp op in TweakService.All) _all.Add(new TweakItemVM(op));
            }
            _all.Sort(delegate(TweakItemVM a, TweakItemVM b)
            {
                int g = string.CompareOrdinal(a.Group, b.Group);
                return g != 0 ? g : string.CompareOrdinal(a.Title, b.Title);
            });

            _cvs = new CollectionViewSource { Source = _all };
            _cvs.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            _cvs.Filter += OnFilter;
            _list.ItemsSource = _cvs.View;
            _cvs.View.Refresh();
            UpdateSelection();
        }

        void OnFilter(object sender, FilterEventArgs e)
        {
            var vm = e.Item as TweakItemVM;
            if (vm == null) { e.Accepted = true; return; }
            if (!vm.Matches(_search == null ? null : _search.Text)) { e.Accepted = false; return; }
            int want = _filter == null ? 0 : _filter.SelectedIndex;
            switch (want)
            {
                case 1: e.Accepted = vm.Status == 0; break;
                case 2: e.Accepted = vm.Status == 1; break;
                case 3: e.Accepted = vm.Status == 2; break;
                case 4: e.Accepted = vm.Status == 3; break;
                default: e.Accepted = true; break;
            }
        }

        void ApplyFilter()
        {
            if (_cvs == null) return;
            _cvs.View.Refresh();
            UpdateSelection();
        }

        // ── 选择与预设 ──
        IEnumerable<TweakItemVM> Gather(bool forApply)
        {
            var list = new List<TweakItemVM>();
            foreach (TweakItemVM v in _all)
            {
                if (!v.IsChecked) continue;
                if (v.Status == 3) continue;
                if (forApply && v.Status == 1) continue;
                if (!forApply && v.Status == 0) continue;
                list.Add(v);
            }
            return list;
        }

        void SelectPending()
        {
            foreach (TweakItemVM v in _all) if (v.Status == 0) v.IsChecked = true;
            UpdateSelection();
        }

        void ClearAll()
        {
            foreach (TweakItemVM v in _all) v.IsChecked = false;
            UpdateSelection();
        }

        void Preset(string kind)
        {
            foreach (TweakItemVM v in _all)
            {
                bool on;
                if (v.Risky) on = false;
                else if (kind == "game") on = Hit(v, GameKeys);
                else if (kind == "office") on = Hit(v, OfficeKeys);
                else if (kind == "minimal") on = Hit(v, MinimalKeys);
                else on = true;
                v.IsChecked = on;
            }
            if (_search != null) _search.Text = "";
            if (_filter != null) _filter.SelectedIndex = 0;
            RefreshView();
            UpdateSelection();

            string name = kind == "game" ? "游戏" : kind == "office" ? "办公" : kind == "minimal" ? "极简" : "安全推荐";
            Notify.Info("已应用「" + name + "」预设，请确认后执行优化");
        }

        static bool Hit(TweakItemVM v, string[] keys)
        {
            foreach (string k in keys)
            {
                if (Contains(v.Title, k) || Contains(v.Desc, k) || Contains(v.Tag, k)) return true;
            }
            return false;
        }

        static bool Contains(string src, string k)
        {
            return !string.IsNullOrEmpty(src) && src.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void UpdateSelection()
        {
            if (_selection == null) return;
            int checkedCount = 0, pending = 0;
            foreach (TweakItemVM v in _all)
            {
                if (v.IsChecked) checkedCount++;
                if (v.Status == 0) pending++;
            }
            _selection.Text = "已勾选 " + checkedCount + " 项 · 待优化 " + pending + " 项 · 共 " + _all.Count + " 项";
        }

        void RefreshView()
        {
            if (_cvs != null) _cvs.View.Refresh();
        }

        // ── 执行 ──
        void RescanAll()
        {
            if (_busy) return;
            SetBusy(true);
            Log("正在重新检测全部项目…");
            Ui.RunAsync(delegate
            {
                var results = new List<int>();
                foreach (TweakItemVM v in _all)
                {
                    int st = 3;
                    try { st = (v.Op.Supported != null && !v.Op.Supported()) ? 3 : (v.Op.Detect != null ? v.Op.Detect() : 3); }
                    catch { st = 3; }
                    results.Add(st);
                }
                Post(delegate
                {
                    for (int i = 0; i < _all.Count; i++) _all[i].SetStatus(results[i]);
                    SetBusy(false);
                    RefreshView();
                    UpdateSelection();
                    Log("检测完成：" + _all.Count + " 项");
                });
            });
        }

        void RunBatch(IEnumerable<TweakItemVM> items, bool apply)
        {
            if (_busy) return;
            var sel = new List<TweakItemVM>(items);
            if (sel.Count == 0)
            {
                Notify.Info(apply ? "没有需要执行的优化项（已勾选项均已优化或不支持）" : "没有可还原的项目");
                return;
            }

            int needAdmin = 0;
            foreach (TweakItemVM v in sel) if (v.NeedsAdmin) needAdmin++;

            string msg = (apply ? "将执行 " : "将还原 ") + sel.Count + " 项设置。";
            if (needAdmin > 0 && !OS.IsElevated) msg += "\n其中 " + needAdmin + " 项需要管理员权限，未提权时会被跳过。";
            msg += "\n\n所有改动都会先自动备份，可在“备份还原中心”恢复。是否继续？";
            if (MessageBox.Show(msg, apply ? "一键优化" : "还原设置",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            SetBusy(true);
            Log(apply ? "── 开始应用优化 ──" : "── 开始还原设置 ──");
            Ui.RunAsync(delegate
            {
                int ok = 0, fail = 0, skip = 0;
                for (int i = 0; i < sel.Count; i++)
                {
                    TweakItemVM vm = sel[i];
                    int idx = i;
                    if (vm.NeedsAdmin && !OS.IsElevated)
                    {
                        skip++;
                        Post(delegate { Log("跳过  " + vm.Title + "（需要管理员权限）"); });
                    }
                    else
                    {
                        string err;
                        try { err = apply ? RunOp(vm, true) : RunOp(vm, false); }
                        catch (Exception ex) { err = ex.Message; }
                        if (err == null)
                        {
                            ok++;
                            Post(delegate { Log((apply ? "完成  " : "还原  ") + vm.Title); vm.Rescan(); });
                        }
                        else
                        {
                            fail++;
                            string e = err;
                            Post(delegate { Log("失败  " + vm.Title + "：" + e, true); });
                        }
                    }
                    int pct = (int)((idx + 1) * 100.0 / sel.Count);
                    Post(delegate
                    {
                        _progress.Value = pct;
                        _status.Text = (idx + 1) + " / " + sel.Count;
                    });
                }
                Post(delegate
                {
                    SetBusy(false);
                    _progress.Value = 0;
                    _status.Text = "";
                    Log(string.Format("── 完成：成功 {0} · 失败 {1} · 跳过 {2} ──", ok, fail, skip), fail > 0);
                    if (fail > 0) Notify.Warning("完成，其中 " + fail + " 项失败，详见日志");
                    else if (skip > 0) Notify.Info("完成，跳过 " + skip + " 项（需要管理员）");
                    else Notify.Success(string.Format("已完成 {0} 项设置", ok));
                    RefreshView();
                    UpdateSelection();
                });
            });
        }

        static string RunOp(TweakItemVM vm, bool apply)
        {
            OneOp op = vm.Op;
            if (apply) return op.Apply != null ? op.Apply() : null;
            return op.Restore != null ? op.Restore() : null;
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            if (_applyBtn != null) _applyBtn.IsEnabled = !busy;
            if (_restoreBtn != null) _restoreBtn.IsEnabled = !busy;
        }

        void Log(string line, bool err)
        {
            Ui.AppendLog(_log, (err ? "[!] " : "") + line);
        }

        void Log(string line) { Log(line, false); }

        void Post(Action a) { Dispatcher.BeginInvoke(a); }
    }
}