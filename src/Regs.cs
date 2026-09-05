using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WinTune
{
    /// <summary>注册表助手：自动区分 32/64 位视图，读出错吞掉返回默认值</summary>
    public static class Regs
    {
        public static RegistryView V64() { return OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default; }
        public static RegistryView V32() { return OS.IsX64 ? RegistryView.Registry32 : RegistryView.Default; }

        static RegistryKey Base(RegistryHive hive, RegistryView view)
        {
            try { return RegistryKey.OpenBaseKey(hive, view); }
            catch { return RegistryKey.OpenBaseKey(hive, RegistryView.Default); }
        }

        public static string Str(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                    return k == null ? null : (k.GetValue(name) as string);
            }
            catch { return null; }
        }
        public static int? Dword(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                {
                    if (k == null) return null;
                    object o = k.GetValue(name);
                    if (o == null) return null;
                    return Convert.ToInt32(o);
                }
            }
            catch { return null; }
        }
        public static object Val(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        public static string[] ValueNames(RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                    return k == null ? new string[0] : k.GetValueNames();
            }
            catch { return new string[0]; }
        }
        public static string[] SubKeys(RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                    return k == null ? new string[0] : k.GetSubKeyNames();
            }
            catch { return new string[0]; }
        }
        public static bool KeyExists(RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path)) return k != null;
            }
            catch { return false; }
        }

        public static string KeyPathRoot(RegistryHive hive)
        {
            return hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" :
                   hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : hive.ToString();
        }

        public static bool ExistsValue(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                    return k != null && k.GetValue(name, null) != null;
            }
            catch { return false; }
        }

        /// <summary>写 DWORD（自动创建路径）；返回 null=成功，否则错误信息</summary>
        public static string SetDword(RegistryHive hive, RegistryView view, string path, string name, int value)
        {
            return SetVal(hive, view, path, name, value, RegistryValueKind.DWord);
        }
        public static string SetStr(RegistryHive hive, RegistryView view, string path, string name, string value)
        {
            return SetVal(hive, view, path, name, value, RegistryValueKind.String);
        }
        public static string SetVal(RegistryHive hive, RegistryView view, string path, string name, object value, RegistryValueKind kind)
        {
            try
            {
                using (var k = Base(hive, view).CreateSubKey(path))
                {
                    if (k == null) return "无法创建注册表项: " + path;
                    k.SetValue(name, value, kind);
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>删除值（不报错，只报真正异常）</summary>
        public static string DelVal(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path, true))
                {
                    if (k != null && k.GetValue(name, null) != null) k.DeleteValue(name, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>删除整个键（含子键）</summary>
        public static string DelKey(RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var k = Base(hive, view))
                {
                    if (k.OpenSubKey(path) != null) k.DeleteSubKeyTree(path, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>列出某键下全部值的 KV 快照（用于备份/导出）</summary>
        public static List<KeyValuePair<string, object>> Snapshot(RegistryHive hive, RegistryView view, string path)
        {
            var list = new List<KeyValuePair<string, object>>();
            try
            {
                using (var k = Base(hive, view).OpenSubKey(path))
                {
                    if (k == null) return list;
                    foreach (string n in k.GetValueNames())
                    {
                        try { list.Add(new KeyValuePair<string, object>(n, k.GetValue(n))); }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        // ═══ 常见路径 ═══
        public const string HK_RUN      = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string HK_WOW_RUN  = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        public const string HK_UNINST   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        public const string HK_WOW_UNINST= @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        public const string HK_APPROVED = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
        public const string HK_SERVICES = @"SYSTEM\CurrentControlSet\Services";
        public const string HK_NTVER    = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        public const string HK_TCPIP    = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    }
}
