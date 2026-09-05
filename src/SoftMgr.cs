using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 已安装软件（32/64位注册表全视图）═══
    public class SoftInfo
    {
        public string Name, Version, Publisher, InstallDate, Uninstall, Quiet, Location;
        public double SizeMB;
        public string From;   // 来源标记
    }

    public static class SoftMgr
    {
        public static List<SoftInfo> ReadAll()
        {
            var list = new List<SoftInfo>();
            AddView(list, RegistryHive.LocalMachine, RegistryView.Default, "本机(64)");
            if (OS.IsX64)
            {
                AddView(list, RegistryHive.LocalMachine, RegistryView.Registry32, "本机(32)");
            }
            AddView(list, RegistryHive.CurrentUser, RegistryView.Default, "当前用户");

            // 去重：同名称取第一个（优先有卸载命令的）
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
            res.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
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
                    // 过滤 Windows 系统补丁条目（有 ReleaseType 且为微软）
                    string rt = Regs.Str(hive, view, p, "ReleaseType");
                    if (rt != null && (Regs.Str(hive, view, p, "Publisher") ?? "").IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    var s = new SoftInfo
                    {
                        Name = name,
                        Version = Regs.Str(hive, view, p, "DisplayVersion") ?? "",
                        Publisher = Regs.Str(hive, view, p, "Publisher") ?? "",
                        InstallDate = Regs.Str(hive, view, p, "InstallDate") ?? "",
                        Uninstall = Regs.Str(hive, view, p, "UninstallString") ?? "",
                        Quiet = Regs.Str(hive, view, p, "QuietUninstallString") ?? "",
                        Location = Regs.Str(hive, view, p, "InstallLocation") ?? "",
                        From = from,
                    };
                    object sz = Regs.Val(hive, view, p, "EstimatedSize");
                    if (sz != null)
                    {
                        try { s.SizeMB = Convert.ToInt64(sz) / 1024.0; } catch { }
                    }
                    if (string.IsNullOrEmpty(s.Uninstall) && string.IsNullOrEmpty(s.Quiet)) continue;
                    list.Add(s);
                }
                catch { }
            }
        }

        public static string BuildUninstallCmd(SoftInfo s)
        {
            string cmd = s.Quiet;
            bool useQuiet = !string.IsNullOrEmpty(cmd);
            if (!useQuiet) cmd = s.Uninstall;
            cmd = cmd.Trim();
            // msiexec /i xxx → /x 卸载
            if (cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("\"msiexec", StringComparison.OrdinalIgnoreCase))
            {
                bool replaceOk = false;
                if (cmd.IndexOf(" /i ", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    cmd = Regex.Replace(cmd, " /i ", " /x ", RegexOptions.IgnoreCase);
                    replaceOk = true;
                }
                else if (cmd.IndexOf(" /package ", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    cmd = Regex.Replace(cmd, " /package ", " /x ", RegexOptions.IgnoreCase);
                    replaceOk = true;
                }
                if (!replaceOk) cmd = "msiexec /x " + cmd.TrimStart('"');
                return cmd;
            }
            if (useQuiet) return cmd; // QuietUninstallString 通常已带静默参数
            // 普通卸载器，追加静默参数常见形式
            return cmd;
        }

        public static bool IsMsiexec(string cmd)
        {
            return cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) ||
                   cmd.StartsWith("\"msiexec", StringComparison.OrdinalIgnoreCase);
        }

        public static void OpenInstallFolder(SoftInfo s)
        {
            string dir = s.Location;
            try
            {
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                    return;
                }
            }
            catch { }
            // 从卸载命令猜 exe 目录
            string m = Regex.Match(s.Uninstall ?? "", "\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(m)) m = (s.Uninstall ?? "").Split(' ')[0];
            try
            {
                string d = System.IO.Path.GetDirectoryName(m);
                if (!string.IsNullOrEmpty(d) && System.IO.Directory.Exists(d))
                    Process.Start("explorer.exe", "\"" + d + "\"");
                else
                    Process.Start("explorer.exe", "/select,\"" + m + "\"");
            }
            catch { }
        }
    }
}
