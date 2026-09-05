using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinTune
{
    // ═══ 一键优化：操作定义（应用=注册表/服务真实写入，全部可还原）═══
    public class OneOp
    {
        public string Group, Title, Desc, Tag;   // Tag: 恢复后需重启提示 / 风险提示
        public bool Recommended = true;          // 默认勾选
        public Func<bool> Supported;             // 是否支持当前系统
        public Func<int> Detect;                 // 0=默认/未优化 1=已优化 2=部分 3=不支持
        public Func<string> Apply;               // 执行优化
        public Func<string> Restore;             // 还原默认
    }

    public static class OpData
    {
        public static List<OneOp> All;

        static int D(RegistryHive h, RegistryView v, string p, string n)  // 0/1/2/3
        {
            int? val = Regs.Dword(h, v, p, n);
            if (val == null) return 0;
            return val.Value == 1 ? 1 : 0;
        }
        static int Zero(RegistryHive h, RegistryView v, string p, string n) // 目标为 0
        {
            int? val = Regs.Dword(h, v, p, n);
            if (val == null) return 0;
            return val.Value == 0 ? 1 : 0;
        }
        static string Set0(RegistryHive h, RegistryView v, string p, string n) { return Regs.SetDword(h, v, p, n, 0); }
        static string Set1(RegistryHive h, RegistryView v, string p, string n) { return Regs.SetDword(h, v, p, n, 1); }
        static string Del(RegistryHive h, RegistryView v, string p, string n) { return Regs.DelVal(h, v, p, n); }

        public static void Build()
        {
            if (All != null) return;
            All = new List<OneOp>();
            var V = RegistryView.Default;          // HKCU 视图
            var V64 = OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default;
            var LM = RegistryHive.LocalMachine;
            var CU = RegistryHive.CurrentUser;

            // ── 隐私与广告 ──
            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "隐私与广告",
                Title = "关闭遥测与诊断",
                Desc = "禁用 DiagTrack/dmwappush 服务并关闭诊断数据上传（Win10/11）",
                Recommended = true,
                Supported = () => OS.IsWin10Plus,
                Detect = () =>
                {
                    // 服务不存在视为已满足
                    bool dt = SvcDisabled("DiagTrack");
                    bool dm = SvcDisabled("dmwappushservice");
                    int pol = D(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                    if (dt && dm && pol == 1) return 1;
                    if (dt || dm || pol == 1) return 2;
                    return 0;
                },
                Apply = () =>
                {
                    string e1 = SvcSet("DiagTrack", 4);
                    string e2 = SvcSet("dmwappushservice", 4);
                    string e3 = Set0(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                    string[] e = { e1, e2, e3 };
                    return FirstErr(e);
                },
                Restore = () =>
                {
                    string e1 = SvcRestore("DiagTrack", 2);
                    string e2 = SvcRestore("dmwappushservice", 3);
                    string e3 = Del(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                    string[] e = { e1, e2, e3 };
                    return FirstErr(e);
                },
            });

            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "隐私与广告",
                Title = "关闭建议/推广类内容",
                Desc = "关闭开始菜单应用推广、锁屏“技巧与建议”、资源管理器同步通知等（Win10/11）",
                Recommended = true,
                Supported = () => OS.IsWin10Plus,
                Detect = () =>
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    int on = 0;
                    foreach (string vn in vals)
                    {
                        int? x = Regs.Dword(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", vn);
                        if (x == null) on++;             // 缺省视为开（可能展示）
                        else if (x.Value != 0) on++;
                    }
                    if (on == 0) return 1;
                    if (on < vals.Length) return 2;
                    return 0;
                },
                Apply = () =>
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    var errs = new List<string>();
                    foreach (string vn in vals)
                    {
                        string e = Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", vn);
                        if (e != null) errs.Add(e);
                    }
                    string e2 = Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications");
                    if (e2 != null) errs.Add(e2);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
                Restore = () =>
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    var errs = new List<string>();
                    foreach (string vn in vals)
                    {
                        string e = Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", vn);
                        if (e != null) errs.Add(e);
                    }
                    string e2 = Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications");
                    if (e2 != null) errs.Add(e2);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
            });

            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "隐私与广告",
                Title = "关闭搜索建议与网页搜索",
                Desc = "任务栏搜索框不再显示建议与 Bing 网页结果（Win10/11）",
                Recommended = true,
                Supported = () => OS.IsWin10Plus,
                Detect = () => D(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableSearchBoxSuggestions"),
                Apply = () => Set1(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableSearchBoxSuggestions"),
                Restore = () => Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableSearchBoxSuggestions"),
            });

            if (OS.IsWin11) All.Add(new OneOp
            {
                Group = "Win11 界面精简",
                Title = "关闭任务栏小组件",
                Desc = "隐藏任务栏左侧“小组件”(Widgets) 图标",
                Recommended = true,
                Supported = () => OS.IsWin11,
                Detect = () => Zero(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa"),
                Apply = () => Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa"),
                Restore = () => Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa"),
            });
            if (OS.IsWin11) All.Add(new OneOp
            {
                Group = "Win11 界面精简",
                Title = "隐藏任务栏聊天图标",
                Desc = "隐藏任务栏 Teams 聊天图标",
                Recommended = true,
                Supported = () => OS.IsWin11,
                Detect = () => Zero(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ChatIcon"),
                Apply = () => Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ChatIcon"),
                Restore = () => Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ChatIcon"),
            });
            if (OS.IsWin11 && OS.Build >= 22621) All.Add(new OneOp
            {
                Group = "Win11 界面精简",
                Title = "关闭开始菜单“推荐”区",
                Desc = "开始菜单不再显示最近使用的应用与文件推荐（22H2+）",
                Recommended = true,
                Supported = () => OS.IsWin11 && OS.Build >= 22621,
                Detect = () => Zero(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_ShowSuggestions"),
                Apply = () => Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_ShowSuggestions"),
                Restore = () => Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_ShowSuggestions"),
            });

            // ── 性能与流畅 ──
            All.Add(new OneOp
            {
                Group = "性能与流畅",
                Title = "关闭视觉动画特效",
                Desc = "关闭窗口动画/淡入淡出/菜单动画等（等于“最佳性能”，立即生效）",
                Recommended = true,
                Supported = () => true,
                Detect = () =>
                {
                    int? x = Regs.Dword(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
                    if (x != null && x.Value == 2) return 1;
                    return 0;
                },
                Apply = () => VisualFx(false),
                Restore = () => VisualFx(true),
            });

            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "性能与流畅",
                Title = "关闭后台应用",
                Desc = "禁止应用在后台运行（Win10/11 设置中的“后台应用”总开关）",
                Recommended = true,
                Supported = () => OS.IsWin10Plus,
                Detect = () => D(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
                Apply = () => Set1(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
                Restore = () => Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
            });

            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "性能与流畅",
                Title = "关闭游戏录制/Game DVR",
                Desc = "Win+G 游戏栏后台录制、Game DVR 全部关闭（仅影响自带录制功能）",
                Recommended = true,
                Supported = () => OS.IsWin10Plus,
                Detect = () =>
                {
                    int? a = Regs.Dword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                    int? b = Regs.Dword(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
                    int? c = Regs.Dword(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppCapture", "AllowCapture");
                    int hit = 0, any = 0;
                    if (a != null) { any++; if (a.Value == 0) hit++; }
                    if (b != null) { any++; if (b.Value == 0) hit++; }
                    if (c != null) { any++; if (c.Value == 0) hit++; }
                    if (any == 0) return 0;
                    if (hit == any) return 1;
                    return 2;
                },
                Apply = () =>
                {
                    var errs = new List<string>();
                    string e1 = Set0(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                    string e2 = Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
                    string e3 = Set0(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppCapture", "AllowCapture");
                    if (e1 != null) errs.Add(e1); if (e2 != null) errs.Add(e2); if (e3 != null) errs.Add(e3);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
                Restore = () =>
                {
                    var errs = new List<string>();
                    string e1 = Del(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                    string e2 = Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
                    string e3 = Del(CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppCapture", "AllowCapture");
                    if (e1 != null) errs.Add(e1); if (e2 != null) errs.Add(e2); if (e3 != null) errs.Add(e3);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
            });

            All.Add(new OneOp
            {
                Group = "性能与流畅",
                Title = "加速菜单响应",
                Desc = "“开始”菜单/右键菜单弹出延时改为 0 毫秒",
                Recommended = true,
                Supported = () => true,
                Detect = () => Zero(CU, V, @"Control Panel\Desktop", "MenuShowDelay"),
                Apply = () => Set0(CU, V, @"Control Panel\Desktop", "MenuShowDelay"),
                Restore = () => Del(CU, V, @"Control Panel\Desktop", "MenuShowDelay"),
            });

            All.Add(new OneOp
            {
                Group = "性能与流畅",
                Title = "关闭休眠并删除休眠文件",
                Desc = "powercfg /h off；释放系统盘与内存占用的休眠文件。笔记本慎用（默认不勾选）",
                Recommended = false,
                Supported = () => true,
                Tag = "笔记本用户请确认不需要休眠再开启",
                Detect = () =>
                {
                    int? h = Regs.Dword(LM, V64, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                    if (h == null) return 0;
                    return h.Value == 0 ? 1 : 0;
                },
                Apply = () => PowerHibernate(false),
                Restore = () => PowerHibernate(true),
            });

            // ── 高级与安全 ──
            All.Add(new OneOp
            {
                Group = "高级与安全",
                Title = "UAC 提权免弹窗（谨慎）",
                Desc = "管理员账户执行需提权程序时不再弹 UAC 确认框（启用需重启）。安全性降低，默认不勾选",
                Recommended = false,
                Supported = () => true,
                Tag = "启用后需重启才能生效；有安全风险，建议保持默认",
                Detect = () =>
                {
                    int? c = Regs.Dword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin");
                    if (c == null || c.Value == 5) return 0;
                    return 1;
                },
                Apply = () =>
                {
                    string e1 = Set0(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin");
                    string e2 = Set0(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop");
                    return e1 ?? e2;
                },
                Restore = () =>
                {
                    string e1 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 5);
                    string e2 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 1);
                    return e1 ?? e2;
                },
            });
        }

        static string FirstErr(string[] e)
        {
            foreach (string s in e) if (s != null) return s;
            return null;
        }

        // ── 服务工具 ──
        static bool SvcExists(string name)
        {
            return Regs.KeyExists(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES + "\\" + name);
        }
        static bool SvcDisabled(string name)
        {
            if (!SvcExists(name)) return true;
            int? s = Regs.Dword(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES + "\\" + name, "Start");
            return s != null && s.Value == 4;
        }
        static string SvcSet(string name, int target)
        {
            if (!SvcExists(name)) return null;    // 不存在跳过
            return ServicesMgr.SetStartType(name, target, true);
        }
        static string SvcRestore(string name, int fallback)
        {
            if (!SvcExists(name)) return null;
            // 若本工具记录过原值则还原，否则还原为系统默认(fallback)
            int? recorded = RecordedStart(name);
            return ServicesMgr.SetStartType(name, recorded == null ? fallback : recorded.Value, false);
        }
        static int? RecordedStart(string name)
        {
            string file = Path.Combine(App.BackupDir, "service-undo.txt");
            try
            {
                if (!File.Exists(file)) return null;
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] p = line.Split('=');
                    if (p.Length == 2 && p[0] == name) return int.Parse(p[1]);
                }
            }
            catch { }
            return null;
        }

        // ── 视觉特效（SPI 立即生效 + 持久化注册）──
        const uint SPIF_SENDCHANGE = 0x01, SPIF_UPDATEINIFILE = 0x02;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        static string VisualFx(bool restore)
        {
            int off = restore ? 1 : 0;
            uint[] actions = {
                0x1043 /*SPI_SETCLIENTAREAANIMATION*/,
                0x1003 /*SPI_SETMENUANIMATION*/,
                0x1005 /*SPI_SETCOMBOBOXANIMATION*/,
                0x1007 /*SPI_SETLISTBOXSMOOTHSCROLLING*/,
                0x1017 /*SPI_SETTOOLTIPANIMATION*/,
                0x1019 /*SPI_SETTOOLTIPFADE*/,
                0x101D /*SPI_SETSELECTIONFADE*/,
                0x1025 /*SPI_SETDRAGFULLWINDOWS*/,
                0x1011 /*SPI_SETCURSORSHADOW*/,
                0x1009 /*SPI_SETGRADIENTCAPTIONS*/,
            };
            uint flag = SPIF_SENDCHANGE | SPIF_UPDATEINIFILE;
            foreach (uint a in actions)
            {
                int v = off;
                try { SystemParametersInfo(a, 0, ref v, flag); } catch { }
            }
            string err;
            if (restore)
                err = Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
            else
                err = Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
            return err;
        }

        static string PowerHibernate(bool on)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe", on ? "/h on" : "/h off")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                var p = System.Diagnostics.Process.Start(psi);
                if (p != null) p.WaitForExit(15000);
                int code = p == null ? -1 : p.ExitCode;
                if (code == 0)
                {
                    Regs.SetDword(RegistryHive.LocalMachine, OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default,
                        @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", on ? 1 : 0);
                    return null;
                }
                return "powercfg 执行失败(代码 " + code + ")，可能无管理员权限";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
