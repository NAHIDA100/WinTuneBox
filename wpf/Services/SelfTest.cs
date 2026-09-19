using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WinTune.Wpf
{
    /// <summary>命令行自检：只读检测（默认）；--selftest-write 追加注册表写-读-还原探针</summary>
    public static class SelfTest
    {
        public const string Version = "2.0.0.0";

        public static string Run()
        {
            var sb = new StringBuilder();
            Action<string> Line = delegate(string s) { sb.AppendLine(s); };

            Line("===== WinTuneBox 2.0 自检报告 =====");
            Line("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("版本: " + Version);
            Line("数据目录: " + AppPaths.Root);
            Line("");

            OS.Refresh();
            Line("[系统]");
            Line("  系统: " + OS.WinVerFull);
            Line("  Build: " + OS.Build + "  位数: " + (OS.IsX64 ? "x64" : "x86") + "  管理员: " + OS.IsElevated);
            Line("  CPU: " + OS.CpuName + " (" + OS.CpuThreads + " 线程)");
            Line("  内存: 总 " + (OS.TotalRam / 1024.0 / 1024 / 1024).ToString("F1") + " GB / 可用 " + (OS.FreeRam / 1024.0 / 1024 / 1024).ToString("F1") + " GB");
            Line("  外观能力: Mica=" + Dwm.MicaAvailable + " 圆角=" + Dwm.SupportsCorner + " 深色标题栏=" + Dwm.SupportsDarkTitleBar);
            Line("");

            Line("[磁盘]");
            foreach (OS.DiskItem d in OS.AllDisks())
                Line(string.Format("  {0} 总 {1:F1}GB 可用 {2:F1}GB ({3:F0}%)", d.Name, d.SizeGB, d.FreeGB, d.FreePercent));
            Line("");

            Line("[显卡]");
            foreach (string g in OS.Gpus) Line("  " + g);
            Line("");

            Line("[一键优化项检测]");
            int opt = 0, unopt = 0, partial = 0, na = 0;
            foreach (TweakItemVM t in BuildTweaks())
            {
                string s = t.Status == 1 ? "已优化" : (t.Status == 0 ? "待优化" : (t.Status == 2 ? "部分" : "不适用"));
                if (t.Status == 1) opt++; else if (t.Status == 0) unopt++; else if (t.Status == 2) partial++; else na++;
                Line(string.Format("  [{0}]{1,-24} {2}", t.Group, t.Title, s + (t.NeedsAdmin ? " (需管理员)" : "") + (t.Risky ? " (谨慎)" : "")));
            }
            Line(string.Format("  汇总: 共 {0} 项 — 已优化 {1} / 待优化 {2} / 部分 {3} / 不适用 {4}",
                opt + unopt + partial + na, opt, unopt, partial, na));
            Line("");

            Line("[垃圾清理扫描]（抽样）");
            string[] scanIds = { "tmpuser", "thumb", "dump", "shader", "browser", "appcache", "wel", "setuplogs" };
            foreach (CleanItem it in CleanService.Defaults())
            {
                if (Array.IndexOf(scanIds, it.Id) < 0) continue;
                try { CleanService.Scan(it, CancellationToken.None); }
                catch (Exception ex) { Line("  [扫描异常] " + it.Name + ": " + ex.Message); continue; }
                Line(string.Format("  {0,-18} {1,12} 文件 {2}", it.Name, CleanService.FormatSize(it.Size), it.Files));
            }
            Line("");

            Line("[存储分析]");
            try
            {
                List<StorageEntry> big = StorageService.TopDirectories(OS.SystemDrive, 6);
                foreach (StorageEntry e in big) Line(string.Format("  {0,-46} {1}", e.Path, CleanService.FormatSize(e.Size)));
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[安全体检（只读）]");
            try
            {
                foreach (SecurityItem s in SecurityService.Check()) Line(string.Format("  {0,-16} {1} — {2}", s.State, s.Title, s.Detail));
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[启动项]");
            try
            {
                List<StartupEntry> su = StartupService.GetAll();
                Line("  共 " + su.Count + " 项，启用 " + su.Count(delegate(StartupEntry e) { return e.Enabled; }) +
                     " / 禁用 " + su.Count(delegate(StartupEntry e) { return !e.Enabled; }));
                foreach (IGrouping<string, StartupEntry> g in su.GroupBy(delegate(StartupEntry e) { return e.Source; }))
                    Line("    " + g.Key + ": " + g.Count());
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[服务优化建议]");
            try
            {
                List<ServiceManager.SvcView> sv = ServiceManager.Evaluate();
                Line("  命中建议 " + sv.Count + " 项");
                foreach (ServiceManager.SvcView s in sv.Take(20))
                    Line(string.Format("    {0,-22} 当前={1} 建议={2} {3}", s.Name, s.CurStart, s.RecStart, s.Why));
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[网络]");
            try
            {
                foreach (AdapterInfo a in NetworkService.Adapters())
                    Line(string.Format("  {0,-20} {1,-12} {2} 网关[{3}] DNS[{4}] {5}Mbps",
                        a.Name, a.Status, a.Ip, a.Gateway, a.Dns, a.SpeedMbps));
                long ping = NetworkService.PingOnce("223.5.5.5", 3000);
                Line("  Ping 223.5.5.5: " + (ping < 0 ? "超时" : ping + " ms"));
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[已安装软件]");
            try
            {
                List<SoftInfo> soft = SoftwareService.ReadAll();
                Line("  Win32 程序: " + soft.Count + " 个");
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[电源计划]");
            try
            {
                string g, n;
                OS.ActivePowerScheme(out g, out n);
                Line("  当前: " + n + " (" + g + ")");
            }
            catch (Exception ex) { Line("  [异常] " + ex.Message); }
            Line("");

            Line("[目录]");
            Line("  备份: " + AppPaths.BackupDir);
            Line("  快照: " + AppPaths.SnapshotDir);
            Line("  日志: " + AppPaths.LogDir);
            Line("");
            Line("===== 自检完成 =====");
            return sb.ToString();
        }

        static List<TweakItemVM> BuildTweaks()
        {
            var list = new List<TweakItemVM>();
            foreach (OneOp op in TweakService.All) list.Add(new TweakItemVM(op));
            return list;
        }

        /// <summary>写-读-还原探针：仅 HKCU 项（不改动系统级设置），用于验证还原链路</summary>
        static void WriteProbe(StringBuilder sb, Action<string> Line)
        {
            Line("");
            Line("[写-读-还原探针]（仅用户级项，操作后立即还原）");
            int ok = 0, fail = 0, limit = 0;
            foreach (OneOp op in TweakService.All)
            {
                if (op.NeedsAdmin) continue;                     // 跳过系统级，避免无人值守环境改动 HKLM/服务
                if (op.Apply == null || op.Restore == null || op.Detect == null) continue;
                if (limit++ >= 12) break;
                try
                {
                    int before = op.Detect();
                    string e1 = op.Apply();
                    int after = op.Detect();
                    string e2 = op.Restore();
                    int back = op.Detect();
                    bool pass = (e1 == null) && (after == 1) && (e2 == null) && (back != 1);
                    if (pass) ok++; else fail++;
                    Line(string.Format("  {0} {1,-24} 前={2} 应用后={3} 还原后={4} {5}",
                        pass ? "PASS" : "FAIL", op.Title, before, after, back,
                        (e1 ?? "") + (e2 ?? "")));
                }
                catch (Exception ex)
                {
                    fail++;
                    Line("  FAIL " + op.Title + " 异常: " + ex.Message);
                }
            }
            Line("  探针结果: 通过 " + ok + " / 失败 " + fail);
        }

        public static string RunToFile(string path, bool writeProbe)
        {
            string report;
            try
            {
                var sb = new StringBuilder(Run());
                if (writeProbe) WriteProbe(sb, delegate(string s) { sb.AppendLine(s); });
                report = sb.ToString();
            }
            catch (Exception ex) { report = "自检发生致命错误: " + ex; }

            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    string dir = AppPaths.LogDir;
                    File.WriteAllText(Path.Combine(dir, "selftest-latest.txt"), report, new UTF8Encoding(true));
                    path = Path.Combine(dir, "selftest-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                }
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, report, new UTF8Encoding(true));
                return path;
            }
            catch (Exception ex)
            {
                try
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "wtb-selftest.txt");
                    File.WriteAllText(tmp, report, new UTF8Encoding(true));
                    return tmp;
                }
                catch { return "写入报告失败: " + ex.Message; }
            }
        }
    }
}