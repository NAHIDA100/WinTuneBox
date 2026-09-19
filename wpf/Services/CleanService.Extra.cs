using System;
using System.Collections.Generic;
using System.IO;

namespace WinTune.Wpf
{
    /// <summary>清理引擎扩展：容量格式化 + 追加清理类目（应用缓存 / 日志 / 系统残留）</summary>
    public static partial class CleanService
    {
        /// <summary>字节数格式化（B/KB/MB/GB）</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
        }

        /// <summary>在 Defaults() 末尾追加扩展类目</summary>
        public static void AppendExtra(List<CleanItem> L)
        {
            string UA = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string AP = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

            L.Add(new CleanItem
            {
                Id = "appcache",
                Name = "第三方应用缓存",
                Note = "Discord / Teams / Steam / Office 文件缓存（不影响账号与设置）",
                IsDir = true,
                Paths = new[] {
                    UA + @"\Discord\Cache",
                    UA + @"\Microsoft\Teams\Cache",
                    UA + @"\Steam\htmlcache",
                    UA + @"\Steam\appcache",
                    UA + @"\Microsoft\Office\16.0\OfficeFileCache",
                    AP + @"\Microsoft\Teams\Cache",
                },
            });

            L.Add(new CleanItem
            {
                Id = "wel",
                Name = "应用错误报告(WER)",
                Note = "用户级 Windows 错误报告队列与归档",
                IsDir = true,
                Paths = new[] {
                    UA + @"\Microsoft\Windows\WER\ReportQueue",
                    UA + @"\Microsoft\Windows\WER\ReportArchive",
                    UA + @"\Microsoft\Windows\WER\Temp",
                },
            });

            L.Add(new CleanItem
            {
                Id = "setuplogs",
                Name = "安装/升级日志",
                Note = @"CBS / DISM / Panther 安装日志（需管理员）",
                IsDir = true,
                Admin = true,
                Paths = new[] {
                    W + @"\Logs\CBS",
                    W + @"\Logs\DISM",
                    W + @"\Panther",
                    W + @"\Logs\WindowsUpdate",
                },
            });

            L.Add(new CleanItem
            {
                Id = "fontcache",
                Name = "字体缓存",
                Note = "字体缓存文件（系统会自动重建，需管理员）",
                IsDir = true,
                Admin = true,
                Paths = new[] {
                    W + @"\ServiceProfiles\LocalService\AppData\Local\FontCache",
                },
            });

            L.Add(new CleanItem
            {
                Id = "upgraderesidue",
                Name = "系统升级残留",
                Note = @"$WINDOWS.~BT / $WINDOWS.~WS 升级临时目录（需管理员）",
                IsDir = true,
                Admin = true,
                Checked = false,
                Paths = new[] {
                    sysDrive + @"\$WINDOWS.~BT",
                    sysDrive + @"\$WINDOWS.~WS",
                },
            });

            L.Add(new CleanItem
            {
                Id = "eventlogbak",
                Name = "事件日志备份文件",
                Note = @"C:\Windows\System32\winevt\Logs 的 .bak/.old 残留（需管理员）",
                IsDir = true,
                Admin = true,
                Paths = new[] { W + @"\System32\winevt\Logs" },
                Filter = "*.bak;*.old",
            });
        }
    }
}