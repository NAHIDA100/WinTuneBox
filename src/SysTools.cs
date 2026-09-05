using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 系统工具：进程运行器 / 还原点 / 修复 / 电源 / 安全模式 ═══
    public static class Runner
    {
        /// <summary>隐藏运行外部命令并捕获输出（同步）</summary>
        public static string Run(string file, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default,
                };
                using (var p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return "执行超时被终止"; }
                    string all = (so + "\n" + se).Trim();
                    if (p.ExitCode == 0) return string.IsNullOrEmpty(all) ? null : all;
                    return "退出码 " + p.ExitCode + "\n" + all;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>异步运行并把输出逐行送到 log（admin 工具用）。
        /// onDone：进程真正结束后回调（先于内部清理执行），用于调用方清空自己的 Process 引用，
        /// 避免对已释放对象求值 HasExited 抛出“没有与此对象关联的进程”。</summary>
        public static Process RunAsync(string file, string args, LogBox log, string label, Action onDone)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            };
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) =>
                {
                    try { if (!string.IsNullOrEmpty(e.Data)) log.Log(e.Data, e.Data.IndexOf("错误", StringComparison.Ordinal) >= 0); }
                    catch { }
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    try { if (!string.IsNullOrEmpty(e.Data)) log.Log(e.Data, true); }
                    catch { }
                };
                p.Exited += (s, e) =>
                {
                    try
                    {
                        int code = p.ExitCode;    // 在 Dispose 前读取
                        log.Log(string.Format("[{0}] 结束，退出码 {1}", label, code));
                    }
                    catch { }
                    try { if (onDone != null) onDone(); } catch { }
                    try { p.Dispose(); } catch { }
                };
                if (!p.Start()) { log.Log(label + " 启动失败", true); return null; }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                log.Log("开始: " + label + "  (" + file + " " + args + ")");
                return p;
            }
            catch (Exception ex) { log.Log(label + " 失败: " + ex.Message, true); return null; }
        }
    }

    public static class SysTools
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
        struct STATEMGRSTATUS
        {
            public int nStatus;
            public int llSequenceNumber;
        }
        [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
        static extern int SRSetRestorePointW(ref RESTOREPOINTINFO info, out STATEMGRSTATUS status);

        public static string CreateRestorePoint(string desc)
        {
            var info = new RESTOREPOINTINFO();
            info.dwEventType = 100;   // BEGIN_SYSTEM_CHANGE
            info.dwRestorePtType = 0; // APPLICATION_INSTALL
            info.llSequenceNumber = 0;
            info.szDescription = desc;
            STATEMGRSTATUS st;
            int hr = SRSetRestorePointW(ref info, out st);
            // 提交
            if (hr == 0)
            {
                info.dwEventType = 101; // END_SYSTEM_CHANGE
                SRSetRestorePointW(ref info, out st);
                return null;
            }
            if (hr == unchecked((int)0x80070422)) return "系统还原服务未开启";
            if (hr == unchecked((int)0x80042308)) return "系统保护未开启（此盘需在“系统保护”中先启用）";
            return "创建失败 (0x" + hr.ToString("X8") + ")，请确认系统保护已开启";
        }

        public static string IsRestorePointSupported()
        {
            return Runner.Run("vssadmin.exe", "list shadowstorage", 15000);
        }

        // ── 内存整理 ──
        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        /// <summary>一轮工作集清理：逐进程 EmptyWorkingSet，校验返回；返回真正清理成功的进程数。</summary>
        public static int CleanAllMemory()
        {
            int ok = 0;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr h = OpenProcess(0x1F0FFF /*PROCESS_ALL_ACCESS*/, false, p.Id);
                        if (h == IntPtr.Zero)
                            h = OpenProcess(0x0400 | 0x0008 /*QUERY|QUERY_LIMITED*/, false, p.Id);
                        if (h != IntPtr.Zero)
                        {
                            try
                            {
                                // EmptyWorkingSet 需 PROCESS_SET_QUOTA，只有限查询句柄会失败 → 校验返回值避免虚报
                                if (EmptyWorkingSet(h) != 0) ok++;
                            }
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

        /// <summary>当前可用物理内存（MB）</summary>
        public static long AvailableMB()
        {
            OS.RefreshMem();
            return (long)(OS.FreeRam / (1024.0 * 1024.0));
        }

        /// <summary>
        /// 一次完整的“内存优化”（PCL 风格，效果对齐）：
        /// 三轮整理全部进程工作集（覆盖更多可让出页）→ 等待脏页写回（writeback 完成后
        /// 才真正计入可用内存）→ 对比前后可用内存得到本次清理量。
        /// 管理员身份下可清理 SYSTEM/服务进程，效果显著更好。
        /// 注意：请在后台线程调用。
        /// </summary>
        public static string OptimizeMemoryNow()
        {
            long before = AvailableMB();
            int n = 0;
            for (int round = 0; round < 3; round++)
            {
                n += CleanAllMemory();
                if (round < 2) System.Threading.Thread.Sleep(150);
            }
            // 等待被换出的脏页写回磁盘（写回完成才计入可用内存）
            System.Threading.Thread.Sleep(1800);
            long after = AvailableMB();
            long freedMb = after - before;
            if (freedMb < 0) freedMb = 0;
            string report = string.Format("整理 {0} 进程次 · 可用内存增加 {1}（{2} → {3} GB）",
                n,
                freedMb >= 1024 ? (freedMb / 1024.0).ToString("F2") + " GB" : freedMb + " MB",
                (before / 1024.0).ToString("F2"),
                (after / 1024.0).ToString("F2"));
            if (!OS.IsElevated)
                report += "\n（当前未提权：系统进程无法整理；以管理员身份运行后效果更佳）";
            return report;
        }

        // ── 电源计划 ──
        const string GUID_ULTIMATE = "e9c42d02-d5df-432d-9ed0-0f53b3d0b34e"; // 卓越性能(隐藏)

        /// <summary>切换电源计划。策略：先复用系统中已有的同名计划（避免反复复制出无数个“高性能”），
        /// 找不到才新建；卓越性能为隐藏计划直接激活其固定 GUID，失败再回退 SCHEME_MAX 复制。</summary>
        public static string SwitchPowerScheme(string targetName)
        {
            // targetName: 节能 / 平衡 / 高性能 / 卓越性能
            if (!OS.IsElevated)
                return "切换电源计划需要管理员权限。\n请先提权（概览页或左下角权限条），再试一次。";

            string curGuid, curName;
            OS.ActivePowerScheme(out curGuid, out curName);
            if (curName != null && curName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                return "当前已是“" + curName + "”，无需切换";

            // 1) 卓越性能：隐藏计划，尝试直接激活固定 GUID
            if (targetName == "卓越性能")
            {
                if (!OS.IsWin10Plus) return "卓越性能仅 Windows 10/11 提供，已为你改用“高性能”路径";
                string r0 = Runner.Run("powercfg.exe", "/setactive " + GUID_ULTIMATE, 10000);
                if (r0 == null) return null;   // 系统存在该隐藏计划，已切换成功
                // 回退 1：复制隐藏计划再激活
                string dup0 = Runner.Run("powercfg.exe", "/duplicatescheme " + GUID_ULTIMATE, 15000);
                string g0 = ExtractGuid(dup0);
                if (!string.IsNullOrEmpty(g0))
                {
                    string r1 = Runner.Run("powercfg.exe", "/setactive " + g0, 10000);
                    if (r1 == null) return null;
                }
                // 回退 2：Win11 24H2/25H2 已移除隐藏“卓越性能”方案（setactive 报“参数无效/退出码 1”），
                // 此时复制现有“高性能”并命名为“卓越性能-增强”顶上
                string listP = Runner.Run("powercfg.exe", "/list", 15000);
                string srcGuid = null;
                if (listP != null)
                {
                    foreach (string line in listP.Split('\n'))
                    {
                        if (line.IndexOf("高性能", StringComparison.Ordinal) >= 0)
                        {
                            srcGuid = ExtractGuid(line);
                            if (!string.IsNullOrEmpty(srcGuid)) break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(srcGuid))
                {
                    string dup2 = Runner.Run("powercfg.exe", "/duplicatescheme " + srcGuid, 15000);
                    string g2 = ExtractGuid(dup2);
                    if (!string.IsNullOrEmpty(g2))
                    {
                        Runner.Run("powercfg.exe", "/changename " + g2 + " 卓越性能-增强", 10000);
                        string r2 = Runner.Run("powercfg.exe", "/setactive " + g2, 10000);
                        if (r2 == null)
                            return "OK:当前 Windows 版本已不再内置“卓越性能”隐藏方案，已自动复制“高性能”为「卓越性能-增强」并启用（设置取向与高性能一致）。";
                    }
                }
                return "未能启用“卓越性能”（当前 Windows 版本未提供该隐藏方案，自动创建替代也失败）：\n" + (dup0 ?? r0);
            }

            // 2) 常规计划：先按名称复用现有计划
            string list = Runner.Run("powercfg.exe", "/list", 15000);
            if (list != null)
            {
                foreach (string line in list.Split('\n'))
                {
                    if (line.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string guid = ExtractGuid(line);
                        if (!string.IsNullOrEmpty(guid))
                        {
                            string r = Runner.Run("powercfg.exe", "/setactive " + guid, 10000);
                            if (r == null) return null;
                            return "切换失败: " + r;
                        }
                    }
                }
            }

            // 3) 系统确实没有 → 用别名复制一个再激活
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
            return "未能找到/创建“" + targetName + "”电源计划：\n" + outp;
        }

        static string ExtractGuid(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int p1 = text.IndexOf('{');
            int p2 = text.IndexOf('}', p1 + 1);
            if (p1 >= 0 && p2 > p1) return text.Substring(p1, p2 - p1 + 1);
            return null;
        }

        // ── 安全模式 ──
        public static string SetSafeBoot(bool on)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            if (on)
                return Runner.Run("bcdedit.exe", "/set {current} safeboot minimal", 10000);
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

        // ── 磁盘检查（计划下次重启） ──
        public static string ScheduleChkdsk(string drive)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            string outp = Runner.Run("cmd.exe", "/c echo y| chkdsk " + drive.TrimEnd('\\') + "\\ /f", 30000);
            if (outp == null) return null;             // 已计划
            if (outp.IndexOf("计划", StringComparison.Ordinal) >= 0 || outp.IndexOf("scheduled", StringComparison.OrdinalIgnoreCase) >= 0
                || outp.IndexOf("下次", StringComparison.Ordinal) >= 0)
                return null;
            return outp;
        }

        // ── 常用系统工具 ──
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
            }
            catch { }
        }

        // ── 重启资源管理器 ──
        public static void RestartExplorer()
        {
            try { Process.Start(new ProcessStartInfo("taskkill.exe", "/IM explorer.exe /F") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(5000); }
            catch { }
            System.Threading.Thread.Sleep(600);
            try { Process.Start("explorer.exe"); } catch { }
        }

        // ── 桌面图标显隐 ──
        static readonly string[] IconClsids = {
            "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // 计算机
            "{645FF040-5081-101B-9F08-00AA002F954E}", // 回收站
            "{208D2C60-3AEA-1069-A2D8-08002B30309D}", // 网络
            "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", // 用户
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

        public static string SetDesktopIcon(string clsid, bool show)
        {
            string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
            // 隐藏=1 显示=删除或0
            if (show) return Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, keyPath, clsid);
            return Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default, keyPath, clsid, 1);
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        public static void RefreshDesktop()
        {
            SHChangeNotify(0x8000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
        }

        // ── 禁用驱动签名/长路径等小开关（保留位置）──
        public static string EnableLongPaths(bool on)
        {
            if (!OS.IsWin10Plus) return "仅 Windows 10/11 支持";
            string err = Regs.SetDword(RegistryHive.LocalMachine, OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", on ? 1 : 0);
            return err;
        }
    }
}
