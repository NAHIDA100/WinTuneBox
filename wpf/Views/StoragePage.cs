using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace WinTune.Wpf.Views
{
    /// <summary>
    /// 存储分析：磁盘占用、大目录 / 大文件排行、系统残留与孤儿卸载项扫描（全部只读）。
    /// </summary>
    public partial class StoragePage : UserControl
    {
        const int TopN = 12;

        StackPanel _diskBody, _dirBody, _fileBody, _residueBody, _orphanBody;
        TextBlock _status;
        ProgressBar _progress;
        Button _scanBtn, _abortBtn;
        CancellationTokenSource _cts;
        bool _built, _busy;

        public StoragePage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        void Build()
        {
            StackPanel content; WrapPanel actions;
            Root.Children.Add(Ui.PageRoot("存储分析", "磁盘占用、大目录 / 大文件排行与系统残留（只读扫描，不会删除任何文件）", out content, out actions));

            _scanBtn = Ui.Btn("开始扫描", "BtnPrimary", Scan);
            _abortBtn = Ui.Btn("中止", "BtnGhost", Abort);
            _abortBtn.IsEnabled = false;
            _abortBtn.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(_scanBtn);
            actions.Children.Add(_abortBtn);

            var bar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _progress = new ProgressBar { Style = (Style)FindResource("ThinProgress"), Height = 4, Minimum = 0, Maximum = 100, Value = 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_progress, 0);
            _status = Ui.T("", 11.8, Ui.SubBrush);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(10, 0, 2, 0);
            Grid.SetColumn(_status, 1);
            bar.Children.Add(_progress);
            bar.Children.Add(_status);

            content.Children.Add(bar);
            content.Children.Add(DisksCard());
            content.Children.Add(DirsCard());
            content.Children.Add(FilesCard());
            content.Children.Add(ResidueCard());
            content.Children.Add(OrphanCard());

            Scan();
        }

        UIElement DisksCard()
        {
            var card = Ui.Card("磁盘占用", "所有已就绪的本地与可移动磁盘", out _diskBody);
            return card;
        }

        UIElement DirsCard()
        {
            var card = Ui.Card("大目录排行", "系统盘一级目录占用（受时间预算限制，超时显示已统计值）", out _dirBody);
            return card;
        }

        UIElement FilesCard()
        {
            var card = Ui.Card("大文件排行", "下载 / 桌面 / 文档中的最大文件（Top " + TopN + "）", out _fileBody);
            return card;
        }

        UIElement ResidueCard()
        {
            var card = Ui.Card("系统残留", "旧系统目录、升级临时文件与驱动包占用", out _residueBody);
            return card;
        }

        UIElement OrphanCard()
        {
            var card = Ui.Card("残留与孤儿项", "卸载注册表中指向已不存在目录的条目，以及指向已删除程序的计划任务（只读报告）", out _orphanBody);
            return card;
        }

        // ── 扫描 ──
        void Scan()
        {
            if (_busy) return;
            SetBusy(true);
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _diskBody.Children.Clear();
            _dirBody.Children.Clear();
            _fileBody.Children.Clear();
            _residueBody.Children.Clear();
            _orphanBody.Children.Clear();
            Step("正在读取磁盘信息…", 5);

            Ui.RunAsync(delegate
            {
                // 磁盘
                List<OS.DiskItem> disks = OS.AllDisks();
                Dispatcher.Invoke(delegate
                {
                    if (disks.Count == 0) _diskBody.Children.Add(Ui.T("未检测到可用的固定磁盘", 12, Ui.SubBrush));
                    foreach (OS.DiskItem d in disks) _diskBody.Children.Add(DiskRow(d));
                });
                if (token.IsCancellationRequested) { Done(true); return; }

                // 大目录（系统盘）
                Step("正在统计系统盘目录占用（可能需要十几秒）…", 20);
                string sysRoot = Environment.GetEnvironmentVariable("SystemDrive") + "\\";
                List<StorageEntry> dirs = StorageService.TopDirectories(sysRoot, TopN, 9000, token);
                Dispatcher.Invoke(delegate
                {
                    FillEntries(_dirBody, dirs, "未获取到目录数据（可能无权限）");
                });
                if (token.IsCancellationRequested) { Done(true); return; }

                // 大文件（用户常用目录）
                Step("正在扫描下载 / 桌面 / 文档中的大文件…", 60);
                var files = new List<StorageEntry>();
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] scanDirs = {
                    Path.Combine(home, "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                };
                foreach (string d in scanDirs)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        if (!Directory.Exists(d)) continue;
                        files.AddRange(StorageService.TopFiles(d, TopN, token));
                    }
                    catch (Exception ex) { Logger.Log("Storage", "扫描 " + d + " 失败: " + ex.Message); }
                }
                files.Sort(delegate(StorageEntry a, StorageEntry b) { return b.Size.CompareTo(a.Size); });
                if (files.Count > TopN) files = files.GetRange(0, TopN);
                Dispatcher.Invoke(delegate { FillEntries(_fileBody, files, "未扫描到大文件"); });
                if (token.IsCancellationRequested) { Done(true); return; }

                // 系统残留
                Step("正在检查系统残留…", 78);
                var residue = new List<StorageEntry>();
                AddResidue(residue, sysRoot + "Windows.old", "旧系统备份（删除后无法回退到旧版本）");
                AddResidue(residue, sysRoot + "$WINDOWS.~BT", "系统升级临时目录");
                AddResidue(residue, sysRoot + "$WINDOWS.~WS", "系统升级临时目录");
                AddResidue(residue, Path.Combine(CleanService.W, "SoftwareDistribution", "Download"), "Windows 更新缓存");
                Dispatcher.Invoke(delegate
                {
                    FillEntries(_residueBody, residue, "未发现常见系统残留");
                    string drv = StorageService.DriverStoreInfo();
                    if (!string.IsNullOrEmpty(drv))
                    {
                        _residueBody.Children.Add(Ui.Divider());
                        _residueBody.Children.Add(Ui.InfoRow("驱动包仓库", drv));
                    }
                });
                if (token.IsCancellationRequested) { Done(true); return; }

                // 孤儿项
                Step("正在扫描孤儿卸载项与残留计划任务…", 90);
                List<StorageEntry> orphans = StorageService.FindOrphanUninstallEntries();
                List<StorageEntry> tasks = StorageService.FindLeftoverTasks();
                Dispatcher.Invoke(delegate
                {
                    if (orphans.Count == 0) _orphanBody.Children.Add(Ui.T("未发现孤儿卸载项", 12, Ui.SubBrush));
                    else FillEntries(_orphanBody, orphans, null);
                    _orphanBody.Children.Add(Ui.Divider());
                    _orphanBody.Children.Add(Ui.T("残留计划任务", 12.5, Ui.TextBrush, true));
                    if (tasks.Count == 0)
                    {
                        var none = Ui.T("未发现指向已删除程序的计划任务", 12, Ui.SubBrush);
                        none.Margin = new Thickness(0, 4, 0, 0);
                        _orphanBody.Children.Add(none);
                    }
                    else
                    {
                        foreach (StorageEntry e in tasks) _orphanBody.Children.Add(EntryRow(e, false));
                    }
                });

                Done(false);
            });
        }

        static void AddResidue(List<StorageEntry> list, string path, string note)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                list.Add(new StorageEntry { Path = path, Note = note, Size = StorageService.DirSize(path, CancellationToken.None, 2500) });
            }
            catch { }
        }

        void Done(bool aborted)
        {
            Dispatcher.Invoke(delegate
            {
                SetBusy(false);
                _progress.Value = 0;
                _status.Text = "";
                if (aborted) Notify.Info("扫描已中止");
                else Notify.Success("存储分析完成");
            });
        }

        void Step(string text, double pct)
        {
            Dispatcher.Invoke(delegate
            {
                _status.Text = text;
                _progress.Value = pct;
            });
        }

        void FillEntries(StackPanel body, List<StorageEntry> list, string emptyText)
        {
            body.Children.Clear();
            if (list == null || list.Count == 0)
            {
                if (!string.IsNullOrEmpty(emptyText)) body.Children.Add(Ui.T(emptyText, 12, Ui.SubBrush));
                return;
            }
            long max = 1;
            foreach (StorageEntry e in list) if (e.Size > max) max = e.Size;
            for (int i = 0; i < list.Count; i++) body.Children.Add(EntryRow(list[i], true, max));
        }

        static UIElement EntryRow(StorageEntry e, bool withBar)
        {
            return EntryRow(e, withBar, 0);
        }

        static UIElement EntryRow(StorageEntry e, bool withBar, long max)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var name = Ui.T(Shorten(e.Path), 12.4, Ui.TextBrush);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = e.Path;
            left.Children.Add(name);
            if (!string.IsNullOrEmpty(e.Note))
            {
                var n = Ui.T(e.Note, 11.2, Ui.Tertiary);
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                n.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(n);
            }
            Grid.SetColumn(left, 0);

            var size = Ui.T(e.Size > 0 ? CleanService.FormatSize(e.Size) : "—", 12.4, Ui.SubBrush, true);
            size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 1);

            g.Children.Add(left);
            g.Children.Add(size);

            if (!withBar || max <= 1) return g;

            var wrap = new StackPanel();
            wrap.Children.Add(g);
            var bar = new ProgressBar
            {
                Style = (Style)Application.Current.FindResource("ThinProgress"),
                Height = 3,
                Minimum = 0,
                Maximum = max,
                Value = e.Size,
                Margin = new Thickness(0, 2, 0, 0),
            };
            wrap.Children.Add(bar);
            return wrap;
        }

        static UIElement DiskRow(OS.DiskItem d)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var title = Ui.T(d.Root + "  " + (string.IsNullOrEmpty(d.Label) ? "" : "（" + d.Label + "）"), 12.8, Ui.TextBrush, true);
            left.Children.Add(title);

            var bar = new ProgressBar
            {
                Style = (Style)Application.Current.FindResource("ThinProgress"),
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Value = d.UsedPercent,
                Margin = new Thickness(0, 6, 0, 0),
            };
            left.Children.Add(bar);
            Grid.SetColumn(left, 0);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var used = Ui.T(d.UsedPercent + "% 已用", 12.2, Ui.SubBrush, true);
            used.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(used);
            var free = Ui.T(string.Format("可用 {0:F1} / {1:F1} GB", d.FreeGB, d.SizeGB), 11.2, Ui.Tertiary);
            free.HorizontalAlignment = HorizontalAlignment.Right;
            free.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(free);
            Grid.SetColumn(right, 1);

            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                return "%USERPROFILE%" + path.Substring(home.Length);
            return path;
        }

        void Abort()
        {
            if (_cts != null && _busy)
            {
                try { _cts.Cancel(); } catch { }
                Notify.Info("已请求中止扫描");
            }
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            if (_scanBtn != null) _scanBtn.IsEnabled = !busy;
            if (_abortBtn != null) _abortBtn.IsEnabled = busy;
            if (_progress != null) _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}