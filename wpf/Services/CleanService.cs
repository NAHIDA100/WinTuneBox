using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public class CleanItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
        public bool Admin { get; set; }
        public bool IsDir { get; set; }
        public string[] Paths { get; set; }
        public bool TopOnly { get; set; }
        public string Filter { get; set; }
        public string Kind { get; set; }  // dir / events / recycle / mru / shortcuts / special
        bool _checked = true;
        public bool Checked { get { return _checked; } set { _checked = value; } }
        public long Size { get; set; }
        public int Files { get; set; }
        public bool Scanned { get; set; }
    }

    /// <summary>垃圾清理引擎：通用扫描/删除，被占用或无权限文件安全跳过</summary>
    public static partial class CleanService
    {
        public static string W = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        static string UA() { return Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local"); }
        static string AP() { return Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }

        public static List<CleanItem> Defaults()
        {
            var L = new List<CleanItem>();
            L.Add(new CleanItem { Id = "tmpuser", Name = "用户临时文件", Note = "%TEMP% 下所有临时文件", IsDir = true, Paths = new[] { "%TEMP%" } });
            L.Add(new CleanItem { Id = "tmpsys", Name = "系统临时文件", Note = @"C:\Windows\Temp（需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\Temp" } });
            L.Add(new CleanItem { Id = "prefetch", Name = "预取缓存", Note = "开机预读缓存（重启后重建，需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\Prefetch" }, Filter = "*.pf" });
            L.Add(new CleanItem { Id = "thumb", Name = "缩略图/图标缓存", Note = "Explorer 自动重建", IsDir = true, Paths = new[] { UA() + @"\Microsoft\Windows\Explorer" }, TopOnly = true, Filter = "thumbcache_*;iconcache_*" });
            L.Add(new CleanItem { Id = "recent", Name = "最近打开文件记录", Note = "开始菜单最近使用历史", IsDir = true, Paths = new[] { AP() + @"\Microsoft\Windows\Recent" } });
            L.Add(new CleanItem { Id = "mru", Name = "运行/搜索历史(注册表)", Note = "运行框、搜索词、地址栏历史", Kind = "mru" });
            L.Add(new CleanItem { Id = "upd", Name = "Windows 更新缓存", Note = "已下载更新安装缓存（需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\SoftwareDistribution\Download" } });
            L.Add(new CleanItem { Id = "do", Name = "传递优化缓存", Note = "P2P 分发缓存文件（需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\SoftwareDistribution\DeliveryOptimization" } });
            L.Add(new CleanItem { Id = "dump", Name = "崩溃转储与错误报告", Note = "Minidump / MEMORY.DMP / WER 报告（需管理员）", IsDir = true, Admin = true, Paths = new[] {
                W + @"\Minidump", W + @"\MEMORY.DMP", UA() + @"\CrashDumps",
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Microsoft\Windows\WER" } });
            L.Add(new CleanItem { Id = "inet", Name = "IE/旧版浏览器缓存", Note = "INetCache 网页临时文件", IsDir = true, Paths = new[] { UA() + @"\Microsoft\Windows\INetCache" } });
            L.Add(new CleanItem { Id = "browser", Name = "Chrome/Edge 缓存", Note = "Chromium 内核浏览器缓存（关闭浏览器时清理最佳）", IsDir = true, Paths = new[] {
                UA() + @"\Google\Chrome\User Data", UA() + @"\Microsoft\Edge\User Data", UA() + @"\Chromium\User Data" }, Kind = "browser" });
            L.Add(new CleanItem { Id = "shader", Name = "显卡着色器缓存", Note = "D3DSCache / NVIDIA / AMD / Intel GPU 缓存", IsDir = true, Paths = new[] {
                UA() + @"\D3DSCache", UA() + @"\NVIDIA", UA() + @"\AMD", UA() + @"\Intel\ShaderCache", UA() + @"\NVIDIA Corporation\NV_Cache" } });
            L.Add(new CleanItem { Id = "shortcuts", Name = "无效快捷方式", Note = "扫描开始菜单/桌面指向已失效目标的快捷方式", Kind = "shortcuts" });
            L.Add(new CleanItem { Id = "log", Name = "系统事件日志", Note = "清空应用程序/系统/安全事件日志（需管理员）", Kind = "events", Admin = true });
            L.Add(new CleanItem { Id = "bin", Name = "回收站", Note = "彻底清空所有回收站（不可恢复！）", Kind = "recycle", Checked = false });
            L.Add(new CleanItem { Id = "old", Name = "Windows.old 旧系统", Note = "升级系统后的旧系统备份，删除后无法回退到旧版本（需管理员）", Kind = "windowsold", Admin = true, Checked = false,
                IsDir = true, Paths = new[] { Environment.GetEnvironmentVariable("SystemDrive") + @"\Windows.old" } });
            AppendExtra(L);
            return L;
        }

        static string[] Expand(string p)
        {
            try
            {
                p = Environment.ExpandEnvironmentVariables(p);
                if (p.IndexOf('%') >= 0) return new string[0];
                return Directory.Exists(p) || File.Exists(p) ? new[] { p } : new string[0];
            }
            catch { return new string[0]; }
        }
        static string[] ExpandAll(string[] ps)
        {
            var list = new List<string>();
            foreach (var p in ps) list.AddRange(Expand(p));
            return list.ToArray();
        }

        public static void Scan(CleanItem it, CancellationToken cancel)
        {
            it.Size = 0; it.Files = 0; it.Scanned = false;
            switch (it.Kind)
            {
                case "recycle":
                    var rb = new SHQUERYRBINFO();
                    rb.cbSize = Marshal.SizeOf(rb);
                    if (SHQueryRecycleBinW(null, ref rb) == 0) { it.Size = rb.i64Size; it.Files = (int)rb.i64NumItems; }
                    break;
                case "events": it.Files = -1; break;
                case "mru": it.Files = CountMrus(); break;
                case "shortcuts":
                    int stTotal;
                    it.Files = FindInvalidShortcuts(out stTotal).Count;
                    break;
                default:
                    string[] dirs = ExpandAll(it.Paths);
                    if (it.Kind == "browser") { foreach (string d in dirs) ScanBrowserProfile(d, it, cancel); }
                    else { foreach (string d in dirs) ScanDir(d, it, cancel, it.TopOnly); }
                    break;
            }
            it.Scanned = true;
        }

        static void ScanBrowserProfile(string profileRoot, CleanItem it, CancellationToken cancel)
        {
            try
            {
                foreach (string sub in Directory.GetDirectories(profileRoot))
                {
                    if (cancel.IsCancellationRequested) return;
                    foreach (string cacheName in new[] { "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache" })
                    {
                        string p = Path.Combine(sub, cacheName);
                        if (Directory.Exists(p)) ScanDir(p, it, cancel, false);
                    }
                }
            }
            catch { }
        }

        static void ScanDir(string dir, CleanItem it, CancellationToken cancel, bool topOnly)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                if (!Directory.Exists(dir))
                {
                    if (File.Exists(dir)) it.Size += SizeOf(dir, it);
                    return;
                }
                if (topOnly)
                {
                    foreach (string f in Directory.GetFiles(dir))
                        if (GlobMatch(Path.GetFileName(f), it.Filter)) it.Size += SizeOf(f, it);
                    return;
                }
                Walk(dir, it, cancel, true, false);
            }
            catch { }
        }

        static void Walk(string dir, CleanItem it, CancellationToken cancel, bool root, bool deleting)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                var di = new DirectoryInfo(dir);
                try { if ((di.Attributes & FileAttributes.ReparsePoint) != 0 && !root) return; } catch { }
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (cancel.IsCancellationRequested) return;
                    string fn = Path.GetFileName(f);
                    if (!deleting && (string.IsNullOrEmpty(it.Filter) || GlobMatch(fn, it.Filter)))
                        it.Size += SizeOf(f, it);
                }
                foreach (string d in Directory.GetDirectories(dir))
                    Walk(d, it, cancel, false, deleting);
            }
            catch { }
        }

        static long SizeOf(string f, CleanItem it)
        {
            try { var fi = new FileInfo(f); it.Files++; return fi.Length; }
            catch { return 0; }
        }

        public static bool GlobMatch(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            foreach (string pat in filter.Split(';'))
            {
                string p = pat.Trim();
                if (p.Length == 0) continue;
                if (p == "*") return true;
                string re = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(name, re, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        public static void Clean(CleanItem it, Action<string, bool> log, CancellationToken cancel)
        {
            switch (it.Kind)
            {
                case "recycle":
                    int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2);
                    if (log != null) log(hr == 0 ? "回收站已清空" : "清空回收站失败（HRESULT " + hr.ToString("X8") + "）", hr != 0);
                    break;
                case "events": ClearEvents(log); break;
                case "mru": ClearMrus(log); break;
                case "shortcuts": CleanShortcuts(log, cancel); break;
                case "windowsold": CleanWindowsOld(log, cancel); break;
                default:
                    string[] dirs = ExpandAll(it.Paths);
                    if (it.Kind == "browser")
                    {
                        foreach (string d in dirs)
                            foreach (string sub in SafeDirs(d))
                                foreach (string cacheName in new[] { "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache" })
                                {
                                    string p = Path.Combine(sub, cacheName);
                                    if (Directory.Exists(p)) RemoveTree(p, log, cancel);
                                }
                    }
                    else
                    {
                        foreach (string d in dirs)
                        {
                            if (it.TopOnly) RemoveTopFiles(d, it, log, cancel);
                            else RemoveTree(d, log, cancel);
                        }
                    }
                    if (log != null) log(it.Name + "：清理完成", false);
                    break;
            }
        }

        static string[] SafeDirs(string root)
        {
            try { return Directory.GetDirectories(root); }
            catch { return new string[0]; }
        }

        static void RemoveTree(string root, Action<string, bool> log, CancellationToken cancel)
        {
            try
            {
                if (!Directory.Exists(root)) { if (File.Exists(root)) TryDelFile(root, log); return; }
                foreach (string d in SafeDirs(root))
                {
                    if (cancel.IsCancellationRequested) return;
                    RemoveTree(d, log, cancel);
                }
                foreach (string f in Directory.GetFiles(root))
                {
                    if (cancel.IsCancellationRequested) return;
                    TryDelFile(f, log);
                }
                try { if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root); } catch { }
            }
            catch { }
        }

        static void RemoveTopFiles(string dir, CleanItem it, Action<string, bool> log, CancellationToken cancel)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (cancel.IsCancellationRequested) return;
                    if (GlobMatch(Path.GetFileName(f), it.Filter)) TryDelFile(f, log);
                }
            }
            catch { }
        }

        static void TryDelFile(string f, Action<string, bool> log)
        {
            try
            {
                var a = File.GetAttributes(f);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                File.Delete(f);
            }
            catch (Exception ex)
            {
                if (log != null && !(ex is IOException) && !(ex is UnauthorizedAccessException))
                    log("跳过(占用/权限): " + Path.GetFileName(f), true);
            }
        }

        static void ClearEvents(Action<string, bool> log)
        {
            foreach (string name in new[] { "Application", "System", "Security" })
            {
                var psi = new ProcessStartInfo("wevtutil.exe", "cl " + name)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default };
                try
                {
                    var p = Process.Start(psi);
                    string errS = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (log != null) log(p.ExitCode == 0 ? "事件日志 " + name + " 已清空" : "事件日志 " + name + " 清理失败: " + errS.Trim(), p.ExitCode != 0);
                }
                catch (Exception ex) { if (log != null) log("无法调用 wevtutil: " + ex.Message, true); }
            }
        }

        static readonly string[] MruKeys = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU",
        };
        static int CountMrus()
        {
            int n = 0;
            foreach (string k in MruKeys)
                foreach (string nm in Regs.ValueNames(RegistryHive.CurrentUser, RegistryView.Default, k))
                    if (!nm.StartsWith("MRUList")) n++;
            return n;
        }
        static void ClearMrus(Action<string, bool> log)
        {
            foreach (string kp in MruKeys)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(kp, true))
                    {
                        if (key == null) continue;
                        foreach (string nm in key.GetValueNames()) { try { key.DeleteValue(nm, false); } catch { } }
                        foreach (string sk in key.GetSubKeyNames()) { try { key.DeleteSubKeyTree(sk, false); } catch { } }
                    }
                }
                catch { }
            }
            if (log != null) log("已清除运行/搜索/地址栏等使用历史", false);
        }

        static void CleanWindowsOld(Action<string, bool> log, CancellationToken cancel)
        {
            string dir = Environment.GetEnvironmentVariable("SystemDrive") + @"\Windows.old";
            if (!Directory.Exists(dir)) { if (log != null) log("未发现 Windows.old", false); return; }
            // 优先用系统自带方式（会处理权限），失败再手动删除
            string outp = Runner.Run("cmd.exe", "/c takeown /F \"" + dir + "\" /R /D Y >nul 2>&1 & icacls \"" + dir + "\" /grant administrators:F /T >nul 2>&1 & rmdir /S /Q \"" + dir + "\"", 180000);
            if (!Directory.Exists(dir)) { if (log != null) log("Windows.old 已删除", false); }
            else { RemoveTree(dir, log, cancel); if (log != null) log(Directory.Exists(dir) ? "Windows.old 部分文件被占用，已清理可删除部分" : "Windows.old 已删除", Directory.Exists(dir)); }
        }

        // ── 无效快捷方式 ──
        public static List<string> FindInvalidShortcuts(out int total)
        {
            var bad = new List<string>();
            total = 0;
            string[] roots = {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            };
            Type shell = null;
            try { shell = Type.GetTypeFromProgID("WScript.Shell"); } catch { }
            foreach (string root in roots)
            {
                if (root == null || !Directory.Exists(root)) continue;
                foreach (string f in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    total++;
                    string target = ShortcutTarget(f, shell);
                    if (string.IsNullOrEmpty(target)) continue;
                    if (target.StartsWith("{")) continue;
                    if (!File.Exists(target) && !Directory.Exists(target)) bad.Add(f);
                }
            }
            return bad;
        }

        static void CleanShortcuts(Action<string, bool> log, CancellationToken cancel)
        {
            int total;
            var bad = FindInvalidShortcuts(out total);
            int del = 0;
            foreach (string f in bad)
            {
                if (cancel.IsCancellationRequested) break;
                try { string bk = Backup.CopyToBackup(f, "shortcut"); File.Delete(f); del++; } catch { }
            }
            if (log != null) log(string.Format("无效快捷方式：扫描 {0} 个，删除 {1} 个（已备份）", total, del), false);
        }

        static string ShortcutTarget(string lnkFile, Type shellType)
        {
            if (shellType == null) return null;
            object sh = null, sc = null;
            try
            {
                sh = Activator.CreateInstance(shellType);
                sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnkFile });
                var v = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                return v == null ? null : v.ToString();
            }
            catch { return null; }
            finally
            {
                if (sc != null) try { Marshal.FinalReleaseComObject(sc); } catch { }
                if (sh != null) try { Marshal.FinalReleaseComObject(sh); } catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pInfo);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBinW(IntPtr hwnd, string pszRootPath, uint dwFlags);
    }
}
