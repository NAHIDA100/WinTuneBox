using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>一键优化项：应用=注册表/服务真实写入，全部可还原。状态 0=待优化 1=已优化 2=部分 3=不支持</summary>
    public class OneOp
    {
        public string Group { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public string Tag { get; set; }
        bool _recommended = true;
        public bool Recommended { get { return _recommended; } set { _recommended = value; } }  // 默认勾选
        public bool NeedsAdmin { get; set; }  // 需要管理员
        public Func<bool> Supported { get; set; }
        public Func<int> Detect { get; set; }
        public Func<string> Apply { get; set; }
        public Func<string> Restore { get; set; }
    }

    public static class TweakService
    {
        public static List<OneOp> All;
        static TweakService() { Build(); ExtraTweaks.Append(All); }

        public static void Build()
        {
            if (All != null) return;
            All = new List<OneOp>();
            var V = RegistryView.Default;
            var V64 = OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default;
            var LM = RegistryHive.LocalMachine;
            var CU = RegistryHive.CurrentUser;
            string EXPLORER = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";

            // ── 隐私与广告 ──
            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "隐私与广告", Title = "关闭遥测与诊断", NeedsAdmin = true,
                Desc = "禁用 DiagTrack/dmwappush 服务并关闭诊断数据上传（Win10/11）",
                Supported = delegate { return OS.IsWin10Plus; },
                Detect = delegate
                {
                    bool dt = SvcDisabled("DiagTrack");
                    bool dm = SvcDisabled("dmwappushservice");
                    int pol = D(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                    if (dt && dm && pol == 1) return 1;
                    if (dt || dm || pol == 1) return 2;
                    return 0;
                },
                Apply = delegate
                {
                    return FirstErr(new string[] {
                        SvcSet("DiagTrack", 4), SvcSet("dmwappushservice", 4),
                        Regs.SetDword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0) });
                },
                Restore = delegate
                {
                    return FirstErr(new string[] {
                        SvcRestore("DiagTrack", 2), SvcRestore("dmwappushservice", 3),
                        Regs.DelVal(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") });
                },
            });

            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "隐私与广告", Title = "关闭建议/推广类内容",
                Desc = "关闭开始菜单应用推广、锁屏技巧建议、资源管理器同步通知等（Win10/11）",
                Supported = delegate { return OS.IsWin10Plus; },
                Detect = delegate
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    int on = 0;
                    foreach (string vn in vals)
                    {
                        int? x = Regs.Dword(CU, V, EXPLORER + @"\ContentDeliveryManager", vn);
                        if (x == null) on++;
                        else if (x.Value != 0) on++;
                    }
                    if (on == 0) return 1;
                    if (on < vals.Length) return 2;
                    return 0;
                },
                Apply = delegate
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    var errs = new List<string>();
                    foreach (string vn in vals) { string e = Regs.SetDword(CU, V, EXPLORER + @"\ContentDeliveryManager", vn, 0); if (e != null) errs.Add(e); }
                    string e2 = Regs.SetDword(CU, V, EXPLORER + @"\Advanced", "ShowSyncProviderNotifications", 0);
                    if (e2 != null) errs.Add(e2);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
                Restore = delegate
                {
                    string[] vals = {
                        "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled",
                        "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-353698Enabled",
                    };
                    var errs = new List<string>();
                    foreach (string vn in vals) { string e = Regs.DelVal(CU, V, EXPLORER + @"\ContentDeliveryManager", vn); if (e != null) errs.Add(e); }
                    string e2 = Regs.DelVal(CU, V, EXPLORER + @"\Advanced", "ShowSyncProviderNotifications");
                    if (e2 != null) errs.Add(e2);
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
            });

            if (OS.IsWin10Plus) All.Add(MkReg("隐私与广告", "关闭搜索建议与网页搜索",
                "任务栏搜索框不再显示建议与 Bing 网页结果（Win10/11）",
                CU, V, EXPLORER + @"\Advanced", "DisableSearchBoxSuggestions", 1, true));

            All.Add(MkReg("隐私与广告", "关闭广告 ID",
                "关闭应用广告 ID，减少个性化广告跟踪",
                CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, true));
            All.Add(MkReg("隐私与广告", "关闭个性化诊断广告",
                "关闭基于诊断数据的个性化体验与 Tailored Ads",
                CU, V, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, true));
            All.Add(MkReg("隐私与广告", "关闭客户体验改善计划(CEIP)",
                "停止向微软上报使用情况数据（需管理员）",
                LM, V64, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0, true, true));

            // ── Win11 界面精简 ──
            if (OS.IsWin11) All.Add(MkReg("Win11 界面精简", "关闭任务栏小组件",
                "隐藏任务栏左侧小组件(Widgets)图标",
                CU, V, EXPLORER + @"\Advanced", "TaskbarDa", 0, true));
            if (OS.IsWin11) All.Add(MkReg("Win11 界面精简", "隐藏任务栏聊天图标",
                "隐藏任务栏 Teams 聊天图标",
                CU, V, EXPLORER + @"\Advanced", "ChatIcon", 0, true));
            if (OS.IsWin11 && OS.Build >= 22621) All.Add(MkReg("Win11 界面精简", "关闭开始菜单推荐区",
                "开始菜单不再显示最近使用的应用与文件推荐（22H2+）",
                CU, V, EXPLORER + @"\Advanced", "Start_ShowSuggestions", 0, true));
            if (OS.IsWin11 && OS.Build >= 22631) All.Add(new OneOp
            {
                Group = "Win11 界面精简", Title = "关闭 Copilot 助手",
                Desc = "隐藏任务栏 Copilot 按钮并通过策略关闭 Windows Copilot（23H2+）",
                Supported = delegate { return OS.IsWin11 && OS.Build >= 22631; },
                Detect = delegate
                {
                    int btn = Zero(CU, V, EXPLORER + @"\Advanced", "ShowCopilotButton");
                    int pol = D(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
                    int hit = 0, any = 0;
                    int? a = Regs.Dword(CU, V, EXPLORER + @"\Advanced", "ShowCopilotButton"); if (a != null) { any++; if (a.Value == 0) hit++; }
                    int? b = Regs.Dword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"); if (b != null) { any++; if (b.Value == 1) hit++; }
                    if (any == 0) return 0;
                    return hit == any ? 1 : 2;
                },
                Apply = delegate
                {
                    string e1 = Regs.SetDword(CU, V, EXPLORER + @"\Advanced", "ShowCopilotButton", 0);
                    string e2 = Regs.SetDword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                    return e1 ?? e2;
                },
                Restore = delegate
                {
                    string e1 = Regs.DelVal(CU, V, EXPLORER + @"\Advanced", "ShowCopilotButton");
                    string e2 = Regs.DelVal(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
                    return e1 ?? e2;
                },
            });
            if (OS.IsWin11 && OS.Build >= 26100) All.Add(MkReg("Win11 界面精简", "关闭 Recall 回顾(AI)",
                "关闭 Recall 自动截图回顾功能，保护隐私（24H2+，需管理员）",
                LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, true, true));
            if (OS.IsWin11) All.Add(new OneOp
            {
                Group = "Win11 界面精简", Title = "恢复经典右键菜单",
                Desc = "Win11 右键菜单恢复为 Win10 样式完整菜单（显示全部选项）",
                Tag = "应用后需重启资源管理器生效",
                Supported = delegate { return OS.IsWin11; },
                Detect = delegate
                {
                    string p = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
                    object v = Regs.Val(CU, V, p, "");
                    return v != null && v.ToString() == "" ? 1 : 0;
                },
                Apply = delegate { return Regs.SetStr(CU, V, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""); },
                Restore = delegate { return Regs.DelKey(CU, V, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"); },
            });
            if (OS.IsWin10Plus) All.Add(MkReg("Win11 界面精简", "隐藏任务视图按钮",
                "隐藏任务栏上的任务视图(虚拟桌面)按钮",
                CU, V, EXPLORER + @"\Advanced", "ShowTaskViewButton", 0, true));

            // ── 性能与流畅 ──
            All.Add(new OneOp
            {
                Group = "性能与流畅", Title = "关闭视觉动画特效",
                Desc = "关闭窗口动画/淡入淡出/菜单动画等（等于最佳性能，立即生效）",
                Supported = delegate { return true; },
                Detect = delegate
                {
                    int? x = Regs.Dword(CU, V, EXPLORER + @"\VisualEffects", "VisualFXSetting");
                    return (x != null && x.Value == 2) ? 1 : 0;
                },
                Apply = delegate { return VisualFx(false); },
                Restore = delegate { return VisualFx(true); },
            });
            if (OS.IsWin10Plus) All.Add(MkReg("性能与流畅", "关闭后台应用",
                "禁止 UWP 应用在后台运行（Win10/11 后台应用总开关）",
                CU, V, EXPLORER + @"\BackgroundAccessApplications", "GlobalUserDisabled", 1, true));
            if (OS.IsWin10Plus) All.Add(new OneOp
            {
                Group = "性能与流畅", Title = "关闭游戏录制/Game DVR",
                Desc = "Win+G 游戏栏后台录制、Game DVR 全部关闭（仅影响自带录制功能）",
                Supported = delegate { return OS.IsWin10Plus; },
                Detect = delegate
                {
                    int? a = Regs.Dword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                    int? b = Regs.Dword(CU, V, EXPLORER + @"\GameDVR", "AppCaptureEnabled");
                    int? c = Regs.Dword(CU, V, EXPLORER + @"\AppCapture", "AllowCapture");
                    int hit = 0, any = 0;
                    if (a != null) { any++; if (a.Value == 0) hit++; }
                    if (b != null) { any++; if (b.Value == 0) hit++; }
                    if (c != null) { any++; if (c.Value == 0) hit++; }
                    if (any == 0) return 0;
                    return hit == any ? 1 : 2;
                },
                Apply = delegate
                {
                    var errs = new List<string>();
                    AddErr(errs, Regs.SetDword(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
                    AddErr(errs, Regs.SetDword(CU, V, EXPLORER + @"\GameDVR", "AppCaptureEnabled", 0));
                    AddErr(errs, Regs.SetDword(CU, V, EXPLORER + @"\AppCapture", "AllowCapture", 0));
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
                Restore = delegate
                {
                    var errs = new List<string>();
                    AddErr(errs, Regs.DelVal(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR"));
                    AddErr(errs, Regs.DelVal(CU, V, EXPLORER + @"\GameDVR", "AppCaptureEnabled"));
                    AddErr(errs, Regs.DelVal(CU, V, EXPLORER + @"\AppCapture", "AllowCapture"));
                    return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
                },
            });
            All.Add(new OneOp
            {
                Group = "性能与流畅", Title = "加速菜单响应",
                Desc = "开始菜单/右键菜单弹出延时改为 0 毫秒",
                Supported = delegate { return true; },
                Detect = delegate { return Zero(CU, V, @"Control Panel\Desktop", "MenuShowDelay"); },
                Apply = delegate { return Regs.SetDword(CU, V, @"Control Panel\Desktop", "MenuShowDelay", 0); },
                Restore = delegate { return Regs.DelVal(CU, V, @"Control Panel\Desktop", "MenuShowDelay"); },
            });
            All.Add(new OneOp
            {
                Group = "性能与流畅", Title = "关闭休眠并删除休眠文件", Recommended = false,
                Desc = "powercfg /h off；释放系统盘休眠文件 hiberfil.sys。笔记本慎用",
                Tag = "笔记本用户请确认不需要休眠再开启",
                Supported = delegate { return true; },
                Detect = delegate
                {
                    int? h = Regs.Dword(LM, V64, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                    if (h == null) return 0;
                    return h.Value == 0 ? 1 : 0;
                },
                Apply = delegate { return PowerHibernate(false); },
                Restore = delegate { return PowerHibernate(true); },
            });

            // ── 资源管理器体验 ──
            All.Add(new OneOp
            {
                Group = "资源管理器", Title = "显示已知文件扩展名",
                Desc = "资源管理器中显示 .txt/.exe 等文件后缀（防伪装病毒，推荐）",
                Supported = delegate { return true; },
                Detect = delegate { return Zero(CU, V, EXPLORER + @"\Advanced", "HideFileExt"); },
                Apply = delegate { return Regs.SetDword(CU, V, EXPLORER + @"\Advanced", "HideFileExt", 0); },
                Restore = delegate { return Regs.SetDword(CU, V, EXPLORER + @"\Advanced", "HideFileExt", 1); },
            });
            All.Add(MkReg("资源管理器", "关闭快速访问最近文件记录",
                "快速访问不再记录并显示最近使用的文件/文件夹",
                CU, V, EXPLORER + @"\Advanced", "Start_TrackDocs", 0, true));

            // ── 安全与网络 ──
            All.Add(MkReg("安全与网络", "禁用 U 盘自动播放",
                "禁止所有驱动器 AutoRun/自动播放，防止 U 盘病毒自启（需管理员）",
                LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255, true, true));
            All.Add(MkReg("安全与网络", "关闭传递优化 P2P 分发",
                "更新/应用仅从微软服务器下载，不再向局域网/互联网其他电脑分发上传（需管理员）",
                LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", 0, true, true));

            // ── 高级与安全 ──
            All.Add(new OneOp
            {
                Group = "高级与安全", Title = "UAC 提权免弹窗（谨慎）", Recommended = false,
                Desc = "管理员账户执行需提权程序时不再弹 UAC 确认框（启用需重启）。安全性降低",
                Tag = "启用后需重启；有安全风险，建议保持默认",
                Supported = delegate { return true; },
                Detect = delegate
                {
                    int? c = Regs.Dword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin");
                    if (c == null || c.Value == 5) return 0;
                    return 1;
                },
                Apply = delegate
                {
                    string e1 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0);
                    string e2 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0);
                    return e1 ?? e2;
                },
                Restore = delegate
                {
                    string e1 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 5);
                    string e2 = Regs.SetDword(LM, V64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 1);
                    return e1 ?? e2;
                },
            });
        }

        static void AddErr(List<string> errs, string e) { if (e != null) errs.Add(e); }
        static string FirstErr(string[] e) { foreach (string s in e) if (s != null) return s; return null; }

        static int D(RegistryHive h, RegistryView v, string p, string n)
        {
            int? val = Regs.Dword(h, v, p, n);
            if (val == null) return 0;
            return val.Value == 1 ? 1 : 0;
        }
        static int Zero(RegistryHive h, RegistryView v, string p, string n)
        {
            int? val = Regs.Dword(h, v, p, n);
            if (val == null) return 0;
            return val.Value == 0 ? 1 : 0;
        }

        /// <summary>通用 DWORD 开关：优化=写 onValue，还原=删除值（或写回）</summary>
        static OneOp MkReg(string group, string title, string desc,
            RegistryHive hive, RegistryView view, string path, string name, int onValue, bool restoreDelete, bool admin = false)
        {
            var op = new OneOp
            {
                Group = group, Title = title, Desc = desc, NeedsAdmin = admin,
                Supported = delegate { return true; },
                Detect = delegate
                {
                    int? x = Regs.Dword(hive, view, path, name);
                    if (x == null) return 0;
                    return x.Value == onValue ? 1 : 0;
                },
                Apply = delegate { return Regs.SetDword(hive, view, path, name, onValue); },
                Restore = delegate { return Regs.DelVal(hive, view, path, name); },
            };
            return op;
        }

        // ── 服务 ──
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
            if (!SvcExists(name)) return null;
            return ServiceManager.SetStartType(name, target, true);
        }
        static string SvcRestore(string name, int fallback)
        {
            if (!SvcExists(name)) return null;
            int? recorded = RecordedStart(name);
            return ServiceManager.SetStartType(name, recorded == null ? fallback : recorded.Value, false);
        }
        static int? RecordedStart(string name)
        {
            string file = Path.Combine(AppPaths.BackupDir, "service-undo.txt");
            try
            {
                if (!File.Exists(file)) return null;
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] p = line.Split('=');
                    int v;
                    if (p.Length == 2 && p[0] == name && int.TryParse(p[1], out v)) return v;
                }
            }
            catch { }
            return null;
        }

        // ── 视觉特效 ──
        const uint SPIF_SENDCHANGE = 0x01, SPIF_UPDATEINIFILE = 0x02;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        static string VisualFx(bool restore)
        {
            int off = restore ? 1 : 0;
            uint[] actions = { 0x1043, 0x1003, 0x1005, 0x1007, 0x1017, 0x1019, 0x101D, 0x1025, 0x1011, 0x1009 };
            uint flag = SPIF_SENDCHANGE | SPIF_UPDATEINIFILE;
            foreach (uint a in actions)
            {
                int v = off;
                try { SystemParametersInfo(a, 0, ref v, flag); } catch { }
            }
            if (restore)
                return Regs.DelVal(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
            return Regs.SetDword(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
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
