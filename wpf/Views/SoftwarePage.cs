using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class SoftwarePage : UserControl
    {
        List<SoftInfo> _all = new List<SoftInfo>();
        bool _built;

        public SoftwarePage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; LoadDesktop(); } };
        }

        void LoadDesktop()
        {
            Summary.Text = "正在读取已安装程序…";
            Ui.RunAsync(delegate
            {
                List<SoftInfo> data;
                try { data = SoftwareService.ReadAll(); }
                catch (Exception ex) { Dispatcher.Invoke(delegate { Summary.Text = "读取失败: " + ex.Message; }); return; }
                _all = data;
                Dispatcher.Invoke(delegate { ApplyFilter(); Summary.Text = "桌面程序 " + data.Count + " 个"; });
            });
        }

        void LoadAppx()
        {
            AppxBtn.IsEnabled = false;
            Summary.Text = "正在读取应用商店应用（PowerShell，约数秒）…";
            Ui.RunAsync(delegate
            {
                List<SoftInfo> data;
                try { data = SoftwareService.ReadAppx(); }
                catch (Exception ex) { Dispatcher.Invoke(delegate { Summary.Text = "读取失败: " + ex.Message; AppxBtn.IsEnabled = true; }); return; }
                Dispatcher.Invoke(delegate
                {
                    foreach (var a in data) if (!_all.Any(delegate(SoftInfo x) { return string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase); })) _all.Add(a);
                    ApplyFilter();
                    Summary.Text = "已加入 " + data.Count + " 个商店应用，共 " + _all.Count + " 项";
                    AppxBtn.IsEnabled = true;
                });
            });
        }

        void ApplyFilter()
        {
            string kw = (Search.Text ?? "").Trim();
            IEnumerable<SoftInfo> view = _all;
            if (!string.IsNullOrEmpty(kw))
                view = _all.Where(delegate(SoftInfo s)
                {
                    return (s.Name != null && s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (s.Publisher != null && s.Publisher.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                });
            List.ItemsSource = view.ToList();
        }

        void Search_Changed(object sender, TextChangedEventArgs e) { if (_built) ApplyFilter(); }
        void Refresh_Click(object sender, RoutedEventArgs e) { _all = new List<SoftInfo>(); LoadDesktop(); }
        void Appx_Click(object sender, RoutedEventArgs e) { LoadAppx(); }

        void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var s = (sender as FrameworkElement).DataContext as SoftInfo;
            if (s == null) return;
            if (s.StoreApp)
            {
                if (MessageBox.Show("确定卸载应用“" + s.Name + "”？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                Ui.RunAsync(delegate
                {
                    string r = SoftwareService.UninstallAppx(s.PackageFullName);
                    Dispatcher.Invoke(delegate
                    {
                        if (string.IsNullOrEmpty(r)) { Notify.Success("已发起卸载: " + s.Name); }
                        else Notify.Warning("卸载失败: " + r);
                    });
                });
                return;
            }
            if (MessageBox.Show("将启动“" + s.Name + "”的卸载程序，是否继续？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                bool quiet = !string.IsNullOrEmpty(s.Quiet);
                string cmd = SoftwareService.BuildUninstallCmd(s);
                LaunchUninstall(cmd);
                Notify.Info("已启动卸载程序");
            }
            catch (Exception ex) { Notify.Warning("无法启动卸载: " + ex.Message); }
        }

        static void LaunchUninstall(string cmd)
        {
            cmd = cmd.Trim();
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("\"msiexec", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "msiexec.exe";
                psi.Arguments = cmd.Replace("msiexec.exe", "").Replace("msiexec", "").Trim();
            }
            else if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0)
                {
                    psi.FileName = cmd.Substring(1, end - 1);
                    psi.Arguments = cmd.Substring(end + 1).Trim();
                }
                else { psi.FileName = "cmd.exe"; psi.Arguments = "/c " + cmd; }
            }
            else
            {
                int sp = cmd.IndexOf(' ');
                if (sp > 0 && File.Exists(cmd.Substring(0, sp))) { psi.FileName = cmd.Substring(0, sp); psi.Arguments = cmd.Substring(sp + 1); }
                else { psi.FileName = "cmd.exe"; psi.Arguments = "/c " + cmd; }
            }
            Process.Start(psi);
        }

        void Folder_Click(object sender, RoutedEventArgs e)
        {
            var s = (sender as FrameworkElement).DataContext as SoftInfo;
            if (s == null) return;
            if (s.StoreApp) { Notify.Info("商店应用为系统托管，无独立安装目录"); return; }
            try { SoftwareService.OpenInstallFolder(s); }
            catch (Exception ex) { Notify.Warning("无法打开目录: " + ex.Message); }
        }
    }
}
