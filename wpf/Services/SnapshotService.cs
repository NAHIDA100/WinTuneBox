using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public class SnapshotInfo
    {
        public string Dir { get; set; }
        public string Stamp { get; set; }
        public string Display { get; set; }
        public int RegFiles { get; set; }
        public bool HasServices { get; set; }
        public bool HasRestorePoint { get; set; }
    }

    /// <summary>优化快照中心：注册表关键分支导出 + 服务启动类型快照，支持一键恢复</summary>
    public static class SnapshotService
    {
        // 一键优化涉及的关键注册表分支（根简写 + 实际路径）
        static readonly string[] RegBranches = {
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppCapture",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            @"HKCU\Control Panel\Desktop",
            @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
        };

        public static string CreateSnapshot(bool withRestorePoint, Action<string, bool> log)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dir = Path.Combine(AppPaths.SnapshotDir, stamp);
            try { Directory.CreateDirectory(dir); } catch (Exception ex) { return "创建快照目录失败: " + ex.Message; }

            int ok = 0, skip = 0;
            foreach (string branch in RegBranches)
            {
                string safe = RegexSafe(branch);
                string file = Path.Combine(dir, safe + ".reg");
                string outp = Runner.Run("reg.exe", "export \"" + branch + "\" \"" + file + "\" /y", 15000);
                if (File.Exists(file)) { ok++; if (log != null) log("已导出 " + branch, false); }
                else { skip++; if (log != null) log("跳过(不存在/无权限) " + branch, true); }
            }

            // 服务启动类型快照
            try
            {
                var sb = new StringBuilder();
                string[] names = Regs.SubKeys(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES);
                foreach (string n in names)
                {
                    int? start = Regs.Dword(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES + "\\" + n, "Start");
                    int? type = Regs.Dword(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES + "\\" + n, "Type");
                    if (start.HasValue && type.HasValue && type.Value != 0 && type.Value < 0x10 && start.Value >= 2 && start.Value <= 4)
                        sb.AppendLine(n + "=" + start.Value);
                }
                File.WriteAllText(Path.Combine(dir, "services.txt"), sb.ToString(), Encoding.UTF8);
                if (log != null) log("已记录服务启动类型快照", false);
            }
            catch (Exception ex) { if (log != null) log("服务快照失败: " + ex.Message, true); }

            bool rp = false;
            if (withRestorePoint)
            {
                if (OS.IsElevated)
                {
                    string err = SystemTools.CreateRestorePoint("WinTuneBox 优化前快照 " + stamp);
                    rp = err == null;
                    if (log != null) log(rp ? "已创建系统还原点" : ("系统还原点: " + err), !rp);
                }
                else if (log != null) log("未提权，跳过系统还原点（注册表/服务快照仍已创建）", true);
            }

            try
            {
                File.WriteAllText(Path.Combine(dir, "info.txt"),
                    "WinTuneBox 优化快照\r\n时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    "\r\n系统: " + OS.WinVerFull + "\r\n注册表分支: " + ok + " 个\r\n系统还原点: " + (rp ? "是" : "否") + "\r\n",
                    Encoding.UTF8);
            }
            catch { }

            Logger.Log("Snapshot", "创建快照 " + stamp + " reg=" + ok + " skip=" + skip + " restorePoint=" + rp);
            if (log != null) log(string.Format("快照完成：{0} 个注册表分支，目录 {1}", ok, dir), false);
            return null;
        }

        static string RegexSafe(string branch)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) branch = branch.Replace(c, '_');
            branch = branch.Replace('\\', '_').Replace(':', '_').Replace('{', '_').Replace('}', ' ').Replace(' ', '_');
            return branch;
        }

        public static List<SnapshotInfo> List()
        {
            var list = new List<SnapshotInfo>();
            try
            {
                foreach (string d in Directory.GetDirectories(AppPaths.SnapshotDir))
                {
                    var info = new SnapshotInfo();
                    info.Dir = d;
                    info.Stamp = Path.GetFileName(d);
                    DateTime dt;
                    if (DateTime.TryParseExact(info.Stamp, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out dt))
                        info.Display = dt.ToString("yyyy-MM-dd HH:mm:ss");
                    else info.Display = info.Stamp;
                    info.RegFiles = Directory.GetFiles(d, "*.reg").Length;
                    info.HasServices = File.Exists(Path.Combine(d, "services.txt"));
                    info.HasRestorePoint = File.Exists(Path.Combine(d, "info.txt")) &&
                        File.ReadAllText(Path.Combine(d, "info.txt")).Contains("系统还原点: 是");
                    list.Add(info);
                }
                list.Sort(delegate(SnapshotInfo a, SnapshotInfo b) { return string.Compare(b.Stamp, a.Stamp, StringComparison.Ordinal); });
            }
            catch { }
            return list;
        }

        public static string Restore(SnapshotInfo info, Action<string, bool> log)
        {
            if (!Directory.Exists(info.Dir)) return "快照目录不存在";
            if (!OS.IsElevated) return "恢复快照需要管理员权限";
            int ok = 0, fail = 0;
            foreach (string reg in Directory.GetFiles(info.Dir, "*.reg"))
            {
                string outp = Runner.Run("reg.exe", "import \"" + reg + "\"", 20000);
                if (outp == null || outp.IndexOf("成功", StringComparison.Ordinal) >= 0 || outp.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0) { ok++; if (log != null) log("已恢复 " + Path.GetFileName(reg), false); }
                else { fail++; if (log != null) log("恢复失败 " + Path.GetFileName(reg) + ": " + outp, true); }
            }
            string svc = Path.Combine(info.Dir, "services.txt");
            if (File.Exists(svc))
            {
                int sok = 0, sfail = 0;
                foreach (string line in File.ReadAllLines(svc))
                {
                    string[] p = line.Split('=');
                    if (p.Length != 2) continue;
                    int v;
                    if (!int.TryParse(p[1], out v)) continue;
                    string err = ServiceManager.SetStartType(p[0], v, false);
                    if (err == null) sok++; else sfail++;
                }
                if (log != null) log(string.Format("服务启动类型恢复 {0} 项，失败 {1} 项", sok, sfail), sfail > 0);
            }
            if (log != null) log("注册表恢复完成（" + ok + " 成功/" + fail + " 失败），部分设置需重启或重启资源管理器生效", fail > 0);
            Logger.Log("Snapshot", "恢复快照 " + info.Stamp + " reg ok=" + ok + " fail=" + fail);
            return fail > 0 ? "部分项目恢复失败，请以管理员身份重试或查看日志" : null;
        }

        public static void Delete(SnapshotInfo info)
        {
            try { if (Directory.Exists(info.Dir)) Directory.Delete(info.Dir, true); } catch { }
        }
    }
}
