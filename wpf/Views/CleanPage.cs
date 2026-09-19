using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    /// <summary>
    /// 垃圾清理：后台扫描 / 清理，支持中止；按类目分组展示，实时统计选中项目可释放空间。
    /// </summary>
    public partial class CleanPage : UserControl
    {
        readonly List<CleanItemVM> _all = new List<CleanItemVM>();
        CollectionViewSource _cvs;
        ListBox _list;
        TextBox _log;
        TextBlock _totalText, _countText, _status;
        ProgressBar _progress;
        Button _scanBtn, _abortBtn, _allBtn, _noneBtn, _cleanBtn;
        CancellationTokenSource _cts;
        bool _built, _busy;

        public CleanPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        void Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(Header(0));
            root.Children.Add(Summary(1));
            root.Children.Add(ListCard(2));
            root.Children.Add(LogBox(3));
            Root.Children.Add(root);

            LoadItems();
            Scan();
        }

        UIElement Header(int row)
        {
            var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Ui.PageTitle("垃圾清理"));
            titles.Children.Add(Ui.PageSub("临时文件、缓存、日志、升级残留；被占用或无权限的文件自动跳过"));

            Grid.SetColumn(titles, 0);
            head.Children.Add(titles);

            var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            _scanBtn = Ui.Btn("扫描", "BtnDefault", Scan);
            _abortBtn = Ui.Btn("中止", "BtnGhost", Abort);
            _abortBtn.IsEnabled = false;
            _allBtn = Ui.Btn("全选", "BtnGhost", delegate { SetAll(true); });
            _noneBtn = Ui.Btn("全不选", "BtnGhost", delegate { SetAll(false); });
            _cleanBtn = Ui.Btn("清理选中", "BtnPrimary", Clean);
            actions.Children.Add(_scanBtn);
            actions.Children.Add(Gap(_abortBtn));
            actions.Children.Add(Gap(_allBtn));
            actions.Children.Add(Gap(_noneBtn));
            actions.Children.Add(Gap(_cleanBtn));
            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);
            Ui.ResponsiveHeader(head, titles, actions);

            Grid.SetRow(head, row);
            return head;
        }

        static Button Gap(Button b) { b.Margin = new Thickness(8, 0, 0, 0); return b; }

        UIElement Summary(int row)
        {
            var card = new Border { Padding = new Thickness(18, 11, 18, 11), Margin = new Thickness(0, 0, 0, 10) };
            try { card.Style = (Style)FindResource("Card"); } catch { }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pre = Ui.T("选中项目预计可释放：", 12.5, Ui.SubBrush);
            pre.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(pre);
            _totalText = Ui.T("—", 16, Ui.Brand, true);
            _totalText.VerticalAlignment = VerticalAlignment.Center;
            _totalText.Margin = new Thickness(4, 0, 0, 0);
            left.Children.Add(_totalText);
            _countText = Ui.T("", 12, Ui.SubBrush);
            _countText.VerticalAlignment = VerticalAlignment.Center;
            _countText.Margin = new Thickness(14, 0, 0, 0);
            left.Children.Add(_countText);
            Grid.SetColumn(left, 0);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _status = Ui.T("", 11.8, Ui.SubBrush);
            _status.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(_status);
            _progress = new ProgressBar
            {
                Style = (Style)FindResource("ThinProgress"),
                Width = 200,
                Height = 4,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            right.Children.Add(_progress);
            Grid.SetColumn(right, 1);

            g.Children.Add(left);
            g.Children.Add(right);
            card.Child = g;
            Grid.SetRow(card, row);
            return card;
        }

        UIElement ListCard(int row)
        {
            var card = new Border { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) };
            try { card.Style = (Style)FindResource("Card"); } catch { }

            _list = new ListBox { BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Single };
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_list, true);
            ScrollViewer.SetCanContentScroll(_list, true);
            try
            {
                GroupStyle gs = (GroupStyle)FindResource("RowGroupStyle");
                if (gs != null) _list.GroupStyle.Add(gs);
            }
            catch (Exception ex) { Logger.Log("Clean", "分组样式加载失败: " + ex.Message); }
            card.Child = _list;
            Grid.SetRow(card, row);
            return card;
        }

        UIElement LogBox(int row)
        {
            _log = new TextBox { Style = (Style)FindResource("LogBox"), Height = 96, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(_log, row);
            return _log;
        }

        // ── 数据 ──
        void LoadItems()
        {
            foreach (CleanItem it in CleanService.Defaults())
            {
                var vm = new CleanItemVM(it);
                vm.PropertyChanged += OnItemChanged;
                _all.Add(vm);
            }
            _all.Sort(delegate(CleanItemVM a, CleanItemVM b)
            {
                int g = a.GroupOrder.CompareTo(b.GroupOrder);
                return g != 0 ? g : string.CompareOrdinal(a.Name, b.Name);
            });

            _cvs = new CollectionViewSource { Source = _all };
            _cvs.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            _list.ItemsSource = _cvs.View;
            UpdateSelection();
        }

        void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsChecked") UpdateSelection();
        }

        // ── 选择 ──
        void SetAll(bool on)
        {
            foreach (CleanItemVM vm in _all) vm.IsChecked = on;
            UpdateSelection();
        }

        void UpdateSelection()
        {
            if (_totalText == null) return;
            long sum = 0;
            int n = 0, adminCount = 0;
            foreach (CleanItemVM vm in _all)
            {
                if (!vm.IsChecked) continue;
                n++;
                sum += vm.Item.Size;
                if (vm.NeedsAdmin && !OS.IsElevated) adminCount++;
            }
            _totalText.Text = sum > 0 ? CleanService.FormatSize(sum) : "—";
            _countText.Text = "已选 " + n + " / " + _all.Count + " 项" + (adminCount > 0 ? "（" + adminCount + " 项需管理员）" : "");
        }

        // ── 扫描 / 清理 ──
        void Scan()
        {
            if (_busy) return;
            SetBusy(true, false);
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            Log("开始扫描…");
            int total = _all.Count;
            Ui.RunAsync(delegate
            {
                for (int i = 0; i < total; i++)
                {
                    CleanItemVM vm = _all[i];
                    if (token.IsCancellationRequested) break;
                    try { CleanService.Scan(vm.Item, token); }
                    catch (Exception ex) { Log("扫描异常 " + vm.Name + "：" + ex.Message, true); }
                    int pct = (int)((i + 1) * 100.0 / total);
                    Post(delegate
                    {
                        vm.RefreshFromItem();
                        _progress.Value = pct;
                        _status.Text = (i + 1) + " / " + total;
                        UpdateSelection();
                    });
                }
                Post(delegate
                {
                    SetBusy(false, false);
                    _progress.Value = 0;
                    _status.Text = "";
                    Log(token.IsCancellationRequested ? "扫描已中止" : "扫描完成");
                    UpdateSelection();
                });
            });
        }

        void Clean()
        {
            if (_busy) return;
            var chosen = new List<CleanItemVM>();
            foreach (CleanItemVM vm in _all) if (vm.IsChecked) chosen.Add(vm);
            if (chosen.Count == 0) { Notify.Info("请先勾选要清理的项目"); return; }

            bool irreversible = false;
            long estimate = 0;
            foreach (CleanItemVM vm in chosen)
            {
                estimate += vm.Item.Size;
                string k = vm.Item.Kind;
                if (k == "recycle" || k == "windowsold") irreversible = true;
            }

            string msg = "将清理 " + chosen.Count + " 项，预计释放 " + CleanService.FormatSize(estimate) + "。";
            if (irreversible) msg += "\n\n其中包含回收站 / Windows.old，删除后无法恢复。";
            msg += "\n\n是否继续？";
            MessageBoxResult r = MessageBox.Show(msg, irreversible ? "不可恢复操作确认" : "清理确认",
                MessageBoxButton.YesNo, irreversible ? MessageBoxImage.Warning : MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            SetBusy(true, true);
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            Log("── 开始清理 " + chosen.Count + " 项 ──");
            Ui.RunAsync(delegate
            {
                for (int i = 0; i < chosen.Count; i++)
                {
                    CleanItemVM vm = chosen[i];
                    if (token.IsCancellationRequested) break;
                    CleanItem item = vm.Item;
                    Post(delegate { vm.Busy = true; _status.Text = "正在清理：" + vm.Name; });
                    Log("正在清理：" + item.Name);
                    try
                    {
                        CleanService.Clean(item, delegate(string m, bool e) { Log(m, e); }, token);
                    }
                    catch (Exception ex) { Log("清理异常 " + item.Name + "：" + ex.Message, true); }
                    item.Size = 0;
                    item.Files = 0;
                    int pct = (int)((i + 1) * 100.0 / chosen.Count);
                    Post(delegate
                    {
                        vm.Busy = false;
                        vm.RefreshFromItem();
                        _progress.Value = pct;
                    });
                }

                Post(delegate
                {
                    _status.Text = "重新统计中…";
                });
                for (int i = 0; i < chosen.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    CleanItemVM vm = chosen[i];
                    try { CleanService.Scan(vm.Item, token); } catch { }
                    Post(delegate { vm.RefreshFromItem(); UpdateSelection(); });
                }

                Post(delegate
                {
                    SetBusy(false, true);
                    _progress.Value = 0;
                    _status.Text = "";
                    Log(token.IsCancellationRequested ? "── 清理已中止 ──" : "── 清理完成 ──");
                    UpdateSelection();
                    Notify.Success(token.IsCancellationRequested ? "清理已中止" : "清理完成");
                });
            });
        }

        void Abort()
        {
            if (_cts != null && _busy)
            {
                try { _cts.Cancel(); } catch { }
                Log("已请求中止，正在等待当前任务结束…");
            }
        }

        void SetBusy(bool busy, bool allowAbort)
        {
            _busy = busy;
            if (_scanBtn != null) _scanBtn.IsEnabled = !busy;
            if (_cleanBtn != null) _cleanBtn.IsEnabled = !busy;
            if (_allBtn != null) _allBtn.IsEnabled = !busy;
            if (_noneBtn != null) _noneBtn.IsEnabled = !busy;
            if (_abortBtn != null) _abortBtn.IsEnabled = busy && allowAbort;
            if (_progress != null) _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        void Log(string msg, bool err)
        {
            Ui.AppendLog(_log, (err ? "[!] " : "") + msg);
        }

        void Log(string msg) { Log(msg, false); }

        void Post(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }
    }
}