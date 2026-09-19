using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>
    /// 系统维护：组件存储清理、磁盘优化、还原点整理、转储策略、缓存重建、存储感知。
    /// 所有方法返回 null 表示成功，否则返回错误文本；调用方负责日志与提示。
    /// </summary>
    public static class MaintenanceService
    {
        // ── 组件存储（WinSxS）──
        public static string CleanWinSxS(bool resetBase, Action<string, bool> log)
        {
            string args = "/Online /Cleanup-Image /StartComponentCleanup" + (resetBase ? " /ResetBase" : "");
            if (log != null) log("dism " + args, false);
            return Runner.Run("dism.exe", args, 1800000);
        }

        public static string ComponentStoreState()
        {
            string outp = Runner.Run("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", 600000);
            if (string.IsNullOrEmpty(outp)) return "组件存储分析失败（需管理员）";
            return outp;
        }

        // ── 磁盘优化 / TRIM ──
        public static string OptimizeDisk(string drive, Action<string, bool> log)
        {
            if (log != null) log("defrag " + drive + " /O", false);
            return Runner.Run("defrag.exe", drive + " /O /V", 900000);
        }

        // ── 转储策略 ──
        public static string SetDumpPolicy(string kind, Action<string, bool> log)
        {
            int value;
            if (kind == "none") value = 0;
            else if (kind == "small") value = 3;
            else value = 2;      // kernel
            string e = Regs.SetDword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", value);
            if (log != null) log("崩溃转储策略 = " + kind + " (" + value + ")" + (e == null ? " 成功" : " 失败:" + e), e != null);
            return e;
        }

        public static string DumpPolicyName()
        {
            int? v = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled");
            if (!v.HasValue) return "未知";
            if (v.Value == 0) return "不写入转储";
            if (v.Value == 1) return "完整内存转储";
            if (v.Value == 2) return "内核内存转储";
            if (v.Value == 3) return "小内存转储(256KB)";
            if (v.Value == 7) return "自动内存转储";
            return "未知(" + v.Value + ")";
        }

        // ── 还原点整理：列出并删除除最新之外的旧还原点 ──
        public static List<string> ListRestorePoints()
        {
            var list = new List<string>();
            string outp = Runner.Run("vssadmin.exe", "list shadows /for=" + OS.SystemDrive, 20000);
            if (string.IsNullOrEmpty(outp)) return list;
            foreach (string raw in outp.Split('\n'))
            {
                string line = raw.Trim();
                if (line.IndexOf("Shadow Copy ID:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("卷影副本 ID", StringComparison.Ordinal) >= 0)
                {
                    int idx = line.IndexOf(':');
                    if (idx > 0) list.Add(line.Substring(idx + 1).Trim());
                }
            }
            return list;
        }

        public static string DeleteOldRestorePoints(Action<string, bool> log)
        {
            List<string> ids = ListRestorePoints();
            if (ids.Count <= 1)
            {
                if (log != null) log("可清理的旧还原点不足（当前 " + ids.Count + " 个）", false);
                return null;
            }
            bool allOk = true;
            for (int i = 1; i < ids.Count; i++)          // 保留第一个（最新）
            {
                string e = Runner.Run("vssadmin.exe", "delete shadows /shadow={" + ids[i] + "} /quiet", 60000);
                if (log != null) log("删除还原点 {" + ids[i] + "}: " + (e == null ? "成功" : e), e != null);
                if (e != null) allOk = false;
            }
            return allOk ? null : "部分还原点删除失败（需要管理员权限）";
        }

        // ── 缓存重建 ──
        public static string RebuildFontCache(Action<string, bool> log)
        {
            var errs = new List<string>();
            string stop = Runner.Run("net.exe", "stop FontCache", 60000);
            if (log != null) log("停止 FontCache: " + (stop == null ? "成功" : "已停止或未运行"), false);
            string dir = Path.Combine(CleanService.W, @"ServiceProfiles\LocalService\AppData\Local\FontCache");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) { errs.Add("删除字体缓存失败: " + ex.Message); }
            string start = Runner.Run("net.exe", "start FontCache", 60000);
            if (log != null) log("启动 FontCache: " + (start == null ? "成功" : "失败"), start != null);
            return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
        }

        public static string RebuildPerfCounters(Action<string, bool> log)
        {
            if (log != null) log("重建性能计数器 (lodctr /R)", false);
            return Runner.Run("lodctr.exe", "/R", 120000);
        }

        public static string RebuildWmiRepository(Action<string, bool> log)
        {
            var errs = new List<string>();
            if (log != null) log("校验 WMI 仓库", false);
            string v = Runner.Run("winmgmt.exe", "/verifyrepository", 300000);
            if (v != null) errs.Add(v);
            if (errs.Count > 0)
            {
                if (log != null) log("校验发现问题，执行修复 (salvagerepository)", true);
                string s = Runner.Run("winmgmt.exe", "/salvagerepository", 600000);
                if (s != null) errs.Add(s);
            }
            return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
        }

        public static string RestartExplorer(Action<string, bool> log)
        {
            try
            {
                SystemTools.RestartExplorer();
                if (log != null) log("已重启资源管理器", false);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ── 存储感知 ──
        public static bool StorageSenseEnabled()
        {
            int? v = Regs.Dword(RegistryHive.CurrentUser, RegistryView.Default,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01");
            return v.HasValue && v.Value == 1;
        }

        public static string SetStorageSense(bool on)
        {
            return Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", on ? 1 : 0);
        }

        // ── CompactOS ──
        public static string CompactOsQuery()
        {
            string outp = Runner.Run("compact.exe", "/compactos:query", 30000);
            if (string.IsNullOrEmpty(outp)) return "查询失败";
            if (outp.IndexOf("not", StringComparison.OrdinalIgnoreCase) >= 0) return "未启用";
            return "已启用";
        }

        public static string SetCompactOs(bool on)
        {
            return Runner.Run("compact.exe", on ? "/compactos:always" : "/compactos:never", 1800000);
        }
    }
}