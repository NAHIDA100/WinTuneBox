using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public class StartupEntry
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Loc { get; set; }
        public string Kind { get; set; }
        public bool Enabled { get; set; }
        public bool SysDisabled { get; set; }
        public string FullPath { get; set; }
        public string Source { get; set; }  // reg / folder / task
    }

    /// <summary>启动项：注册表 Run + 启动文件夹 + 计划任务(登录触发)，兼容 Win7 ~ Win11</summary>
    public static class StartupService
    {
        public const string OFF_MARK = "@WinTuneOff@";
        static readonly string[] FolderExts = { ".lnk", ".url", ".exe", ".bat", ".cmd", ".pif" };

        public static List<StartupEntry> GetAll()
        {
            var list = new List<StartupEntry>();
            AddReg(list, "cu", Regs.HK_RUN, "注册表: 当前用户", false);
            AddReg(list, "lm", Regs.HK_RUN, "注册表: 本机(64位)", OS.IsX64);
            AddReg(list, "lm", Regs.HK_WOW_RUN, "注册表: 本机(32位)", OS.IsX64);
            AddDir(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "启动文件夹: 当前用户", false);
            AddDir(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "启动文件夹: 所有用户", true);
            AddTasks(list);
            return list;
        }

        static void AddReg(List<StartupEntry> list, string hv, string path, string locName, bool useView32)
        {
            bool sys = hv == "lm";
            RegistryView view = useView32 ? RegistryView.Registry32 : RegistryView.Default;
            RegistryHive hive = sys ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            foreach (string name in Regs.ValueNames(hive, view, path))
            {
                string cmd = Regs.Str(hive, view, path, name);
                if (string.IsNullOrEmpty(cmd)) continue;
                string orig = name;
                bool oursOff = false;
                if (name.EndsWith(OFF_MARK)) { orig = name.Substring(0, name.Length - OFF_MARK.Length); oursOff = true; }
                bool sysOff = IsSysDisabledReg(hv, useView32, orig);
                list.Add(new StartupEntry
                {
                    Name = orig,
                    Command = cmd,
                    Loc = locName,
                    Source = "reg",
                    Kind = (hv == "cu" ? "reg_user" : (useView32 ? "reg_sys32" : "reg_sys64")),
                    Enabled = !(oursOff || sysOff),
                    SysDisabled = sysOff,
                    FullPath = "R|" + hv + "|" + (useView32 ? "32" : "64") + "|" + path + "|" + orig,
                });
            }
        }

        static bool IsSysDisabledReg(string hv, bool useView32, string valueName)
        {
            if (!OS.IsWin8Plus) return false;
            bool sys = hv == "lm";
            RegistryView runView = useView32 ? RegistryView.Registry32 : RegistryView.Default;
            string approvedPath = Regs.HK_APPROVED + "\\Run";
            if (sys && useView32 && OS.IsX64) approvedPath = Regs.HK_APPROVED + "\\Run32";
            byte[] b = GetApproved(hv, approvedPath, valueName);
            return b != null && b.Length > 0 && b[0] == 0x03;
        }

        static byte[] GetApproved(string hv, string approvedPath, string name)
        {
            RegistryHive hive = hv == "lm" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView view = hv == "lm" ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default;
            return Regs.Val(hive, view, approvedPath, name) as byte[];
        }

        static void WriteApproved(string hv, string approvedPath, string name, bool disabled)
        {
            RegistryHive hive = hv == "lm" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView view = hv == "lm" ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default;
            byte[] data = new byte[12];
            data[0] = disabled ? (byte)0x03 : (byte)0x02;
            try
            {
                using (var k = RegistryKey.OpenBaseKey(hive, view).CreateSubKey(approvedPath))
                    if (k != null) k.SetValue(name, data);
            }
            catch { }
        }

        static void AddDir(List<StartupEntry> list, string folder, string locName, bool sys)
        {
            if (folder == null || !Directory.Exists(folder)) return;
            foreach (string f in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(f).ToLower();
                if (Array.IndexOf(FolderExts, ext) < 0) continue;
                string file = Path.GetFileName(f);
                string orig = file;
                bool oursOff = false;
                if (orig.EndsWith(OFF_MARK)) { orig = orig.Substring(0, orig.Length - OFF_MARK.Length); oursOff = true; }
                bool sysOff = false;
                if (OS.IsWin8Plus)
                {
                    byte[] b = GetApproved(sys ? "lm" : "cu", Regs.HK_APPROVED + "\\StartupFolder", orig);
                    sysOff = b != null && b.Length > 0 && b[0] == 0x03;
                }
                list.Add(new StartupEntry
                {
                    Name = orig,
                    Command = f,
                    Loc = locName,
                    Source = "folder",
                    Kind = sys ? "dir_sys" : "dir_user",
                    Enabled = !(oursOff || sysOff),
                    SysDisabled = sysOff,
                    FullPath = "F|" + f + "|" + orig,
                });
            }
        }

        /// <summary>计划任务中登录时触发、且本机/当前用户的任务（schtasks 解析，只读展示，可禁用）</summary>
        static void AddTasks(List<StartupEntry> list)
        {
            try
            {
                string outp = Runner.Run("schtasks.exe", "/query /fo csv /nh /v", 20000);
                if (string.IsNullOrEmpty(outp)) return;
                using (var sr = new StringReader(outp))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        var cols = ParseCsv(line);
                        if (cols.Count < 3) continue;
                        string taskName = cols[0];
                        string status = cols.Count > 2 ? cols[2] : "";
                        string trigger = cols.Count > 4 ? cols[4] : "";
                        string runAs = cols.Count > 5 ? cols[5] : "";
                        string taskToRun = cols.Count > 7 ? cols[7] : "";
                        if (trigger.IndexOf("登录", StringComparison.OrdinalIgnoreCase) < 0 &&
                            trigger.IndexOf("logon", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (string.IsNullOrEmpty(taskToRun) || taskToRun == "\"\"") continue;
                        list.Add(new StartupEntry
                        {
                            Name = Path.GetFileName(taskName.Trim('"')) ,
                            Command = taskToRun.Trim('"'),
                            Loc = "计划任务: " + (runAs.Length > 24 ? runAs.Substring(0, 24) : runAs),
                            Source = "task",
                            Kind = "task",
                            Enabled = status.IndexOf("禁用", StringComparison.OrdinalIgnoreCase) < 0 &&
                                      status.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) < 0,
                            FullPath = "T|" + taskName.Trim('"'),
                        });
                    }
                }
            }
            catch { }
        }

        static List<string> ParseCsv(string line)
        {
            var list = new List<string>();
            bool inQ = false; var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (c == ',' && !inQ) { list.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(c);
            }
            list.Add(cur.ToString());
            return list;
        }

        public static string Enable(StartupEntry it, bool enable)
        {
            string[] p = it.FullPath.Split('|');
            if (p[0] == "R") return EnableReg(p, enable);
            if (p[0] == "F") return EnableFile(p, enable);
            return EnableTask(p, enable);
        }

        static string EnableReg(string[] p, bool enable)
        {
            string hv = p[1], bits = p[2], path = p[3], origName = p[4];
            RegistryHive hive = hv == "lm" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView view = (hv == "lm" && bits == "32" && OS.IsX64) ? RegistryView.Registry32 : RegistryView.Default;
            try
            {
                if (OS.IsWin8Plus)
                {
                    string ap = Regs.HK_APPROVED + "\\Run";
                    if (hv == "lm" && bits == "32" && OS.IsX64) ap = Regs.HK_APPROVED + "\\Run32";
                    WriteApproved(hv, ap, origName, !enable);
                    return null;
                }
                using (var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path, true))
                {
                    if (k == null) return "找不到启动项键";
                    string src = enable ? origName + OFF_MARK : origName;
                    string dst = enable ? origName : origName + OFF_MARK;
                    object v = k.GetValue(src);
                    if (v == null) return "启动项状态与预期不符，请刷新";
                    if (k.GetValue(dst) == null) { k.SetValue(dst, v); k.DeleteValue(src, false); }
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string EnableFile(string[] p, bool enable)
        {
            string full = p[1], orig = p[2];
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            bool sys = !string.IsNullOrEmpty(common) && full.StartsWith(common, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (OS.IsWin8Plus)
                {
                    WriteApproved(sys ? "lm" : "cu", Regs.HK_APPROVED + "\\StartupFolder", orig, !enable);
                    return null;
                }
                if (!File.Exists(full)) return "快捷方式已不存在";
                string src = enable ? full + OFF_MARK : full;
                string dst = enable ? full : full + OFF_MARK;
                if (!File.Exists(dst) && File.Exists(src)) File.Move(src, dst);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string EnableTask(string[] p, bool enable)
        {
            if (!OS.IsElevated) return "禁用计划任务需要管理员权限";
            string name = p[1];
            string args = enable
                ? "/change /tn \"" + name + "\" /enable"
                : "/change /tn \"" + name + "\" /disable";
            string r = Runner.Run("schtasks.exe", args, 15000);
            return r == null ? null : ("计划任务修改失败: " + r);
        }

        public static string Delete(StartupEntry it)
        {
            string[] p = it.FullPath.Split('|');
            if (p[0] == "R")
            {
                string hv = p[1], bits = p[2], path = p[3], name = p[4];
                string backup = Backup.WriteRegBackup(hv == "lm", bits == "32", path, name, "startup");
                RegistryHive hive = hv == "lm" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                RegistryView view = (hv == "lm" && bits == "32" && OS.IsX64) ? RegistryView.Registry32 : RegistryView.Default;
                try
                {
                    using (var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path, true))
                    {
                        if (k == null) return "找不到启动项键";
                        string nm = k.GetValue(name) != null ? name : name + OFF_MARK;
                        k.DeleteValue(nm, false);
                    }
                    return backup == null ? null : "已删除（备份: " + Path.GetFileName(backup) + "）";
                }
                catch (Exception ex) { return ex.Message; }
            }
            if (p[0] == "F")
            {
                string full = p[1];
                try
                {
                    string back = Backup.CopyToBackup(full, "startup");
                    if (File.Exists(full)) File.Delete(full);
                    return back == null ? "已删除" : "已删除（备份: " + Path.GetFileName(back) + "）";
                }
                catch (Exception ex) { return ex.Message; }
            }
            return "计划任务请在“任务计划程序”中删除（本工具仅支持启用/禁用）";
        }

        public static string Add(string name, string exePath, bool allUsers, bool asFolderShortcut)
        {
            try
            {
                if (asFolderShortcut)
                {
                    string folder = allUsers
                        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                        : Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    string link = Path.Combine(folder, name + ".lnk");
                    CreateShortcut(link, exePath);
                    return null;
                }
                RegistryHive hive = allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                return Regs.SetStr(hive, allUsers ? Regs.V64() : RegistryView.Default, Regs.HK_RUN, name, "\"" + exePath + "\"");
            }
            catch (Exception ex) { return ex.Message; }
        }

        static void CreateShortcut(string link, string target)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                Arguments = string.Format("-NoProfile -Command \"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{0}');$s.TargetPath='{1}';$s.Save()\"",
                    link.Replace("'", "''"), target.Replace("'", "''")),
            };
            System.Diagnostics.Process.Start(psi).WaitForExit(8000);
        }

        public static void OpenFolder(bool common)
        {
            string f = common ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                              : Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\""); } catch { }
        }
    }
}
