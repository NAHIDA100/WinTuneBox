using System;
using System.IO;
using System.Text;

namespace WinTune.Wpf
{
    /// <summary>轻量文件日志（按天滚动）</summary>
    public static class Logger
    {
        static readonly object _lock = new object();

        public static string CurrentFile
        {
            get { return Path.Combine(AppPaths.LogDir, "log-" + DateTime.Now.ToString("yyyyMMdd") + ".txt"); }
        }

        public static void Log(string msg)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(CurrentFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void Log(string category, string msg) { Log("[" + category + "] " + msg); }
    }
}