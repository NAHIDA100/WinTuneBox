using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>存储/残留条目（扫描结果）</summary>
    public class StorageEntry
    {
        public string Path { get; set; }
        public string Note { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// 存储分析与残留扫描：大目录/大文件统计、孤儿卸载项、失效计划任务。
    /// 全部为只读扫描；删除动作由调用方显式触发。
    /// </summary>
    public static class StorageService
    {
        /// <summary>统计目录大小（带时间预算，超时返回已统计值）</summary>
        public static long DirSize(string dir, CancellationToken cancel, int budgetMs)
        {
            long total = 0;
            int start = Environment.TickCount;
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                if (cancel.IsCancellationRequested) break;
                if (budgetMs > 0 && Environment.TickCount - start > budgetMs) break;
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
                try
                {
                    foreach (string d in Directory.GetDirectories(cur))
                    {
                        try
                        {
                            FileAttributes attr = File.GetAttributes(d);
                            if ((attr & FileAttributes.ReparsePoint) != 0) continue;   // 跳过符号链接，避免重复计算
                        }
                        catch { }
                        stack.Push(d);
                    }
                }
                catch { }
            }
            return total;
        }

        /// <summary>系统盘一级目录占用 Top-N（带时间预算）</summary>
        public static List<StorageEntry> TopDirectories(string root, int topN)
        {
            return TopDirectories(root, topN, 6000, CancellationToken.None);
        }

        public static List<StorageEntry> TopDirectories(string root, int topN, int budgetMs, CancellationToken cancel)
        {
            var list = new List<StorageEntry>();
            try
            {
                if (!root.EndsWith("\\")) root += "\\";
                string[] dirs = Directory.GetDirectories(root);
                int perDirBudget = Math.Max(1500, budgetMs / Math.Max(1, dirs.Length));
                foreach (string d in dirs)
                {
                    if (cancel.IsCancellationRequested) break;
                    string name = Path.GetFileName(d);
                    if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;
                    long size = DirSize(d, cancel, perDirBudget);
                    list.Add(new StorageEntry { Path = d, Size = size, Note = name });
                }
                list.Sort(delegate(StorageEntry a, StorageEntry b) { return b.Size.CompareTo(a.Size); });
                if (list.Count > topN) list = list.GetRange(0, topN);
            }
            catch (Exception ex) { Logger.Log("Storage", "目录统计失败: " + ex.Message); }
            return list;
        }

        /// <summary>指定目录下最大的文件 Top-N</summary>
        public static List<StorageEntry> TopFiles(string dir, int topN, CancellationToken cancel)
        {
            var found = new List<StorageEntry>();
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (cancel.IsCancellationRequested) break;
                    try
                    {
                        var fi = new FileInfo(f);
                        found.Add(new StorageEntry { Path = f, Size = fi.Length, Note = fi.LastWriteTime.ToString("yyyy-MM-dd") });
                    }
                    catch { }
                    if (found.Count > 4000) break;      // 防御性上限
                }
                found.Sort(delegate(StorageEntry a, StorageEntry b) { return b.Size.CompareTo(a.Size); });
                if (found.Count > topN) found = found.GetRange(0, topN);
            }
            catch (Exception ex) { Logger.Log("Storage", "文件统计失败: " + ex.Message); }
            return found;
        }

        /// <summary>扫描卸载注册表项：安装目录/卸载程序已不存在的“孤儿项”（只读）</summary>
        public static List<StorageEntry> FindOrphanUninstallEntries()
        {
            var list = new List<StorageEntry>();
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = OS.IsX64
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };
            string[] roots = { Regs.HK_UNINST, Regs.HK_WOW_UNINST };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    foreach (string root in roots)
                    {
                        foreach (string sub in Regs.SubKeys(hive, view, root))
                        {
                            string path = root + "\\" + sub;
                            try
                            {
                                string name = Regs.Str(hive, view, path, "DisplayName");
                                if (string.IsNullOrEmpty(name)) continue;
                                string loc = Regs.Str(hive, view, path, "InstallLocation");
                                string un = Regs.Str(hive, view, path, "UninstallString");
                                string sysComp = Regs.Dword(hive, view, path, "SystemComponent") == 1 ? "1" : null;
                                if (sysComp == "1") continue;

                                bool locMissing = !string.IsNullOrEmpty(loc) && !Directory.Exists(loc.Trim('"', ' '));
                                string exe = ExtractExe(un);
                                bool exeMissing = !string.IsNullOrEmpty(exe) && !File.Exists(exe);
                                if (locMissing || exeMissing)
                                {
                                    list.Add(new StorageEntry
                                    {
                                        Path = Regs.KeyPathRoot(hive) + "\\" + path,
                                        Note = name + (locMissing ? " · 安装目录缺失" : " · 卸载程序缺失"),
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>扫描计划任务：可执行文件已不存在的失效任务（只读）</summary>
        public static List<StorageEntry> FindLeftoverTasks()
        {
            var list = new List<StorageEntry>();
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\Tasks");
                if (!Directory.Exists(dir)) return list;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        string text = File.ReadAllText(f);
                        int i = text.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase);
                        if (i < 0) continue;
                        int j = text.IndexOf("</Command>", i, StringComparison.OrdinalIgnoreCase);
                        if (j <= i) continue;
                        string cmd = text.Substring(i + 9, j - i - 9).Trim();
                        if (cmd.Length == 0) continue;
                        if (cmd.StartsWith("%")) continue;                       // 含环境变量，跳过
                        if (File.Exists(cmd)) continue;
                        if (cmd.IndexOf('\\') < 0) continue;                     // 不是路径，跳过
                        list.Add(new StorageEntry
                        {
                            Path = f.Substring(dir.Length).TrimStart('\\'),
                            Note = "目标缺失: " + Path.GetFileName(cmd),
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Logger.Log("Storage", "计划任务扫描失败: " + ex.Message); }
            return list;
        }

        static string ExtractExe(string uninstallString)
        {
            if (string.IsNullOrEmpty(uninstallString)) return null;
            string s = uninstallString.Trim();
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                return end > 1 ? s.Substring(1, end - 1) : null;
            }
            int sp = s.IndexOf(" -");
            string cand = sp > 0 ? s.Substring(0, sp) : s;
            cand = cand.Replace("\"", "").Trim();
            return cand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? cand : null;
        }

        /// <summary>驱动/组件残留提示：DriverStore 中未被使用的驱动包（只读统计）</summary>
        public static string DriverStoreInfo()
        {
            try
            {
                string store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\DriverStore\\FileRepository");
                if (!Directory.Exists(store)) return "DriverStore 不可用";
                int dirs = Directory.GetDirectories(store).Length;
                return "驱动仓库包数量: " + dirs + "（建议用 pnputil /enum-drivers 查看未使用项）";
            }
            catch { return "DriverStore 读取失败"; }
        }
    }
}