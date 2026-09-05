using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 垃圾清理引擎（通用扫描/删除，不依赖特殊框架）═══
    public class CleanItem
    {
        public string Id, Name, Note;        // Note: 说明
        public bool Admin;                   // 需要管理员权限
        public bool IsDir;                   // 目录/文件型
        public string[] Paths;               // 支持 %VAR%
        public bool TopOnly;                 // 仅删除目录下直接文件（不递归，如缩略图缓存）
        public string Filter;                // 文件名过滤, 支持多个用 ';'，空=全部
        public string Kind;                  // dir / events / recycle / mru
        public bool Checked = true;
        public long Size;                    // 扫描结果
        public int Files;
        public bool Scanned;
        public string State;                 // 扫描时界面显示
    }

    public static class Cleaner
    {
        public static string W = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        static string UA() { return Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local"); }
        static string AP() { return Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }

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

        public static List<CleanItem> Defaults()
        {
            var L = new List<CleanItem>();
            L.Add(new CleanItem { Id = "tmpuser", Name = "用户临时文件", Note = "%TEMP% 下所有临时文件", IsDir = true, Paths = new[] { "%TEMP%" }, Filter = "" });
            L.Add(new CleanItem { Id = "tmpsys", Name = "系统临时文件", Note = "C:\\Windows\\Temp（需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\Temp" } });
            L.Add(new CleanItem { Id = "prefetch", Name = "预取缓存", Note = "加快开机预读产生的缓存文件（重启后重建）", IsDir = true, Admin = true, Paths = new[] { W + @"\Prefetch" }, Filter = "*.pf" });
            L.Add(new CleanItem { Id = "thumb", Name = "缩略图/图标缓存", Note = "Explorer 自动重建", IsDir = true, Paths = new[] { UA() + @"\Microsoft\Windows\Explorer" }, TopOnly = true, Filter = "thumbcache_*;iconcache_*" });
            L.Add(new CleanItem { Id = "recent", Name = "最近打开文件记录", Note = "开始菜单“最近使用”历史", IsDir = true, Paths = new[] { AP() + @"\Microsoft\Windows\Recent" }, Filter = "" });
            L.Add(new CleanItem { Id = "mru", Name = "运行/搜索历史(注册表)", Note = "运行框(RunMRU)、搜索词历史、地址栏历史", Kind = "mru" });
            L.Add(new CleanItem { Id = "upd", Name = "Windows 更新缓存", Note = "已下载更新包安装缓存（需管理员）", IsDir = true, Admin = true, Paths = new[] { W + @"\SoftwareDistribution\Download" } });
            L.Add(new CleanItem { Id = "dump", Name = "崩溃转储与错误报告", Note = "Minidump / MEMORY.DMP / WER 报告", IsDir = true, Admin = true, Paths = new[] {
                W + @"\Minidump", W + @"\MEMORY.DMP", UA() + @"\CrashDumps",
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Microsoft\Windows\WER" } });
            L.Add(new CleanItem { Id = "inet", Name = "IE/旧版浏览器缓存", Note = "INetCache 网页临时文件", IsDir = true, Paths = new[] { UA() + @"\Microsoft\Windows\INetCache" } });
            L.Add(new CleanItem { Id = "browser", Name = "Chrome/Edge 缓存", Note = "两家 Chromium 内核浏览器的缓存与着色器缓存（关闭浏览器时清理效果最好）", IsDir = true, Paths = new[] {
                UA() + @"\Google\Chrome\User Data", UA() + @"\Microsoft\Edge\User Data", UA() + @"\Chromium\User Data" },
                TopOnly = false, Kind = "browser" });
            L.Add(new CleanItem { Id = "shader", Name = "显卡着色器缓存", Note = "D3DSCache / NVIDIA / AMD / Intel GPU 缓存", IsDir = true, Paths = new[] {
                UA() + @"\D3DSCache", UA() + @"\NVIDIA", UA() + @"\AMD", UA() + @"\Intel\ShaderCache", UA() + @"\NVIDIA Corporation\NV_Cache" } });
            L.Add(new CleanItem { Id = "log", Name = "系统事件日志", Note = "清空 应用程序/系统/安全 事件日志（需管理员）", Kind = "events", Admin = true });
            L.Add(new CleanItem { Id = "bin", Name = "回收站", Note = "彻底清空所有回收站（不可恢复！）", Kind = "recycle", Admin = false, Checked = false });
            return L;
        }

        // ── 扫描 ──
        public static void Scan(CleanItem it, System.Threading.CancellationTokenSource cancel)
        {
            it.Size = 0; it.Files = 0; it.State = "";
            switch (it.Kind)
            {
                case "recycle":
                    var rb = new SHQUERYRBINFO();
                    rb.cbSize = Marshal.SizeOf(rb);
                    if (SHQueryRecycleBinW(null, ref rb) == 0)
                    { it.Size = rb.i64Size; it.Files = (int)rb.i64NumItems; }
                    break;
                case "events": it.Files = -1; break;
                case "mru":
                    it.Files = CountMrus();
                    break;
                default:
                    string[] dirs = ExpandAll(it.Paths);
                    if (it.Kind == "browser")
                    {
                        foreach (string d in dirs) ScanBrowserProfile(d, it, cancel);
                    }
                    else
                    {
                        foreach (string d in dirs) ScanDir(d, it, cancel, it.TopOnly);
                    }
                    break;
            }
            it.Scanned = true;
        }

        static void ScanBrowserProfile(string profileRoot, CleanItem it, System.Threading.CancellationTokenSource cancel)
        {
            // profileRoot = "...\User Data"，各 Profile 子目录下的 Cache/Code Cache/GPUCache/ShaderCache
            try
            {
                foreach (string sub in Directory.GetDirectories(profileRoot))
                {
                    string n = Path.GetFileName(sub).ToLower();
                    if (cancel != null && cancel.IsCancellationRequested) return;
                    foreach (string cacheName in new[] { "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache" })
                    {
                        string p = Path.Combine(sub, cacheName);
                        if (Directory.Exists(p)) ScanDir(p, it, cancel, false);
                    }
                }
            }
            catch { }
        }

        static void ScanDir(string dir, CleanItem it, System.Threading.CancellationTokenSource cancel, bool topOnly)
        {
            if (cancel != null && cancel.IsCancellationRequested) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                if (topOnly)
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        if (GlobMatch(Path.GetFileName(f), it.Filter))
                            it.Size += SizeOf(f, it);
                    }
                    return;
                }
                Walk(dir, it, cancel, true);
            }
            catch { }
        }

        static void Walk(string dir, CleanItem it, System.Threading.CancellationTokenSource cancel, bool root)
        {
            if (cancel != null && cancel.IsCancellationRequested) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                var di = new DirectoryInfo(dir);
                try
                {
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0 && !root) return; // 防死循环
                }
                catch { }
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (cancel != null && cancel.IsCancellationRequested) return;
                    string fn = Path.GetFileName(f);
                    if (string.IsNullOrEmpty(it.Filter) || GlobMatch(fn, it.Filter))
                        it.Size += SizeOf(f, it);
                }
                foreach (string d in Directory.GetDirectories(dir))
                    Walk(d, it, cancel, false);
                if (root) return;
                try { if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
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
                string re = "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(name, re, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        // ── 清理 ──
        public static void Clean(CleanItem it, LogBox log, System.Threading.CancellationTokenSource cancel)
        {
            switch (it.Kind)
            {
                case "recycle":
                    int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 /*SHERB_NOCONFIRMATION*/ | 0x2 /*SHERB_NOPROGRESSUI*/);
                    if (hr == 0) log.Log("回收站已清空");
                    else log.Log("清空回收站失败（HRESULT " + hr.ToString("X8") + "）", true);
                    break;
                case "events":
                    ClearEvents(log);
                    break;
                case "mru":
                    ClearMrus(log);
                    break;
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
                    log.Log(it.Name + "：清理完成");
                    break;
            }
        }

        static string[] SafeDirs(string root)
        {
            try { return Directory.GetDirectories(root); }
            catch { return new string[0]; }
        }

        static void RemoveTree(string root, LogBox log, System.Threading.CancellationTokenSource cancel)
        {
            try
            {
                if (!Directory.Exists(root)) { if (File.Exists(root)) TryDelFile(root, log); return; }
                foreach (string d in SafeDirs(root))
                {
                    if (cancel != null && cancel.IsCancellationRequested) return;
                    RemoveTree(d, log, cancel);
                }
                foreach (string f in Directory.GetFiles(root))
                {
                    if (cancel != null && cancel.IsCancellationRequested) return;
                    TryDelFile(f, log);
                }
                try { if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root); } catch { }
            }
            catch { }
        }

        static void RemoveTopFiles(string dir, CleanItem it, LogBox log, System.Threading.CancellationTokenSource cancel)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (cancel != null && cancel.IsCancellationRequested) return;
                    if (GlobMatch(Path.GetFileName(f), it.Filter)) TryDelFile(f, log);
                }
            }
            catch { }
        }

        static void TryDelFile(string f, LogBox log)
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
                    log.Log("跳过(占用/权限): " + Path.GetFileName(f), true);
            }
        }

        // ── 事件日志 ──
        static void ClearEvents(LogBox log)
        {
            foreach (string name in new[] { "Application", "System", "Security" })
            {
                var psi = new ProcessStartInfo("wevtutil.exe", "cl " + name)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default };
                try
                {
                    var p = Process.Start(psi);
                    string outS = p.StandardOutput.ReadToEnd();
                    string errS = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0) log.Log("事件日志 " + name + " 已清空");
                    else log.Log("事件日志 " + name + " 清理失败: " + errS.Trim(), true);
                }
                catch (Exception ex) { log.Log("无法调用 wevtutil: " + ex.Message, true); }
            }
        }

        // ── 注册表历史 ──
        static int CountMrus()
        {
            int n = 0;
            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU",
            };
            foreach (string k in keys)
            {
                foreach (string nm in Regs.ValueNames(RegistryHive.CurrentUser, RegistryView.Default, k))
                    if (!nm.StartsWith("MRUList")) n++;
            }
            return n;
        }
        static void ClearMrus(LogBox log)
        {
            string[] keyPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU",
            };
            foreach (string kp in keyPaths)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(kp, true))
                    {
                        if (key == null) continue;
                        foreach (string nm in key.GetValueNames()) { try { key.DeleteValue(nm, false); } catch { } }
                        foreach (string sk in key.GetSubKeyNames())
                            try { key.DeleteSubKeyTree(sk, false); } catch { }
                    }
                }
                catch { }
            }
            log.Log("已清除运行/搜索/地址栏等使用历史");
        }

        // ── P/Invoke 回收站 ──
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pInfo);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBinW(IntPtr hwnd, string pszRootPath, uint dwFlags);

        // ── 无效快捷方式扫描 ──
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
                    if (!File.Exists(target) && !Directory.Exists(target))
                        bad.Add(f);
                }
            }
            return bad;
        }

        static string ShortcutTarget(string lnkFile, Type shellType)
        {
            if (shellType == null) return null;
            object sh = null, sc = null;
            try
            {
                sh = Activator.CreateInstance(shellType);
                sc = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnkFile });
                var t = sc.GetType();
                var v = t.InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.GetProperty, null, sc, null);
                return v == null ? null : v.ToString();
            }
            catch { return null; }
            finally
            {
                if (sc != null) try { Marshal.FinalReleaseComObject(sc); } catch { }
                if (sh != null) try { Marshal.FinalReleaseComObject(sh); } catch { }
            }
        }
    }
}
