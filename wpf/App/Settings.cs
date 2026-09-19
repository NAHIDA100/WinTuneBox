using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>settings.json 键值存储（取代旧版注册表偏好），并负责旧版本数据/自启项迁移</summary>
    public static class Settings
    {
        const string OldPrefKey = @"Software\WinTuneBox2";   // 旧版 WPF 预览版偏好
        const string V1RunValue = "WinOptimizer";            // v1 WinForms 开机自启项
        const string OldRunValue = "WinTuneBox2";            // 旧版预览开机自启项
        public const string RunValue = "WinTuneBox";         // 本版开机自启项

        public const string KeyTheme = "Theme";
        public const string KeyAccent = "Accent";
        public const string KeyMica = "Mica";
        public const string KeyFloatBall = "FloatBall";
        public const string KeyMinToTray = "MinToTray";
        public const string KeyAutoStart = "AutoStart";
        public const string KeyDisclaimer = "Disclaimer";
        public const string KeyLastPage = "LastPage";
        const string KeyMigrated = "Migrated";

        static readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object _lock = new object();
        static bool _loaded;

        public static string FilePath { get { return Path.Combine(AppPaths.Root, "settings.json"); } }

        public static string Get(string key, string def)
        {
            lock (_lock)
            {
                EnsureLoaded();
                string v;
                return _map.TryGetValue(key, out v) ? v : def;
            }
        }

        public static bool GetBool(string key, bool def) { return Get(key, def ? "1" : "0") == "1"; }
        public static int GetInt(string key, int def)
        {
            int x;
            return int.TryParse(Get(key, def.ToString()), out x) ? x : def;
        }

        public static void Set(string key, string value)
        {
            lock (_lock) { EnsureLoaded(); _map[key] = value; SaveLocked(); }
        }
        public static void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }
        public static void SetInt(string key, int value) { Set(key, value.ToString()); }

        public static void Save()
        {
            lock (_lock) { EnsureLoaded(); SaveLocked(); }
        }

        static void SaveLocked()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                int i = 0;
                foreach (KeyValuePair<string, string> kv in _map)
                {
                    sb.Append("  \"").Append(Escape(kv.Key)).Append("\": \"").Append(Escape(kv.Value)).Append("\"");
                    if (++i < _map.Count) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("}\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Logger.Log("Settings", "保存失败: " + ex.Message); }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                foreach (KeyValuePair<string, string> kv in Parse(text)) _map[kv.Key] = kv.Value;
            }
            catch (Exception ex) { Logger.Log("Settings", "读取失败: " + ex.Message); }
        }

        /// <summary>极简 JSON 对象解析：只处理字符串键值，容错优先</summary>
        static List<KeyValuePair<string, string>> Parse(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = text.IndexOf('{');
            if (i < 0) return list;
            i++;
            while (i < text.Length)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
                if (i >= text.Length || text[i] == '}') break;
                if (text[i] != '"') { i++; continue; }
                string key = ReadString(text, ref i);
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ':')) i++;
                if (i >= text.Length) break;
                string val;
                if (text[i] == '"') val = ReadString(text, ref i);
                else
                {
                    int start = i;
                    while (i < text.Length && text[i] != ',' && text[i] != '}') i++;
                    val = text.Substring(start, i - start).Trim();
                }
                if (key != null) list.Add(new KeyValuePair<string, string>(key, val));
            }
            return list;
        }

        static string ReadString(string text, ref int i)
        {
            var sb = new StringBuilder();
            i++; // 跳过起始引号
            while (i < text.Length)
            {
                char ch = text[i++];
                if (ch == '"') break;
                if (ch == '\\' && i < text.Length)
                {
                    char n = text[i++];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>首次运行迁移：旧版注册表偏好 + 自启项 + 历史快照</summary>
        public static void Migrate()
        {
            try
            {
                if (GetBool(KeyMigrated, false)) return;

                string theme = null;
                bool autoStart = false;
                int disclaimer = 0, floatBall = 1, minToTray = 1;

                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(OldPrefKey))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("Dark");
                        if (v != null) theme = Convert.ToInt32(v) == 1 ? "dark" : "light";
                        v = k.GetValue("DisclaimerShown");
                        if (v != null) disclaimer = 1;
                        v = k.GetValue("FloatBall");
                        if (v != null) floatBall = Convert.ToInt32(v);
                        v = k.GetValue("MinToTray");
                        if (v != null) minToTray = Convert.ToInt32(v);
                    }
                }

                if (theme != null) Set(KeyTheme, theme);
                if (disclaimer == 1) SetBool(KeyDisclaimer, true);

                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(Regs.HK_RUN))
                {
                    if (run != null)
                    {
                        if (run.GetValue(V1RunValue) != null || run.GetValue(OldRunValue) != null) autoStart = true;
                    }
                }

                Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, Regs.HK_RUN, V1RunValue);
                Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, Regs.HK_RUN, OldRunValue);

                if (autoStart)
                {
                    SetAutoStart(true);
                    SetBool(KeyAutoStart, true);
                }
                else if (!GetBool(KeyAutoStart, false))
                {
                    SetBool(KeyAutoStart, false);
                }

                SetBool(KeyFloatBall, floatBall == 1);
                SetBool(KeyMinToTray, minToTray == 1);
                AppPaths.MigrateOldData();
                SetBool(KeyMigrated, true);
                Logger.Log("Migrate", "设置迁移完成 (autoStart=" + autoStart + ")");
            }
            catch (Exception ex) { Logger.Log("Migrate", "迁移异常: " + ex.Message); }
        }

        // ── 开机自启（HKCU Run）──
        public static bool AutoStartEnabled()
        {
            return Regs.ExistsValue(RegistryHive.CurrentUser, RegistryView.Default, Regs.HK_RUN, RunValue);
        }

        public static string SetAutoStart(bool on)
        {
            if (on)
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string err = Regs.SetStr(RegistryHive.CurrentUser, RegistryView.Default, Regs.HK_RUN,
                    RunValue, "\"" + exe + "\" --minimized");
                if (err == null) SetBool(KeyAutoStart, true);
                return err;
            }
            string e = Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default, Regs.HK_RUN, RunValue);
            if (e == null) SetBool(KeyAutoStart, false);
            return e;
        }
    }
}