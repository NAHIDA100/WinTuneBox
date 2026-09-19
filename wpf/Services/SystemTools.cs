using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public static class SystemTools
    {
        // ── 系统还原点 ──
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RESTOREPOINTINFO
        {
            public int dwEventType;
            public int dwRestorePtType;
            public long llSequenceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szDescription;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct STATEMGRSTATUS { public int nStatus; public int llSequenceNumber; }
        [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
        static extern int SRSetRestorePointW(ref RESTOREPOINTINFO info, out STATEMGRSTATUS status);

        public static string CreateRestorePoint(string desc)
        {
            var info = new RESTOREPOINTINFO();
            info.dwEventType = 100;
            info.dwRestorePtType = 0;
            info.llSequenceNumber = 0;
            info.szDescription = desc;
            STATEMGRSTATUS st;
            int hr = SRSetRestorePointW(ref info, out st);
            if (hr == 0)
            {
                info.dwEventType = 101;
                SRSetRestorePointW(ref info, out st);
                return null;
            }
            if (hr == unchecked((int)0x80070422)) return "系统还原服务未开启";
            if (hr == unchecked((int)0x80042308)) return "系统保护未开启（需在系统保护中先为系统盘启用）";
            return "创建失败 (0x" + hr.ToString("X8") + ")，请确认系统保护已开启";
        }

        // ── 内存整理 ──
        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static int CleanAllMemory()
        {
            int ok = 0;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr h = OpenProcess(0x1F0FFF, false, p.Id);
                        if (h == IntPtr.Zero) h = OpenProcess(0x0400 | 0x0008, false, p.Id);
                        if (h != IntPtr.Zero)
                        {
                            try { if (EmptyWorkingSet(h) != 0) ok++; }
                            finally { CloseHandle(h); }
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return ok;
        }

        public static long AvailableMB()
        {
            OS.RefreshMem();
            return (long)(OS.FreeRam / (1024.0 * 1024.0));
        }

        /// <summary>三轮工作集整理 + 等待脏页写回，返回 PCL 风格报告（后台线程调用）</summary>
        public static string OptimizeMemoryNow()
        {
            long before = AvailableMB();
            int n = 0;
            for (int round = 0; round < 3; round++)
            {
                n += CleanAllMemory();
                if (round < 2) System.Threading.Thread.Sleep(150);
            }
            System.Threading.Thread.Sleep(1800);
            long after = AvailableMB();
            long freedMb = after - before;
            if (freedMb < 0) freedMb = 0;
            string report = string.Format("整理 {0} 进程次 · 可用内存增加 {1}（{2} → {3} GB）",
                n,
                freedMb >= 1024 ? (freedMb / 1024.0).ToString("F2") + " GB" : freedMb + " MB",
                (before / 1024.0).ToString("F2"),
                (after / 1024.0).ToString("F2"));
            if (!OS.IsElevated) report += "\n（当前未提权：系统进程无法整理；管理员身份运行效果更佳）";
            return report;
        }

        // ── 电源计划 ──
        const string GUID_ULTIMATE = "e9c42d02-d5df-432d-9ed0-0f53b3d0b34e";
        static readonly Regex GuidRe = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static string SwitchPowerScheme(string targetName)
        {
            if (!OS.IsElevated) return "切换电源计划需要管理员权限，请先提权。";
            string curGuid, curName;
            OS.ActivePowerScheme(out curGuid, out curName);
            if (curName != null && curName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                return "当前已是“" + curName + "”，无需切换";

            if (targetName == "卓越性能")
            {
                if (!OS.IsWin10Plus) return "卓越性能仅 Windows 10/11 提供";
                string r0 = Runner.Run("powercfg.exe", "/setactive " + GUID_ULTIMATE, 10000);
                if (r0 == null) return null;
                string dup0 = Runner.Run("powercfg.exe", "/duplicatescheme " + GUID_ULTIMATE, 15000);
                string g0 = ExtractGuid(dup0);
                if (!string.IsNullOrEmpty(g0)) { string r1 = Runner.Run("powercfg.exe", "/setactive " + g0, 10000); if (r1 == null) return null; }
                string listP = Runner.Run("powercfg.exe", "/list", 15000);
                string srcGuid = null;
                if (listP != null)
                    foreach (string line in listP.Split('\n'))
                        if (line.IndexOf("高性能", StringComparison.Ordinal) >= 0) { srcGuid = ExtractGuid(line); if (!string.IsNullOrEmpty(srcGuid)) break; }
                if (!string.IsNullOrEmpty(srcGuid))
                {
                    string dup2 = Runner.Run("powercfg.exe", "/duplicatescheme " + srcGuid, 15000);
                    string g2 = ExtractGuid(dup2);
                    if (!string.IsNullOrEmpty(g2))
                    {
                        Runner.Run("powercfg.exe", "/changename " + g2 + " 卓越性能-增强", 10000);
                        string r2 = Runner.Run("powercfg.exe", "/setactive " + g2, 10000);
                        if (r2 == null) return "OK:当前 Windows 版本已移除内置“卓越性能”方案，已复制“高性能”为「卓越性能-增强」并启用。";
                    }
                }
                return "未能启用“卓越性能”：\n" + (dup0 ?? r0);
            }

            string list = Runner.Run("powercfg.exe", "/list", 15000);
            string lastErr = null;
            if (list != null)
                foreach (string line in list.Split('\n'))
                    if (line.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string guid = ExtractGuid(line);
                        if (!string.IsNullOrEmpty(guid)) { string r = Runner.Run("powercfg.exe", "/setactive " + guid, 10000); if (r == null) return null; lastErr = r; }
                    }
            string alias = "SCHEME_BALANCED";
            if (targetName == "高性能") alias = "SCHEME_MIN";
            if (targetName == "节能") alias = "SCHEME_SAVER";
            string outp = Runner.Run("powercfg.exe", "/duplicatescheme " + alias, 20000);
            string g = ExtractGuid(outp);
            if (!string.IsNullOrEmpty(g))
            {
                string r2 = Runner.Run("powercfg.exe", "/setactive " + g, 10000);
                if (r2 == null) return null;
                return "切换失败: " + r2;
            }
            return "未能找到/创建“" + targetName + "”电源计划：\n" + outp + (lastErr != null ? "\n（激活已有同名计划也失败: " + lastErr + "）" : "");
        }

        static string ExtractGuid(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = GuidRe.Match(text);
            return m.Success ? m.Value : null;
        }

        // ── 安全模式 ──
        public static string SetSafeBoot(bool on)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            if (on) return Runner.Run("bcdedit.exe", "/set {current} safeboot minimal", 10000);
            return Runner.Run("bcdedit.exe", "/deletevalue {current} safeboot", 10000);
        }
        public static void RestartNow()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"WinTuneBox: 系统将在 5 秒后重启\"") { UseShellExecute = false, CreateNoWindow = true });
                if (p != null) p.WaitForExit(3000);
            }
            catch { }
        }

        public static string ScheduleChkdsk(string drive)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            string outp = Runner.Run("cmd.exe", "/c echo y| chkdsk " + drive.TrimEnd('\\') + "\\ /f", 30000);
            if (outp == null) return null;
            if (outp.IndexOf("计划", StringComparison.Ordinal) >= 0 || outp.IndexOf("scheduled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                outp.IndexOf("下次", StringComparison.Ordinal) >= 0) return null;
            return outp;
        }

        public static void Open(string what)
        {
            try
            {
                string t = what.ToLower();
                if (t == "msconfig") Process.Start("msconfig.exe");
                else if (t == "taskmgr") Process.Start("taskmgr.exe");
                else if (t == "devmgmt") Process.Start("devmgmt.msc");
                else if (t == "services") Process.Start("services.msc");
                else if (t == "eventvwr") Process.Start("eventvwr.msc");
                else if (t == "perfmon") Process.Start("perfmon.exe", "/res");
                else if (t == "diskmgmt") Process.Start("diskmgmt.msc");
                else if (t == "compmgmt") Process.Start("compmgmt.msc");
                else if (t == "regedit") Process.Start("regedit.exe");
                else if (t == "cmd") Process.Start("cmd.exe");
                else if (t == "msinfo") Process.Start("msinfo32.exe");
                else if (t == "cleanmgr") Process.Start("cleanmgr.exe");
                else if (t == "appwiz") Process.Start("appwiz.cpl");
                else if (t == "restore") Process.Start("rstrui.exe");
                else if (t == "sysdm") Process.Start("sysdm.cpl", ",4");
                else if (t == "netcpl") Process.Start("ncpa.cpl");
                else if (t == "taskschd") Process.Start("taskschd.msc");
                else if (t == "wf") Process.Start("wf.msc");
                else if (t == "winver") Process.Start("winver.exe");
            }
            catch { }
        }

        public static void RestartExplorer()
        {
            try { Process.Start(new ProcessStartInfo("taskkill.exe", "/IM explorer.exe /F") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(5000); }
            catch { }
            System.Threading.Thread.Sleep(600);
            try { Process.Start("explorer.exe"); } catch { }
        }

        static readonly string[] IconClsids = {
            "{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            "{645FF040-5081-101B-9F08-00AA002F954E}",
            "{208D2C60-3AEA-1069-A2D8-08002B30309D}",
            "{59031a47-3f72-44a7-89c5-5595fe6b30ee}",
        };
        static readonly string[] IconNames = { "此电脑/计算机", "回收站", "网络", "用户文件夹" };
        public static string[] DesktopIcons(out bool[] visible)
        {
            visible = new bool[IconClsids.Length];
            string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
            for (int i = 0; i < IconClsids.Length; i++)
            {
                int? h = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default, keyPath, IconClsids[i]);
                visible[i] = h == null || h.Value == 0;
            }
            return IconNames;
        }
        public static string[] DesktopClsids() { return IconClsids; }
        public static string SetDesktopIcon(string clsid, bool show)
        {
            string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
            if (show) return Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, keyPath, clsid);
            return Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default, keyPath, clsid, 1);
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        public static void RefreshDesktop() { SHChangeNotify(0x8000000, 0, IntPtr.Zero, IntPtr.Zero); }

        public static string EnableLongPaths(bool on)
        {
            if (!OS.IsWin10Plus) return "仅 Windows 10/11 支持";
            return Regs.SetDword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", on ? 1 : 0);
        }
    }
}
