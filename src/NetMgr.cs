using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 网络：适配器信息 / DNS 设置 / hosts ═══
    public class AdapterInfo
    {
        public string Id, Name, Status;
        public string Ip;         // 主要 IPv4
        public string Dns;        // 当前 DNS（逗号分隔）
        public bool Dhcp;
    }

    public static class NetMgr
    {
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
                        Status = ni.OperationalStatus == OperationalStatus.Up ? "已连接" : "已断开",
                    };
                    try
                    {
                        var props = ni.GetIPProperties();
                        foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                        {
                            if (u.Address.AddressFamily == AddressFamily.InterNetwork && a.Ip == null)
                                a.Ip = u.Address.ToString();
                        }
                        try
                        {
                            var v4 = props.GetIPv4Properties();
                            a.Dhcp = v4.IsDhcpEnabled;
                        }
                        catch { }
                        var dnsList = new List<string>();
                        foreach (IPAddress d in props.DnsAddresses)
                            if (d.AddressFamily == AddressFamily.InterNetwork)
                                dnsList.Add(d.ToString());
                        a.Dns = string.Join(", ", dnsList.ToArray());
                    }
                    catch { }
                    list.Add(a);
                }
            }
            catch { }
            return list;
        }

        /// <summary>把接口 DNS 设为固定值（写注册表，重连后生效）。servers 逗号分隔；null=恢复自动</summary>
        public static string SetDns(string ifaceId, string servers)
        {
            string keyPath = Regs.HK_TCPIP + "\\{" + ifaceId + "}";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(keyPath, true))
                {
                    if (k == null) return "找不到该网络接口的配置（可能已断开连接）";
                    if (servers == null)
                    {
                        if (k.GetValue("NameServer") != null) k.DeleteValue("NameServer", false);
                        return null;
                    }
                    k.SetValue("NameServer", servers, RegistryValueKind.String);
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

        // ═══ hosts ═══
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
                string bak = App.BackupDir + "\\hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(HostsFile, bak); } catch { }
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

        /// <summary>切换遥测拦截段；返回 (newContent, 已开启?)</summary>
        public static string[] ToggleBlock(bool enable)
        {
            string c = ReadHosts();
            int b = c.IndexOf(BLOCK_BEGIN, StringComparison.Ordinal);
            int e = c.IndexOf(BLOCK_END, StringComparison.Ordinal);
            if (enable)
            {
                if (b >= 0) return new[] { c, "1" };
                var sb = new StringBuilder(c.TrimEnd());
                if (sb.Length > 0 && !sb.ToString().EndsWith("\n")) sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(BLOCK_BEGIN);
                foreach (string line in BlockList) sb.AppendLine(line);
                sb.AppendLine(BLOCK_END);
                return new[] { sb.ToString(), "1" };
            }
            if (b < 0) return new[] { c, "0" };
            string rest = c.Remove(b, (e >= 0 ? e + BLOCK_END.Length : c.Length) - b);
            return new[] { rest, "0" };
        }

        public static bool HasBlock()
        {
            return ReadHosts().IndexOf(BLOCK_BEGIN, StringComparison.Ordinal) >= 0;
        }

        public static string DefaultHosts()
        {
            return "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n# hosts 文件说明（本文件已由 WinTuneBox 还原为默认）\r\n127.0.0.1       localhost\r\n::1             localhost\r\n";
        }
    }
}
