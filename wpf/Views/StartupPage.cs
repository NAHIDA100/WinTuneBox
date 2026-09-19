using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class StartupPage : UserControl
    {
        List<StartupEntry> _all = new List<StartupEntry>();
        bool _built;

        public StartupPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Load(); } };
        }

        void Load()
        {
            Summary.Text = "正在扫描启动项（含计划任务，可能需要数秒）…";
            Ui.RunAsync(delegate
            {
                List<StartupEntry> data;
                try { data = StartupService.GetAll(); }
                catch (Exception ex) { Dispatcher.Invoke(delegate { Summary.Text = "加载失败: " + ex.Message; }); return; }
                _all = data;
                Dispatcher.Invoke(delegate
                {
                    ApplyFilter();
                    int en = _all.Count(delegate(StartupEntry e) { return e.Enabled; });
                    Summary.Text = string.Format("共 {0} 项：启用 {1}，禁用 {2}", _all.Count, en, _all.Count - en);
                });
            });
        }

        void ApplyFilter()
        {
            string kw = (Search.Text ?? "").Trim();
            IEnumerable<StartupEntry> view = _all;
            if (!string.IsNullOrEmpty(kw) && kw != "输入关键字过滤…")
                view = _all.Where(delegate(StartupEntry e)
                {
                    return (e.Name != null && e.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (e.Command != null && e.Command.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (e.Loc != null && e.Loc.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                });
            List.ItemsSource = view.ToList();
        }

        void Search_Changed(object sender, TextChangedEventArgs e) { if (_built) ApplyFilter(); }

        void Refresh_Click(object sender, RoutedEventArgs e) { Load(); }

        void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var it = (sender as FrameworkElement).DataContext as StartupEntry;
            if (it == null) return;
            bool target = !it.Enabled;
            Ui.RunAsync(delegate
            {
                string err;
                try { err = StartupService.Enable(it, target); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (err == null) { it.Enabled = target; ApplyFilter(); Notify.Success((target ? "已启用: " : "已禁用: ") + it.Name); }
                    else Notify.Warning("操作失败: " + err);
                });
            });
        }

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            var it = (sender as FrameworkElement).DataContext as StartupEntry;
            if (it == null) return;
            if (MessageBox.Show("确定删除启动项“" + it.Name + "”？\n删除前会自动备份。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Ui.RunAsync(delegate
            {
                string r;
                try { r = StartupService.Delete(it); }
                catch (Exception ex) { r = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (r != null && r.StartsWith("计划任务")) { Notify.Warning(r); return; }
                    Notify.Success(r ?? "已删除");
                    Load();
                });
            });
        }

        void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*", Title = "选择要开机启动的程序" };
            if (dlg.ShowDialog() != true) return;
            string exe = dlg.FileName;
            string name = System.IO.Path.GetFileNameWithoutExtension(exe);
            Ui.RunAsync(delegate
            {
                string err;
                try { err = StartupService.Add(name, exe, false, false); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (err == null) { Notify.Success("已添加启动项: " + name); Load(); }
                    else Notify.Warning("添加失败: " + err);
                });
            });
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            StartupService.OpenFolder(false);
        }
    }
}
