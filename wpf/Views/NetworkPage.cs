using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class NetworkPage : UserControl
    {
        bool _built;
        public NetworkPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Init(); } };
        }

        void Init()
        {
            DnsPreset.ItemsSource = NetworkService.Presets();
            DnsPreset.DisplayMemberPath = "Name";
            LoadAdapters();
            ReadHosts();
            BlockToggle.IsChecked = NetworkService.HasBlock();
        }

        void LoadAdapters()
        {
            Ui.RunAsync(delegate
            {
                var list = NetworkService.Adapters();
                Dispatcher.Invoke(delegate
                {
                    AdapterList.ItemsSource = list;
                    var real = list.FirstOrDefault(delegate(AdapterInfo a) { return a.RealConnected; });
                    if (real != null) AdapterList.SelectedItem = real;
                    else if (list.Count > 0) AdapterList.SelectedIndex = 0;
                });
            });
        }

        void RefreshAdapters_Click(object sender, RoutedEventArgs e) { LoadAdapters(); }

        AdapterInfo SelectedAdapter() { return AdapterList.SelectedItem as AdapterInfo; }

        void ApplyDns_Click(object sender, RoutedEventArgs e)
        {
            var a = SelectedAdapter();
            var preset = DnsPreset.SelectedItem as DnsPreset;
            if (a == null) { Notify.Warning("请先选择网卡"); return; }
            if (preset == null) { Notify.Warning("请选择 DNS 预设"); return; }
            if (!OS.IsElevated) { Notify.Warning("设置 DNS 需要管理员权限，请先在左下角提权"); return; }
            string[] servers = string.IsNullOrEmpty(preset.Secondary)
                ? new[] { preset.Primary }
                : new[] { preset.Primary, preset.Secondary };
            Ui.RunAsync(delegate
            {
                string r = NetworkService.SetDnsViaNetsh(a.Name, servers);
                Dispatcher.Invoke(delegate
                {
                    if (r == null) { Notify.Success(preset.Name + " DNS 已应用到 " + a.Name); LoadAdapters(); }
                    else Notify.Warning("设置失败: " + r);
                });
            });
        }

        void DhcpDns_Click(object sender, RoutedEventArgs e)
        {
            var a = SelectedAdapter();
            if (a == null) { Notify.Warning("请先选择网卡"); return; }
            if (!OS.IsElevated) { Notify.Warning("需要管理员权限，请先提权"); return; }
            Ui.RunAsync(delegate
            {
                string r = NetworkService.SetDnsViaNetsh(a.Name, null);
                Dispatcher.Invoke(delegate
                {
                    if (r == null) { Notify.Success(a.Name + " 已恢复自动获取 DNS"); LoadAdapters(); }
                    else Notify.Warning("操作失败: " + r);
                });
            });
        }

        void Ping_Click(object sender, RoutedEventArgs e)
        {
            string host = (PingHost.Text ?? "").Trim();
            if (string.IsNullOrEmpty(host)) return;
            PingResult.Text = "Ping 中…";
            Ui.RunAsync(delegate
            {
                int ok = 0; long sum = 0, min = long.MaxValue, max = 0;
                for (int i = 0; i < 4; i++)
                {
                    long ms = NetworkService.PingOnce(host, 3000);
                    if (ms >= 0) { ok++; sum += ms; if (ms < min) min = ms; if (ms > max) max = ms; }
                }
                Dispatcher.Invoke(delegate
                {
                    PingResult.Text = ok == 0 ? "全部超时/不可达"
                        : string.Format("收到 {0}/4，平均 {1:F0}ms（{2}~{3}ms）", ok, (double)sum / ok, min, max);
                });
            });
        }

        void FlushDns_Click(object sender, RoutedEventArgs e)
        {
            string r = NetworkService.FlushDns();
            if (r == null) Notify.Success("DNS 缓存已刷新"); else Notify.Warning(r);
        }

        void Renew_Click(object sender, RoutedEventArgs e)
        {
            if (!OS.IsElevated) { Notify.Warning("需要管理员权限，请先提权"); return; }
            Ui.RunAsync(delegate
            {
                string r = NetworkService.RenewIp();
                Dispatcher.Invoke(delegate { Notify.Info(r == null ? "已释放并续租 IP" : ("输出: " + r)); LoadAdapters(); });
            });
        }

        void Winsock_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmReset("重置 Winsock 会恢复网络组件目录，通常需要重启，是否继续？")) return;
            Ui.RunAsync(delegate
            {
                string r = NetworkService.ResetWinsock();
                Dispatcher.Invoke(delegate { Notify.Info(r == null ? "Winsock 已重置，建议重启电脑" : r); });
            });
        }

        void TcpReset_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmReset("重置 TCP/IP 协议栈会重写网络配置，通常需要重启，是否继续？")) return;
            Ui.RunAsync(delegate
            {
                string r = NetworkService.ResetTcpIp();
                Dispatcher.Invoke(delegate { Notify.Info(r == null ? "TCP/IP 已重置，建议重启电脑" : r); });
            });
        }

        bool ConfirmReset(string msg)
        {
            if (!OS.IsElevated) { Notify.Warning("需要管理员权限，请先在左下角提权"); return true; }
            return MessageBox.Show(msg, "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
        }

        void ReadHosts() { HostsBox.Text = NetworkService.ReadHosts(); }
        void ReadHosts_Click(object sender, RoutedEventArgs e) { ReadHosts(); Notify.Info("已重新读取 hosts"); }

        void SaveHosts_Click(object sender, RoutedEventArgs e)
        {
            if (!OS.IsElevated) { Notify.Warning("保存 hosts 需要管理员权限，请先提权"); return; }
            string r = NetworkService.SaveHosts(HostsBox.Text);
            if (r == null) Notify.Success("hosts 已保存并刷新 DNS"); else Notify.Warning("保存失败: " + r);
        }

        bool _syncingBlock;
        void Block_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingBlock) return;
            bool on = BlockToggle.IsChecked == true;
            if (!OS.IsElevated)
            {
                Notify.Warning("修改 hosts 需要管理员权限，请先提权");
                _syncingBlock = true; BlockToggle.IsChecked = !on; _syncingBlock = false;
                return;
            }
            string content = NetworkService.ToggleBlock(on);
            string r = NetworkService.SaveHosts(content);
            if (r == null) { HostsBox.Text = content; Notify.Success(on ? "已加入遥测拦截规则" : "已移除遥测拦截规则"); }
            else { Notify.Warning("写入失败: " + r); _syncingBlock = true; BlockToggle.IsChecked = !on; _syncingBlock = false; }
        }
    }
}
