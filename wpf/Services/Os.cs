using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>系统/硬件信息：Win7 x86 → Win11 x64 通用，全部使用真实 API/注册表</summary>
    public static class OS
    {
        public static readonly int Build;
        public static readonly int Major;
        public static readonly int Minor;
        public static readonly bool IsX64;
        public static readonly bool IsWin7;
        public static readonly bool IsWin8Plus;
        public static readonly bool IsWin10Plus;
        public static readonly bool IsWin11;

        static OS()
        {
            try
            {
                var v = RtlGetRealVersion();
                Major = v.dwMajorVersion;
                Minor = v.dwMinorVersion;
                Build = v.dwBuildNumber;
            }
            catch
            {
                var v2 = Environment.OSVersion.Version;
                Major = v2.Major; Minor = v2.Minor; Build = v2.Build;
            }
            IsX64 = Environment.Is64BitOperatingSystem;
            IsWin7 = Major == 6 && Minor == 1;
            IsWin8Plus = (Major > 6) || (Major == 6 && Minor >= 2);
            IsWin10Plus = Major >= 10;
            IsWin11 = Build >= 22000;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OSVERSIONINFOEXW
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        static extern int RtlGetVersion(ref OSVERSIONINFOEXW v);

        static OSVERSIONINFOEXW RtlGetRealVersion()
        {
            var v = new OSVERSIONINFOEXW();
            v.dwOSVersionInfoSize = Marshal.SizeOf(v);
            if (RtlGetVersion(ref v) != 0) throw new InvalidOperationException();
            return v;
        }

        // ── 管理员权限 ──
        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_ELEVATION { public uint TokenIsElevated; }
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr tok, int cls, out TOKEN_ELEVATION info, int len, out int ret);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        public static bool IsElevated
        {
            get
            {
                try
                {
                    IntPtr tok;
                    if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out tok)) return false;
                    try
                    {
                        TOKEN_ELEVATION info;
                        int ret, sz = Marshal.SizeOf(typeof(TOKEN_ELEVATION));
                        if (!GetTokenInformation(tok, 20, out info, sz, out ret)) return false;
                        return info.TokenIsElevated != 0;
                    }
                    finally { CloseHandle(tok); }
                }
                catch { return false; }
            }
        }

        // ── 内存 ──
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lp);

        public static ulong TotalRam;
        public static ulong FreeRam;
        public static uint RamLoad;
        public static void RefreshMem()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(m);
            if (GlobalMemoryStatusEx(ref m)) { TotalRam = m.ullTotalPhys; FreeRam = m.ullAvailPhys; RamLoad = m.dwMemoryLoad; }
        }

        // ── 磁盘 ──
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetDiskFreeSpaceEx(string dir, out ulong free, out ulong total, out ulong freeAvail);
        public static bool DiskInfo(string root, out ulong free, out ulong total)
        {
            ulong f, t, a;
            if (GetDiskFreeSpaceEx(root, out f, out t, out a)) { free = f; total = t; return true; }
            free = total = 0; return false;
        }
        public static string SystemDrive { get { return Environment.GetEnvironmentVariable("SystemDrive") + "\\"; } }

        public class DiskItem
        {
            public string Root;
            public ulong Free;
            public ulong Total;
            public string Label;
            public int UsedPercent { get { return Total == 0 ? 0 : (int)((Total - Free) * 100 / Total); } }

            // 便利别名（UI 使用）
            public string Name { get { return Root; } }
            public string DriveType { get { return "本地磁盘"; } }
            public double SizeGB { get { return Total / 1024.0 / 1024 / 1024; } }
            public double FreeGB { get { return Free / 1024.0 / 1024 / 1024; } }
            public double FreePercent { get { return Total == 0 ? 0 : Free * 100.0 / Total; } }
        }
        public static List<DiskItem> AllDisks()
        {
            var list = new List<DiskItem>();
            try
            {
                foreach (var d in System.IO.DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != System.IO.DriveType.Fixed && d.DriveType != System.IO.DriveType.Removable) continue;
                        if (!d.IsReady) continue;
                        ulong f, t;
                        if (DiskInfo(d.RootDirectory.FullName, out f, out t))
                            list.Add(new DiskItem { Root = d.RootDirectory.FullName, Free = f, Total = t, Label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : d.VolumeLabel });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        // ── CPU 使用率 ──
        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long _i, _k, _u;
        static bool _first = true;
        public static int CpuPercent()
        {
            long i, k, u;
            if (!GetSystemTimes(out i, out k, out u)) return 0;
            if (_first) { _first = false; _i = i; _k = k; _u = u; return 0; }
            long di = i - _i, dk = k - _k, du = u - _u;
            _i = i; _k = k; _u = u;
            long total = di + dk + du;
            if (total <= 0) return 0;
            int p = (int)(100 - di * 100 / total);
            return p < 0 ? 0 : (p > 100 ? 100 : p);
        }

        // ── 开机时间 ──
        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();
        public static DateTime BootTime
        {
            get { try { return DateTime.Now.AddMilliseconds(-(double)GetTickCount64()); } catch { return DateTime.MinValue; } }
        }

        // ── 电池/笔记本 ──
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte SystemStatusFlag; public int BatteryLifeTime; public int BatteryFullLifeTime; }
        [DllImport("kernel32.dll")]
        static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS s);
        public static bool HasBattery
        {
            get
            {
                try
                {
                    var s = new SYSTEM_POWER_STATUS();
                    if (GetSystemPowerStatus(ref s)) return s.BatteryFlag != 128; // 128=无电池
                }
                catch { }
                return false;
            }
        }

        // ── 静态信息 ──
        public static string ProductName, EditionId, ReleaseName, CpuName, GpuName, GpuAll, WinVerFull;
        public static int CpuCores;
        public static DateTime InstallDate;

        static string ReadStr(RegistryKey k, string n) { try { return k.GetValue(n) as string; } catch { return null; } }

        public static void CollectInfo()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        ProductName = ReadStr(k, "ProductName");
                        EditionId = ReadStr(k, "EditionID");
                        string cv = ReadStr(k, "CurrentVersion");
                        string dis = ReadStr(k, "DisplayVersion");
                        string rl = ReadStr(k, "ReleaseId");
                        string ub = ReadStr(k, "UBR");
                        ReleaseName = dis ?? cv;
                        if (string.IsNullOrEmpty(ReleaseName)) ReleaseName = "";
                        string build = ReadStr(k, "CurrentBuildNumber") ?? Build.ToString();
                        string full = ProductName;
                        if (string.IsNullOrEmpty(full)) full = "Windows";
                        if (IsWin11 && (full.IndexOf("11") < 0)) full = "Windows 11 " + (EditionId ?? "");
                        full = full.Trim();
                        if (!string.IsNullOrEmpty(ReleaseName)) full += " " + ReleaseName;
                        full += " (内部版本 " + build + (string.IsNullOrEmpty(ub) ? "" : "." + ub) + ")";
                        WinVerFull = full;
                        long id = 0;
                        try { id = Convert.ToInt64(ReadStr(k, "InstallDate")); } catch { }
                        if (id > 0) { try { InstallDate = new DateTime(1970, 1, 1).AddSeconds(id).ToLocalTime(); } catch { } }
                    }
                }
            }
            catch { }

            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k != null) CpuName = (ReadStr(k, "ProcessorNameString") ?? "").Trim();
                }
            }
            catch { }
            CpuCores = Environment.ProcessorCount;

            try
            {
                var gpus = new List<string>();
                for (int i = 0; i < 16; i++)
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\" + i.ToString("D4")))
                    {
                        if (k == null) break;
                        string desc = ReadStr(k, "DriverDesc");
                        string vid = ReadStr(k, "ProviderName");
                        if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(vid))
                            gpus.Add(desc + " (" + vid + ")");
                    }
                }
                GpuAll = string.Join("  |  ", gpus.ToArray());
                if (gpus.Count > 0)
                {
                    string[] virtualKeys = {
                        "virtual", "mumu", "mu mu", "模拟", "remote", "rdp", "indirect",
                        "mirror", "basic display", "基本显示", "vmware svga", "vbox",
                        "virtualbox", "hyper-v", "qxl", "qemu", "parsec", "citrix",
                        "msmirror", "venview", "spacedesk", "duet display",
                        "vga display", "microsoft basic", "vda", "radmin", "ultravnc", "anydesk",
                        "iddcx", "luminoncore", "gameviewer",
                    };
                    Func<string, int> realRank = delegate(string s)
                    {
                        string t = s.ToLower();
                        if (t.IndexOf("nvidia") >= 0 || t.IndexOf("geforce") >= 0 || t.IndexOf("quadro") >= 0 ||
                            t.IndexOf("rtx") >= 0 || t.IndexOf("gtx") >= 0) return 1;
                        if (t.IndexOf("radeon") >= 0 || t.IndexOf("amd") >= 0 || t.IndexOf("ati") >= 0) return 2;
                        if (t.IndexOf("intel") >= 0 || t.IndexOf("arc") >= 0 || t.IndexOf("iris") >= 0 ||
                            t.IndexOf("uhd") >= 0) return 3;
                        return 9;
                    };
                    Func<string, bool> isVirtual = delegate(string s)
                    {
                        string t = s.ToLower();
                        foreach (string v in virtualKeys) if (t.IndexOf(v) >= 0) return true;
                        return false;
                    };
                    int best = 0;
                    string bestName = gpus[0];
                    for (int j = 0; j < gpus.Count; j++)
                    {
                        if (isVirtual(gpus[j])) continue;
                        int r = realRank(gpus[j]);
                        if (r < best || best == 0) { best = r; bestName = gpus[j]; }
                    }
                    GpuName = best == 0 ? gpus[0] : bestName;
                }
            }
            catch { }
        }

        /// <summary>电源计划：当前活动 GUID + 名称</summary>
        public static void ActivePowerScheme(out string guid, out string name)
        {
            guid = name = "";
            try
            {
                string s = Runner.Run("powercfg.exe", "/getactivescheme", 10000) ?? "";
                int g = s.IndexOf("{"); int g2 = s.IndexOf("}", g + 1);
                if (g >= 0 && g2 > g) guid = s.Substring(g, g2 - g + 1);
                string rest = s.Substring(Math.Min(s.Length, g2 + 1)).Trim();
                if (rest.StartsWith("(")) rest = rest.TrimStart('(').TrimEnd(')', ' ', '\r', '\n');
                name = rest;
            }
            catch { }
        }

        // ── UI 便利成员 ──
        public static int CpuThreads { get { return Environment.ProcessorCount; } }
        public static double RamUsagePercent
        {
            get { RefreshMem(); return RamLoad; }
        }
        public static List<string> Gpus
        {
            get
            {
                if (string.IsNullOrEmpty(GpuAll)) return new List<string>();
                return new List<string>(GpuAll.Split(new[] { "  |  " }, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        static bool _infoCollected;
        /// <summary>刷新内存负载并（仅一次）采集系统/硬件静态信息</summary>
        public static void Refresh()
        {
            RefreshMem();
            if (!_infoCollected)
            {
                CollectInfo();
                _infoCollected = true;
            }
        }
    }
}
