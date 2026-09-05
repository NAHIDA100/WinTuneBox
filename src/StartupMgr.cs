using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 启动项管理：注册表 Run + 启动文件夹，兼容 Win7(WOW视图/无StartupApproved) ~ Win11 ═══
    public class StartupEntry
    {
        public string Name;       // 显示名（=值名或文件名）
        public string Command;    // 命令或目标
        public string Loc;        // 位置描述
        public string Kind;       // reg_user / reg_sys64 / reg_sys32 / dir_user / dir_sys
        public bool Enabled;      // 是否处于启用状态
        public bool SysDisabled;  // 被系统(任务管理器)禁用标记
        public string FullPath;   // 注册表值所在 hive+path 或文件路径（还原用）
    }

    public static class StartupMgr
    {
        public const string OFF_MARK = "@WinTuneOff@";   // Win7 降级方案：值名/文件名标记
        static readonly string[] FolderExts = { ".lnk", ".url", ".exe", ".bat", ".cmd", ".pif" };

        public struct HiveView { public const string HKCU = "cu"; public const string HKLM = "lm"; }

        public static List<StartupEntry> GetAll()
        {
            var list = new List<StartupEntry>();
            AddReg(list, HiveView.HKCU, Regs.HK_RUN, "注册表: 当前用户", false);
            AddReg(list, HiveView.HKLM, Regs.HK_RUN, "注册表: 本机(64位)", OS.IsX64);
            AddReg(list, HiveView.HKLM, Regs.HK_WOW_RUN, "注册表: 本机(32位)", OS.IsX64);

            string uf = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string cf = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            AddDir(list, uf, "启动文件夹: 当前用户", false);
            AddDir(list, cf, "启动文件夹: 所有用户", true);
            return list;
        }

        // ── 读注册表 Run ──
        static void AddReg(List<StartupEntry> list, string hv, string path, string locName, bool useView32)
        {
            bool sys = hv == HiveView.HKLM;
            RegistryView view = useView32 ? RegistryView.Registry32 : RegistryView.Default;
            RegistryHive hive = sys ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            string[] names = Regs.ValueNames(hive, view, path);
            foreach (string name in names)
            {
                string cmd = Regs.Str(hive, view, path, name);
                if (string.IsNullOrEmpty(cmd)) continue;
                string orig = name;
                bool oursOff = false;
                if (name.EndsWith(OFF_MARK)) { orig = name.Substring(0, name.Length - OFF_MARK.Length); oursOff = true; }

                bool sysOff = IsSysDisabled(hv, sys, view, orig);
                list.Add(new StartupEntry
                {
                    Name = orig,
                    Command = cmd,
                    Loc = locName,
                    Kind = (hv == HiveView.HKCU ? "reg_user" : (useView32 ? "reg_sys32" : "reg_sys64")),
                    Enabled = !(oursOff || sysOff),
                    SysDisabled = sysOff,
                    FullPath = "R|" + hv + "|" + (useView32 ? "32" : "64") + "|" + path + "|" + orig,
                });
            }
        }

        // ── StartupApproved 判定（Win8+；值首字节 02=启用 03=禁用）──
        static bool IsSysDisabled(string hv, bool sys, RegistryView runView, string valueName)
        {
            if (!OS.IsWin8Plus) return false;
            string approvedPath = Regs.HK_APPROVED + "\\" + (sys ? "Run" : "Run");
            if (hv == HiveView.HKLM && runView == RegistryView.Registry32 && OS.IsX64)
                approvedPath = Regs.HK_APPROVED + "\\Run32";
            byte[] b = GetApproved(hv, approvedPath, valueName);
            if (b == null || b.Length == 0) return false;
            return b[0] == 0x03;
        }
        static byte[] GetApproved(string hv, string approvedPath, string name)
        {
            RegistryHive hive = hv == HiveView.HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView view = hv == HiveView.HKLM ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default;
            object o = Regs.Val(hive, view, approvedPath, name);
            return o as byte[];
        }

        static void WriteApproved(string hv, string approvedPath, string name, bool disabled)
        {
            RegistryHive hive = hv == HiveView.HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView view = hv == HiveView.HKLM ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default;
            byte[] data = new byte[12];
            data[0] = disabled ? (byte)0x03 : (byte)0x02;
            try
            {
                using (var k = RegistryKey.OpenBaseKey(hive, view).CreateSubKey(approvedPath))
                    if (k != null) k.SetValue(name, data);
            }
            catch { }
        }

        // ── 读启动文件夹 ──
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
                    string ap = Regs.HK_APPROVED + "\\StartupFolder";
                    string hv = sys ? HiveView.HKLM : HiveView.HKCU;
                    byte[] b = GetApproved(hv, ap, orig);
                    sysOff = b != null && b.Length > 0 && b[0] == 0x03;
                }
                list.Add(new StartupEntry
                {
                    Name = orig,
                    Command = f,
                    Loc = locName,
                    Kind = sys ? "dir_sys" : "dir_user",
                    Enabled = !(oursOff || sysOff),
                    SysDisabled = sysOff,
                    FullPath = "F|" + f + "|" + orig,
                });
            }
        }

        // ═══ 操作 ═══
        public static string Enable(StartupEntry it, bool enable, string which)
        {
            string[] p = which.Split('|');
            if (p[0] == "R") return EnableReg(p, enable);
            return EnableFile(p, enable);
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
                    // Win8+：只写 StartupApproved（与任务管理器同机制，值本身不动）
                    string ap = Regs.HK_APPROVED + "\\Run";
                    if (hv == "lm" && bits == "32" && OS.IsX64) ap = Regs.HK_APPROVED + "\\Run32";
                    WriteApproved(hv, ap, origName, !enable);
                    return null;
                }
                // Win7：无 StartupApproved，用值名标记法
                using (var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path, true))
                {
                    if (k == null) return "找不到启动项键";
                    string src = enable ? origName + OFF_MARK : origName;
                    string dst = enable ? origName : origName + OFF_MARK;
                    object v = k.GetValue(src);
                    if (v == null) return "启动项状态与预期不符，请刷新";
                    if (k.GetValue(dst) == null)
                    {
                        k.SetValue(dst, v);
                        k.DeleteValue(src, false);
                    }
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
                    string hv = sys ? HiveView.HKLM : HiveView.HKCU;
                    string ap = Regs.HK_APPROVED + "\\StartupFolder";
                    WriteApproved(hv, ap, orig, !enable);
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

        /// <summary>删除启动项；注册表项导出 .reg 备份，文件项复制到备份目录</summary>
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
            else
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
        }

        /// <summary>新增启动项</summary>
        public static string Add(string name, string exePath, bool allUsers, bool asFolderShortcut)
        {
            try
            {
                if (asFolderShortcut)
                {
                    string folder = allUsers
                        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                        : Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    string target = exePath;
                    if (!target.ToLower().EndsWith(".lnk")) target = target + ".lnk";
                    string link = Path.Combine(folder, name + ".lnk");
                    CreateShortcut(link, exePath, name);
                    return null;
                }
                RegistryHive hive = allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                string err = Regs.SetStr(hive, allUsers ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default,
                    Regs.HK_RUN, name, "\"" + exePath + "\"");
                return err;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static void CreateShortcut(string link, string target, string name)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                Arguments = string.Format("-NoProfile -Command \"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{0}');$s.TargetPath='{1}';$s.Save()\"",
                    link.Replace("'", "''"), target.Replace("'", "''")),
            };
            System.Diagnostics.Process.Start(psi).WaitForExit(8000);
        }

        public static void OpenFolder(bool common)
        {
            string f = common
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                : Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\""); }
            catch { }
        }
    }
}
