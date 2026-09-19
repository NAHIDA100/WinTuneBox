using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>备份工具：注册表值导出 .reg、文件复制、整键快照</summary>
    public static class Backup
    {
        public static string WriteRegBackup(bool localMachine, bool is32, string keyPath, string valueName, string tag)
        {
            try
            {
                RegistryHive hive = localMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                RegistryView view = (localMachine && is32 && OS.IsX64) ? RegistryView.Registry32
                    : (localMachine ? (OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default) : RegistryView.Default);
                object val = Regs.Val(hive, view, keyPath, valueName);
                if (val == null) return null;
                string file = Path.Combine(AppPaths.BackupDir, string.Format("{0}-{1:yyyyMMdd-HHmmss}.reg", tag, DateTime.Now));
                var sb = new StringBuilder();
                sb.AppendLine("Windows Registry Editor Version 5.00");
                string fullKey = Regs.KeyPathRoot(hive) + "\\" + keyPath;
                sb.AppendLine();
                sb.AppendLine("[" + fullKey + "]");
                sb.AppendLine(EscapeRegValue(valueName, val));
                File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
                return file;
            }
            catch { return null; }
        }

        static string EscapeRegValue(string name, object val)
        {
            string v = val as string;
            if (v != null)
            {
                return "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"=\"" +
                       v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            if (val is int)
            {
                return "\"" + name + "\"=dword:" + ((int)val).ToString("x8");
            }
            if (val is byte[])
            {
                var bytes = (byte[])val;
                var sb = new StringBuilder();
                sb.Append("\"" + name + "\"=hex:");
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
            return "\"" + name + "\"=\"" + Convert.ToString(val) + "\"";
        }

        public static string CopyToBackup(string file, string tag)
        {
            try
            {
                string dest = Path.Combine(AppPaths.BackupDir,
                    string.Format("{0}-{1:yyyyMMdd-HHmmss}-{2}", tag, DateTime.Now, Path.GetFileName(file)));
                File.Copy(file, dest, true);
                return dest;
            }
            catch { return null; }
        }
    }
}
