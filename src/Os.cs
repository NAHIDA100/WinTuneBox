using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTune
{
    /// <summary>操作系统 / 硬件信息（Win7 x86 → Win11 x64 通用，全部用真实 API/注册表）</summary>
    public static class OS
    {
        public static readonly int Build;
        public static readonly int Major;
        public static readonly int Minor;
        public static readonly bool IsX64;
        public static readonly bool IsWin7;
        public static readonly bool IsWin8Plus;   // 8.0+ (StartupApproved 可用)
        public static readonly bool IsWin10Plus;  // build >= 10240
        public static readonly bool IsWin11;      // build >= 22000

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
            if (Build < 10000) Build += 10000 * Major * 10; // 保险：10.x 构建号归一（不会走到）
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

        // ── 权限 ──
        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_ELEVATION { public uint TokenIsElevated; }
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr tok, int cls, out TOKEN_ELEVATION info, int len, out int ret);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static bool IsElevated
        {
            get
            {
                try
                {
                    IntPtr tok;
                    if (!OpenProcessToken(ProcessGetCurrent(), 0x0008 /*TOKEN_QUERY*/, out tok)) return false;
                    try
                    {
                        TOKEN_ELEVATION info;
                        int ret, sz = Marshal.SizeOf(typeof(TOKEN_ELEVATION));
                        if (!GetTokenInformation(tok, 20 /*TokenElevation*/, out info, sz, out ret)) return false;
                        return info.TokenIsElevated != 0;
                    }
                    finally { CloseHandle(tok); }
                }
                catch { return false; }
            }
        }
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
        static IntPtr ProcessGetCurrent() { return GetCurrentProcess(); }

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

        // ── CPU 使用率（GetSystemTimes 差值）──
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

        // ── 注册表静态信息 ──
        public static string ProductName, EditionId, ReleaseName, ArchName, CpuName, GpuName;
        public static string WinVerFull;   // 例: Windows 11 专业版 24H2 (Build 26100.…)
        public static int CpuCores;        // 逻辑处理器数
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
                        string cv = ReadStr(k, "CurrentVersion");       // Win11: 24H2
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
                for (int i = 0; i < 12; i++)
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\" + i.ToString("D4")))
                    {
                        if (k == null) break;
                        string desc = ReadStr(k, "DriverDesc");
                        string vid = ReadStr(k, "ProviderName");
                        if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(vid))
                        { GpuName = desc + " (" + vid + ")"; break; }
                    }
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
                var psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe", "/getactivescheme")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default,
                };
                var p = System.Diagnostics.Process.Start(psi);
                string s = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                int g = s.IndexOf("{"); int g2 = s.IndexOf("}", g + 1);
                if (g >= 0 && g2 > g) guid = s.Substring(g, g2 - g + 1);
                string rest = s.Substring(Math.Min(s.Length, g2 + 1)).Trim();
                if (rest.StartsWith("(")) rest = rest.TrimStart('(').TrimEnd(')', ' ', '\r', '\n');
                name = rest;
            }
            catch { }
        }
    }
}
