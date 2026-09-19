using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class ServicePage : UserControl
    {
        bool _built;
        public ServicePage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Scan(); } };
        }

        void Scan()
        {
            Summary.Text = "正在分析服务…";
            Ui.RunAsync(delegate
            {
                List<ServiceManager.SvcView> items;
                try { items = ServiceManager.Evaluate(); }
                catch (Exception ex) { Dispatcher.Invoke(delegate { Summary.Text = "分析失败: " + ex.Message; }); return; }
                Dispatcher.Invoke(delegate
                {
                    List.ItemsSource = items;
                    Summary.Text = string.Format("命中 {0} 项可优化建议；已有 {1} 项改动可还原", items.Count, ServiceManager.UndoCount());
                });
            });
        }

        void Refresh_Click(object sender, RoutedEventArgs e) { Scan(); }

        void ApplyOne(ServiceManager.SvcView it)
        {
            if (!OS.IsElevated) { Notify.Warning("修改服务需要管理员权限，请先在左下角提权"); return; }
            Ui.RunAsync(delegate
            {
                string err;
                try { err = ServiceManager.SetStartType(it.Name, it.Target, true); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (err == null) { Notify.Success("已将 " + it.Display + " 设为" + it.RecStart); Scan(); }
                    else Notify.Warning("设置失败: " + err);
                });
            });
        }

        void ApplyOne_Click(object sender, RoutedEventArgs e)
        {
            var it = (sender as FrameworkElement).DataContext as ServiceManager.SvcView;
            if (it != null) ApplyOne(it);
        }

        void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var items = List.ItemsSource as List<ServiceManager.SvcView>;
            if (items == null || items.Count == 0) { Notify.Info("没有可应用的建议"); return; }
            if (!OS.IsElevated) { Notify.Warning("批量修改服务需要管理员权限，请先在左下角提权"); return; }
            if (MessageBox.Show(string.Format("将对 {0} 项服务应用推荐配置，是否继续？\n（可随时在此页“全部还原”）", items.Count),
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Ui.RunAsync(delegate
            {
                int ok = 0; string firstErr = null;
                foreach (var it in items)
                {
                    string err = null;
                    try { err = ServiceManager.SetStartType(it.Name, it.Target, true); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err == null) ok++; else if (firstErr == null) firstErr = it.Name + ": " + err;
                }
                Dispatcher.Invoke(delegate
                {
                    Notify.Success(string.Format("已应用 {0}/{1} 项", ok, items.Count));
                    if (firstErr != null) Notify.Warning("部分失败，例如 " + firstErr);
                    Scan();
                });
            });
        }

        void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (ServiceManager.UndoCount() == 0) { Notify.Info("没有可还原的服务改动"); return; }
            if (!OS.IsElevated) { Notify.Warning("还原服务需要管理员权限，请先提权"); return; }
            if (MessageBox.Show("将把服务恢复到优化前的启动类型，是否继续？", "确认还原",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Ui.RunAsync(delegate
            {
                string r;
                try { r = ServiceManager.UndoAll(); }
                catch (Exception ex) { r = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (r == null) Notify.Success("已全部还原");
                    else if (r.StartsWith("已还原")) Notify.Success(r);
                    else Notify.Warning(r);
                    Scan();
                });
            });
        }
    }
}
