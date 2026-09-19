using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public class SoftInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string InstallDate { get; set; }
        public string Uninstall { get; set; }
        public string Quiet { get; set; }
        public string Location { get; set; }
        public string From { get; set; }
        public double SizeMB { get; set; }
        public bool StoreApp { get; set; }
        public string PackageFullName { get; set; }
        public string SizeText
        {
            get { return StoreApp ? "UWP 应用" : (SizeMB > 0 ? SizeMB.ToString("F0") + " MB" : ""); }
        }
    }

    public static class SoftwareService
    {
        public static List<SoftInfo> ReadAll()
        {
            var list = new List<SoftInfo>();
            AddView(list, RegistryHive.LocalMachine, RegistryView.Default, "本机(64)");
            if (OS.IsX64) AddView(list, RegistryHive.LocalMachine, RegistryView.Registry32, "本机(32)");
            AddView(list, RegistryHive.CurrentUser, RegistryView.Default, "当前用户");

            var seen = new Dictionary<string, SoftInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in list)
            {
                if (string.IsNullOrEmpty(s.Name)) continue;
                SoftInfo old;
                if (seen.TryGetValue(s.Name, out old))
                {
                    if (string.IsNullOrEmpty(old.Uninstall) && !string.IsNullOrEmpty(s.Uninstall)) seen[s.Name] = s;
                    continue;
                }
                seen[s.Name] = s;
            }
            var res = new List<SoftInfo>(seen.Values);
            res.Sort(delegate(SoftInfo a, SoftInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return res;
        }

        static void AddView(List<SoftInfo> list, RegistryHive hive, RegistryView view, string from)
        {
            string path = Regs.HK_UNINST;
            if (view == RegistryView.Registry32 && OS.IsX64 && hive == RegistryHive.LocalMachine)
                path = Regs.HK_WOW_UNINST;
            foreach (string sub in Regs.SubKeys(hive, view, path))
            {
                try
                {
                    string p = path + "\\" + sub;
                    string name = Regs.Str(hive, view, p, "DisplayName");
                    if (string.IsNullOrEmpty(name)) continue;
                    string rt = Regs.Str(hive, view, p, "ReleaseType");
                    string pub = Regs.Str(hive, view, p, "Publisher") ?? "";
                    if (rt != null && pub.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string sysComp = Regs.Str(hive, view, p, "SystemComponent");
                    if (sysComp == "1") continue;
                    var s = new SoftInfo
                    {
                        Name = name,
                        Version = Regs.Str(hive, view, p, "DisplayVersion") ?? "",
                        Publisher = pub,
                        InstallDate = Regs.Str(hive, view, p, "InstallDate") ?? "",
                        Uninstall = Regs.Str(hive, view, p, "UninstallString") ?? "",
                        Quiet = Regs.Str(hive, view, p, "QuietUninstallString") ?? "",
                        Location = Regs.Str(hive, view, p, "InstallLocation") ?? "",
                        From = from,
                    };
                    object sz = Regs.Val(hive, view, p, "EstimatedSize");
                    if (sz != null) { try { s.SizeMB = Convert.ToInt64(sz) / 1024.0; } catch { } }
                    if (string.IsNullOrEmpty(s.Uninstall) && string.IsNullOrEmpty(s.Quiet)) continue;
                    list.Add(s);
                }
                catch { }
            }
        }

        /// <summary>枚举当前用户 UWP/AppX 应用（PowerShell，失败返回空列表）</summary>
        public static List<SoftInfo> ReadAppx()
        {
            var list = new List<SoftInfo>();
            try
            {
                string ps = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage | Where-Object {$_.SignatureKind -ne 'System' -and $_.IsFramework -eq $false} | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.Publisher }\"";
                var psi = new ProcessStartInfo("powershell.exe", ps)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return list; }
                    foreach (string raw in output.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length < 2) continue;
                        // 过滤明显的系统框架/运行时包
                        string n = parts[0];
                        if (n.IndexOf("Framework", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("VCLibs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("UI.Xaml", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Store", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("WindowsTerminal", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        list.Add(new SoftInfo
                        {
                            Name = n,
                            PackageFullName = parts[1],
                            Publisher = parts.Length > 2 ? parts[2] : "",
                            From = "应用商店",
                            StoreApp = true,
                        });
                    }
                }
                list.Sort(delegate(SoftInfo a, SoftInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            }
            catch { }
            return list;
        }

        public static string UninstallAppx(string packageFullName)
        {
            if (!OS.IsElevated) return "卸载其他用户的应用需要管理员；当前用户应用可直接卸载";
            string ps = "-NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '" + packageFullName.Replace("'", "''") + "'\"";
            string r = Runner.Run("powershell.exe", ps, 60000);
            return r;
        }

        public static string BuildUninstallCmd(SoftInfo s)
        {
            string cmd = s.Quiet;
            bool useQuiet = !string.IsNullOrEmpty(cmd);
            if (!useQuiet) cmd = s.Uninstall;
            cmd = cmd.Trim();
            if (cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("\"msiexec", StringComparison.OrdinalIgnoreCase))
            {
                if (cmd.IndexOf(" /i ", StringComparison.OrdinalIgnoreCase) > 0)
                    return Regex.Replace(cmd, " /i ", " /x ", RegexOptions.IgnoreCase);
                if (cmd.IndexOf(" /package ", StringComparison.OrdinalIgnoreCase) > 0)
                    return Regex.Replace(cmd, " /package ", " /x ", RegexOptions.IgnoreCase);
                return "msiexec /x " + cmd.TrimStart('"');
            }
            return cmd;
        }

        public static void OpenInstallFolder(SoftInfo s)
        {
            string dir = s.Location;
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) { Process.Start("explorer.exe", "\"" + dir + "\""); return; }
            }
            catch { }
            string m = Regex.Match(s.Uninstall ?? "", "\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(m)) m = (s.Uninstall ?? "").Split(' ')[0];
            try
            {
                string d = Path.GetDirectoryName(m);
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\"");
                else Process.Start("explorer.exe", "/select,\"" + m + "\"");
            }
            catch { }
        }
    }
}
