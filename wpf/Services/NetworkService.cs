using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTune.Wpf
{
    public class AdapterInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Ip { get; set; }
        public string Dns { get; set; }
        public string Gateway { get; set; }
        public bool Dhcp { get; set; }
        public bool RealConnected { get; set; }  // 有网关且非 APIPA
        public long SpeedMbps { get; set; }
        public string Description { get; set; }
    }

    public class DnsPreset
    {
        public string Name { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string Note { get; set; }
        public DnsPreset(string n, string p, string s, string note) { Name = n; Primary = p; Secondary = s; Note = note; }
    }

    public static class NetworkService
    {
        public static List<DnsPreset> Presets()
        {
            return new List<DnsPreset>
            {
                new DnsPreset("阿里 DNS", "223.5.5.5", "223.6.6.6", "国内快速稳定"),
                new DnsPreset("腾讯 DNS", "119.29.29.29", "182.254.116.116", "DNSPod 公共解析"),
                new DnsPreset("114 DNS", "114.114.114.114", "114.114.115.115", "老牌公共 DNS"),
                new DnsPreset("字节 DNS", "180.184.1.1", "180.184.2.2", "ByteDance 公共 DNS"),
                new DnsPreset("百度 DNS", "180.76.76.76", "", "百度云公共 DNS"),
                new DnsPreset("OneDNS", "117.50.11.11", "117.50.22.22", "带恶意域名拦截"),
                new DnsPreset("Google", "8.8.8.8", "8.8.4.4", "国外，国内可能较慢"),
                new DnsPreset("Cloudflare", "1.1.1.1", "1.0.0.1", "国外，隐私优先"),
            };
        }

        public static List<AdapterInfo> Adapters()
        {
            var list = new List<AdapterInfo>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel &&
                        ni.Name.IndexOf("isatap", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var a = new AdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        SpeedMbps = ni.Speed / 1000000L,
                    };
                    bool up = ni.OperationalStatus == OperationalStatus.Up;
                    try
                    {
                        var props = ni.GetIPProperties();
                        foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                        {
                            if (u.Address.AddressFamily == AddressFamily.InterNetwork && a.Ip == null)
                                a.Ip = u.Address.ToString();
                        }
                        try { a.Dhcp = props.GetIPv4Properties().IsDhcpEnabled; } catch { }
                        var gw = new List<string>();
                        foreach (var g in props.GatewayAddresses)
                        {
                            if (g.Address.AddressFamily == AddressFamily.InterNetwork) gw.Add(g.Address.ToString());
                        }
                        a.Gateway = string.Join(", ", gw.ToArray());
                        var dnsList = new List<string>();
                        foreach (IPAddress d in props.DnsAddresses)
                            if (d.AddressFamily == AddressFamily.InterNetwork) dnsList.Add(d.ToString());
                        a.Dns = string.Join(", ", dnsList.ToArray());
                        bool apipa = a.Ip != null && a.Ip.StartsWith("169.254.");
                        a.RealConnected = up && !apipa && gw.Count > 0;
                        a.Status = !up ? "已断开" : (apipa ? "未联网(APIPA)" : (gw.Count > 0 ? "已连接" : "已连接(无网关)"));
                    }
                    catch
                    {
                        a.Status = up ? "已连接" : "已断开";
                    }
                    list.Add(a);
                }
                list.Sort(delegate(AdapterInfo x, AdapterInfo y) { return y.RealConnected.CompareTo(x.RealConnected); });
            }
            catch { }
            return list;
        }

        /// <summary>netsh 设置 DNS（立即生效，需管理员）。servers 为 null/空=恢复 DHCP 自动获取</summary>
        public static string SetDnsViaNetsh(string adapterName, string[] servers)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            try
            {
                string n = adapterName;
                if (servers == null || servers.Length == 0 || string.IsNullOrEmpty(servers[0]))
                {
                    string r1 = Runner.Run("netsh.exe", "interface ip set dns name=\"" + n + "\" source=dhcp", 15000);
                    string r2 = Runner.Run("netsh.exe", "interface ip set wins name=\"" + n + "\" source=dhcp", 15000);
                    FlushDns();
                    return r1;
                }
                string first = Runner.Run("netsh.exe", "interface ip set dns name=\"" + n + "\" static " + servers[0] + " primary", 15000);
                if (first != null) return first;
                if (servers.Length > 1 && !string.IsNullOrEmpty(servers[1]))
                {
                    string second = Runner.Run("netsh.exe", "interface ip add dns name=\"" + n + "\" " + servers[1] + " index=2", 15000);
                    if (second != null) return second;
                }
                FlushDns();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>注册表方式设置 DNS（旧逻辑，备选）</summary>
        public static string SetDnsRegistry(string ifaceId, string servers)
        {
            string keyPath = Regs.HK_TCPIP + "\\{" + ifaceId + "}";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, true))
                {
                    if (k == null) return "找不到该网络接口配置（可能已断开）";
                    if (string.IsNullOrEmpty(servers))
                    {
                        if (k.GetValue("NameServer") != null) k.DeleteValue("NameServer", false);
                        return null;
                    }
                    k.SetValue("NameServer", servers, Microsoft.Win32.RegistryValueKind.String);
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        [DllImport("dnsapi.dll")]
        static extern uint DnsFlushResolverCache();
        public static string FlushDns()
        {
            try { return DnsFlushResolverCache() == 0 ? null : "刷新失败（请以管理员身份运行）"; }
            catch (Exception ex) { return ex.Message; }
        }

        public static string ResetWinsock()
        {
            if (!OS.IsElevated) return "需要管理员权限";
            return Runner.Run("netsh.exe", "winsock reset", 60000);
        }
        public static string ResetTcpIp()
        {
            if (!OS.IsElevated) return "需要管理员权限";
            return Runner.Run("netsh.exe", "int ip reset", 60000);
        }
        public static string RenewIp()
        {
            if (!OS.IsElevated) return "需要管理员权限";
            string a = Runner.Run("ipconfig.exe", "/release", 60000);
            return Runner.Run("ipconfig.exe", "/renew", 60000);
        }

        /// <summary>Ping 延迟测试，返回毫秒；失败返回 -1</summary>
        public static long PingOnce(string host, int timeoutMs)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var opt = new PingOptions(64, true);
                    var data = new byte[32];
                    var reply = ping.Send(host, timeoutMs, data, opt);
                    if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
                    return -1;
                }
            }
            catch { return -1; }
        }

        // ── hosts ──
        public static string HostsFile { get { return Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\drivers\etc\hosts"; } }
        public static string ReadHosts()
        {
            try { return File.ReadAllText(HostsFile, Encoding.UTF8); }
            catch { try { return File.ReadAllText(HostsFile, Encoding.Default); } catch (Exception ex) { return "读取失败: " + ex.Message; } }
        }
        public static string SaveHosts(string content)
        {
            if (!OS.IsElevated) return "需要管理员权限才能写入 hosts";
            try
            {
                string bak = Path.Combine(AppPaths.BackupDir, "hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                try { File.Copy(HostsFile, bak, true); } catch { }
                File.WriteAllText(HostsFile, content, new UTF8Encoding(false));
                FlushDns();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        const string BLOCK_BEGIN = "# ==== WinTuneBox 遥测拦截（可整体删除） ====";
        const string BLOCK_END = "# ==== WinTuneBox End ====";
        static readonly string[] BlockList = {
            "0.0.0.0 vortex.data.microsoft.com", "0.0.0.0 settings-win.data.microsoft.com",
            "0.0.0.0 watson.telemetry.microsoft.com", "0.0.0.0 telemetry.microsoft.com",
            "0.0.0.0 v10.vortex-win.data.microsoft.com", "0.0.0.0 wns.notify.windows.com.akadns.net",
        };
        public static string ToggleBlock(bool enable)
        {
            string c = ReadHosts();
            int b = c.IndexOf(BLOCK_BEGIN, StringComparison.Ordinal);
            int e = c.IndexOf(BLOCK_END, StringComparison.Ordinal);
            if (enable)
            {
                if (b >= 0) return c;
                var sb = new StringBuilder(c.TrimEnd());
                if (sb.Length > 0 && !sb.ToString().EndsWith("\n")) sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(BLOCK_BEGIN);
                foreach (string line in BlockList) sb.AppendLine(line);
                sb.AppendLine(BLOCK_END);
                return sb.ToString();
            }
            if (b < 0) return c;
            return c.Remove(b, (e >= 0 ? e + BLOCK_END.Length : c.Length) - b);
        }
        public static bool HasBlock() { return ReadHosts().IndexOf(BLOCK_BEGIN, StringComparison.Ordinal) >= 0; }
    }
}
