using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class SnapshotPage : UserControl
    {
        bool _built;
        public SnapshotPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; RefreshList(); } };
        }

        void LogLine(string m, bool err)
        {
            Dispatcher.Invoke(delegate
            {
                Log.AppendText(string.Format("[{0}] {1}\n", DateTime.Now.ToString("HH:mm:ss"), m));
                Log.ScrollToEnd();
            });
        }

        void Create_Click(object sender, RoutedEventArgs e)
        {
            bool withRp = WithRestorePoint.IsChecked == true;
            if (withRp && !OS.IsElevated)
            {
                Notify.Info("未提权，将只创建注册表/服务快照（系统还原点需管理员）");
                withRp = false;
            }
            CreateBtn.IsEnabled = false;
            Ui.RunAsync(delegate
            {
                string r;
                try { r = SnapshotService.CreateSnapshot(withRp, LogLine); }
                catch (Exception ex) { r = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    CreateBtn.IsEnabled = true;
                    RefreshList();
                    if (r == null) Notify.Success("快照创建完成"); else Notify.Warning(r);
                });
            });
        }

        void RefreshList()
        {
            List.ItemsSource = SnapshotService.List();
        }
        void Refresh_Click(object sender, RoutedEventArgs e) { RefreshList(); }

        void Restore_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement).DataContext as SnapshotInfo;
            if (info == null) return;
            if (!OS.IsElevated) { Notify.Warning("恢复快照需要管理员权限，请先在左下角提权"); return; }
            if (MessageBox.Show("将把注册表分支与服务配置恢复到 " + info.Display + " 的状态，是否继续？\n建议先关闭其他程序，恢复后重启资源管理器或重启电脑。",
                "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Ui.RunAsync(delegate
            {
                string r;
                try { r = SnapshotService.Restore(info, LogLine); }
                catch (Exception ex) { r = ex.Message; }
                Dispatcher.Invoke(delegate { if (r == null) Notify.Success("快照已恢复"); else Notify.Warning(r); });
            });
        }

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement).DataContext as SnapshotInfo;
            if (info == null) return;
            if (MessageBox.Show("确定删除快照 " + info.Display + "？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SnapshotService.Delete(info);
            RefreshList();
            Notify.Info("已删除");
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as FrameworkElement).DataContext as SnapshotInfo;
            if (info != null) OpenDir(info.Dir);
        }
        void OpenSnapshotDir_Click(object sender, RoutedEventArgs e) { OpenDir(AppPaths.SnapshotDir); }
        void OpenBackupDir_Click(object sender, RoutedEventArgs e) { OpenDir(AppPaths.BackupDir); }

        static void OpenDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Notify.Warning("打开目录失败: " + ex.Message); }
        }
    }
}
