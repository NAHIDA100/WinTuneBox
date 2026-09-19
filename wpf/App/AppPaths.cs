using System;
using System.IO;

namespace WinTune.Wpf
{
    /// <summary>应用数据目录：设置 / 备份 / 快照 / 日志，统一在 %LOCALAPPDATA%\WinTuneBox</summary>
    public static class AppPaths
    {
        public const string ProductName = "WinTuneBox";
        public const string OldProductName = "WinTuneBox2";   // v2 预览版数据目录（一次性迁移）
        public const string DisplayName = "WinTuneBox 优化工具箱";

        public static string Root
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);
                TryCreate(d);
                return d;
            }
        }

        public static string OldRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OldProductName);
            }
        }

        public static string BackupDir { get { return Ensure("backups"); } }
        public static string SnapshotDir { get { return Ensure("snapshots"); } }
        public static string LogDir { get { return Ensure("logs"); } }
        public static string TempDir { get { return Ensure("tmp"); } }

        static string Ensure(string sub)
        {
            string d = Path.Combine(Root, sub);
            TryCreate(d);
            return d;
        }

        static void TryCreate(string d)
        {
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
        }

        public static void OpenInExplorer(string dir)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        /// <summary>把旧版（WinTuneBox2）快照与备份搬到新目录，旧目录保留不动</summary>
        public static void MigrateOldData()
        {
            try
            {
                if (!Directory.Exists(OldRoot)) return;
                CopyTree(Path.Combine(OldRoot, "snapshots"), SnapshotDir);
                CopyTree(Path.Combine(OldRoot, "backups"), BackupDir);
                Logger.Log("Migrate", "已从 " + OldRoot + " 迁移历史数据");
            }
            catch (Exception ex) { Logger.Log("Migrate", "迁移失败: " + ex.Message); }
        }

        static void CopyTree(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return;
                if (!Directory.Exists(to)) Directory.CreateDirectory(to);
                foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                {
                    string rel = dir.Substring(from.Length).TrimStart('\\');
                    string target = Path.Combine(to, rel);
                    if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                }
                foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(from.Length).TrimStart('\\');
                    string target = Path.Combine(to, rel);
                    if (!File.Exists(target)) File.Copy(file, target, false);
                }
            }
            catch { }
        }
    }
}