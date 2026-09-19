using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>注册表条目（组合项用）：数值型或字符串型</summary>
    public class RegEntry
    {
        public RegistryHive Hive { get; set; }
        public RegistryView View { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public int On { get; set; }  // 数值型：优化后的值
        public int? Off { get; set; }  // 数值型：还原值（null = 删除）
        public string OnText { get; set; }  // 字符串型：优化后的值
        public string OffText { get; set; }  // 字符串型：还原值
        public bool IsText { get; set; }
    }

    /// <summary>
    /// v2.0 新增优化项（性能与游戏 / 存储与维护 / 隐私与安全）。
    /// 每项都是 Detect-Apply-Restore 三件套；风险项 Recommended=false（默认不勾选）。
    /// </summary>
    public static class ExtraTweaks
    {
        static RegistryView V64 { get { return OS.IsX64 ? RegistryView.Registry64 : RegistryView.Default; } }
        const RegistryHive LM = RegistryHive.LocalMachine;
        const RegistryHive CU = RegistryHive.CurrentUser;

        const string MM = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        const string EXPLORER = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        const string POLICIES = @"SOFTWARE\Policies\Microsoft\Windows";
        const string CONSENT = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        const string TCPIF = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        public static void Append(List<OneOp> All)
        {
            // ══════════ 性能与游戏 ══════════
            All.Add(Svc("性能与游戏", "暂停搜索索引后台负载",
                "Windows Search 改为手动启动：文件搜索仍可用，但不再常驻后台索引磁盘",
                "WSearch", 3, 2, true, true, "关闭后首次搜索文件会稍慢"));

            All.Add(Reg("性能与游戏", "前台优先级改为游戏优先",
                "Win32PrioritySeparation=38：前台程序获得更长时间片，游戏与交互更跟手",
                LM, V64, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, 2, true, true, null));

            All.Add(Reg("性能与游戏", "启用硬件加速 GPU 计划",
                "HwSchMode=2：由 GPU 自行管理显存调度，降低游戏延迟（需重启生效）",
                LM, V64, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1, true, true, "需重启；较老的显卡可能不支持"));

            All.Add(Reg("性能与游戏", "关闭 SysMain / Superfetch",
                "SSD 系统上 SysMain 收益有限，关闭可减少后台磁盘与内存占用",
                LM, V64, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4, 2, true, true, "机械硬盘用户不建议关闭"));

            All.Add(RegMulti("性能与游戏", "关闭 NTFS 访问时间戳与 8.3 短名",
                "减少每次文件读写的附加元数据写入，SSD 更省寿命、批量文件操作更快",
                true, true, null,
                E(LM, V64, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1, 2),
                E(LM, V64, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1, 2)));

            All.Add(Cmd("性能与游戏", "关闭计划磁盘整理",
                "停用“计划的碎片整理”任务，避免 SSD 上无意义的定时优化",
                "schtasks /Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Disable",
                "schtasks /Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Enable",
                "schtasks /Query /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /FO CSV",
                new string[] { "Disabled", "已禁用" }, true, true, null));

            All.Add(Power("性能与游戏", "电源：处理器最小状态 5%",
                "避免 CPU 长期锁在最低频率导致卡顿，同时保留降频节能",
                "PROCTHROTTLEMIN", 5, 100, "笔记本使用电池时可能增加耗电"));

            All.Add(Power("性能与游戏", "电源：关闭 USB 选择性暂停",
                "防止 USB 设备（键鼠/声卡/采集卡）被挂起后断连或爆音",
                "USB", 0, 1, "笔记本使用电池时可能增加耗电"));

            All.Add(Power("性能与游戏", "电源：关闭 PCIe 链路状态电源管理",
                "避免 PCIe 设备（显卡/NVMe）进入低功耗状态带来的偶发卡顿",
                "PCIE", 0, 1, "笔记本使用电池时可能增加耗电"));

            All.Add(RegMulti("性能与游戏", "网络：解除多媒体限流",
                "NetworkThrottlingIndex=不限、SystemResponsiveness=10，降低网卡调度延迟",
                true, true, null,
                E(LM, V64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1, 10),
                E(LM, V64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10, 20)));

            All.Add(new OneOp
            {
                Group = "性能与游戏", Title = "网络：禁用 Nagle 合并（低延迟）", NeedsAdmin = true,
                Desc = "对活动网卡设置 TcpAckFrequency=1 / TCPNoDelay=1，减少小包延迟（竞技游戏更明显）",
                Tag = "可能略微降低大文件传输吞吐",
                Supported = delegate { return true; },
                Detect = delegate
                {
                    string[] ifaces = Interfaces();
                    if (ifaces.Length == 0) return 3;
                    int hit = 0;
                    foreach (string id in ifaces)
                        if (Regs.Dword(LM, V64, TCPIF + id, "TcpAckFrequency") == 1) hit++;
                    if (hit == 0) return 0;
                    return hit == ifaces.Length ? 1 : 2;
                },
                Apply = delegate
                {
                    var errs = new List<string>();
                    foreach (string id in Interfaces())
                    {
                        AddErr(errs, Regs.SetDword(LM, V64, TCPIF + id, "TcpAckFrequency", 1));
                        AddErr(errs, Regs.SetDword(LM, V64, TCPIF + id, "TCPNoDelay", 1));
                    }
                    return Join(errs);
                },
                Restore = delegate
                {
                    var errs = new List<string>();
                    foreach (string id in Interfaces())
                    {
                        AddErr(errs, Regs.DelVal(LM, V64, TCPIF + id, "TcpAckFrequency"));
                        AddErr(errs, Regs.DelVal(LM, V64, TCPIF + id, "TCPNoDelay"));
                    }
                    return Join(errs);
                },
            });

            All.Add(Reg("性能与游戏", "关闭启动延迟",
                "StartupDelayInMSec=0：登录后立即启动自启程序，不再等待约 10 秒",
                CU, RegistryView.Default, EXPLORER + @"\Serialize", "StartupDelayInMSec", 0, null, false, true, null));

            All.Add(RegStrMulti("性能与游戏", "关闭任务栏与最小化动画",
                "减少 Explorer 动画开销，老机器与集显上窗口操作更干脆",
                false, true, null,
                T(CU, EXPLORER + @"\Advanced", "TaskbarAnimations", "0", "1"),
                T(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1")));

            All.Add(new OneOp
            {
                Group = "性能与游戏", Title = "关闭内存压缩", Recommended = false, NeedsAdmin = true,
                Desc = "Disable-MMAgent -MemoryCompression：省下压缩/解压的 CPU 开销，代价是内存占用略升",
                Tag = "内存 ≤8GB 的机器建议保持开启",
                Supported = delegate { return OS.IsWin10Plus; },
                Detect = delegate
                {
                    return CmdContains("powershell -NoProfile -Command \"(Get-MMAgent).MemoryCompression\"",
                        new string[] { "True" }) ? 0 : 1;
                },
                Apply = delegate { return Runner.Run("powershell.exe", "-NoProfile -Command \"Disable-MMAgent -MemoryCompression\"", 30000); },
                Restore = delegate { return Runner.Run("powershell.exe", "-NoProfile -Command \"Enable-MMAgent -MemoryCompression\"", 30000); },
            });

            All.Add(RegMulti("性能与游戏", "大内存优化：内核常驻 + 大系统缓存",
                "DisablePagingExecutive/LargeSystemCache=1：内核与文件缓存更多留在物理内存",
                true, false, "内存 16GB 以上再考虑；缓存占用会偏高",
                E(LM, V64, MM, "DisablePagingExecutive", 1, 0),
                E(LM, V64, MM, "LargeSystemCache", 1, 0)));

            // ══════════ 存储与维护 ══════════
            All.Add(Reg("存储与维护", "开启存储感知自动清理",
                "系统自动回收临时文件与回收站内容，减少手动清理频率",
                CU, RegistryView.Default,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, 0, false, true, null));

            All.Add(Reg("存储与维护", "关闭崩溃转储自动写入",
                "CrashDumpEnabled=0：蓝屏时不再写与内存等大的 MEMORY.DMP 文件",
                LM, V64, @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", 0, 3, true, false, "关闭后蓝屏无法进一步分析原因"));

            All.Add(Reg("存储与维护", "更新后不自动重启",
                "NoAutoRebootWithLoggedOnUsers=1：有用户登录时推迟重启，不影响更新的下载与安装",
                LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1, null, true, true, null));

            // ══════════ 隐私与安全 ══════════
            All.Add(RegStrMulti("隐私与安全", "关闭定位服务",
                "系统与应用无法获取位置信息，位置历史同时关闭",
                true, true, null,
                T(LM, CONSENT + "location", "Value", "Deny", "Allow"),
                T(CU, CONSENT + "location", "Value", "Deny", "Allow")));

            All.Add(RegMulti("隐私与安全", "关闭活动历史与时间线",
                "不再记录本机与云端的应用活动记录（Windows 时间线数据源）",
                true, true, null,
                E(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, 1),
                E(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, 1),
                E(LM, V64, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, 1)));

            All.Add(RegMulti("隐私与安全", "关闭输入个性化与云词典",
                "停止上传输入习惯，关闭输入法云端候选与手写个性化",
                false, true, null,
                E(CU, RegistryView.Default, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, 0),
                E(CU, RegistryView.Default, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, 0),
                E(CU, RegistryView.Default, @"SOFTWARE\Microsoft\Input\TIPC", "Enabled", 0, 1),
                E(CU, RegistryView.Default, @"SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, 1)));

            All.Add(RegStrMulti("隐私与安全", "收紧应用权限（摄像头/麦克风/通讯录/日历）",
                "ConsentStore 默认拒绝：应用需显式授权后才能访问敏感设备与数据",
                true, true, "需要使用相机/语音通话时，请在系统“隐私设置”里单独放行",
                T(LM, CONSENT + "webcam", "Value", "Deny", "Allow"),
                T(LM, CONSENT + "microphone", "Value", "Deny", "Allow"),
                T(LM, CONSENT + "contacts", "Value", "Deny", "Allow"),
                T(LM, CONSENT + "appointments", "Value", "Deny", "Allow")));

            All.Add(Reg("隐私与安全", "关闭客户体验反馈请求",
                "不再弹出“Windows 反馈”邀请，也不再定时上报使用情况",
                CU, RegistryView.Default, @"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null, false, true, null));

            All.Add(RegMulti("隐私与安全", "关闭云内容搜索与搜索亮点",
                "任务栏搜索只查本机内容，不再回传关键词、不再显示搜索亮点",
                true, true, null,
                E(LM, V64, POLICIES + @"\Windows Search", "AllowCloudSearch", 0, 1),
                E(LM, V64, POLICIES + @"\Windows Search", "AllowSearchHighlights", 0, 1)));

            All.Add(new OneOp
            {
                Group = "隐私与安全", Title = "关闭 OneDrive 开机自启",
                Desc = "从启动项移除 OneDrive 自启（不卸载、不影响手动打开与同步设置），原值会备份",
                Supported = delegate { return true; },
                Detect = delegate { return Regs.ExistsValue(CU, RegistryView.Default, Regs.HK_RUN, "OneDrive") ? 0 : 1; },
                Apply = delegate
                {
                    object v = Regs.Val(CU, RegistryView.Default, Regs.HK_RUN, "OneDrive");
                    if (v != null) Settings.Set("OneDriveRun", Convert.ToString(v));
                    return Regs.DelVal(CU, RegistryView.Default, Regs.HK_RUN, "OneDrive");
                },
                Restore = delegate
                {
                    string v = Settings.Get("OneDriveRun", null);
                    if (string.IsNullOrEmpty(v)) return null;
                    return Regs.SetStr(CU, RegistryView.Default, Regs.HK_RUN, "OneDrive", v);
                },
            });

            All.Add(RegMulti("隐私与安全", "网络安全加固：关闭 SMBv1",
                "SMBv1 存在已知高危漏洞（永恒之蓝），现代系统已不再需要",
                true, true, null,
                E(LM, V64, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", 0, 1),
                E(LM, V64, @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "SMB1", 0, 1)));

            All.Add(Reg("隐私与安全", "网络安全加固：禁用 LLMNR",
                "阻止局域网通过 LLMNR 广播解析主机名，降低中间人欺骗风险",
                LM, V64, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0, 1, true, true, null));

            All.Add(Reg("隐私与安全", "关闭远程协助邀请",
                "fAllowToGetHelp=0：他人无法通过远程协助连入本机（不影响远程桌面设置）",
                LM, V64, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, 1, true, true, null));

            All.Add(Cmd("隐私与安全", "停用遥测与兼容性计划任务",
                "停用 CEIP / 兼容性评估等后台任务，减少空闲时段的后台占用",
                "schtasks /Change /TN \"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /Disable",
                "schtasks /Change /TN \"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /Enable",
                "schtasks /Query /TN \"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /FO CSV",
                new string[] { "Disabled", "已禁用" }, true, true, null));
        }

        // ══════════ 构造 helper ══════════
        static RegEntry E(RegistryHive hive, RegistryView view, string path, string name, int on, int? off)
        {
            return new RegEntry { Hive = hive, View = view, Path = path, Name = name, On = on, Off = off, IsText = false };
        }

        static RegEntry T(RegistryHive hive, string path, string name, string on, string off)
        {
            return new RegEntry { Hive = hive, View = RegistryView.Default, Path = path, Name = name, OnText = on, OffText = off, IsText = true };
        }

        static OneOp Reg(string group, string title, string desc, RegistryHive hive, RegistryView view,
            string path, string name, int on, int? off, bool admin, bool recommended, string tag)
        {
            return RegMulti(group, title, desc, admin, recommended, tag, E(hive, view, path, name, on, off));
        }

        static OneOp RegMulti(string group, string title, string desc, bool admin, bool recommended, string tag,
            params RegEntry[] entries)
        {
            var op = new OneOp
            {
                Group = group, Title = title, Desc = desc, Tag = tag,
                NeedsAdmin = admin, Recommended = recommended,
            };
            op.Supported = delegate { return true; };
            op.Detect = delegate
            {
                int hit = 0;
                foreach (RegEntry e in entries) if (Match(e)) hit++;
                if (hit == 0) return 0;
                return hit == entries.Length ? 1 : 2;
            };
            op.Apply = delegate
            {
                var errs = new List<string>();
                foreach (RegEntry e in entries)
                {
                    if (e.IsText) AddErr(errs, Regs.SetStr(e.Hive, e.View, e.Path, e.Name, e.OnText));
                    else AddErr(errs, Regs.SetDword(e.Hive, e.View, e.Path, e.Name, e.On));
                }
                return Join(errs);
            };
            op.Restore = delegate
            {
                var errs = new List<string>();
                foreach (RegEntry e in entries)
                {
                    if (e.IsText)
                    {
                        if (e.OffText == null) AddErr(errs, Regs.DelVal(e.Hive, e.View, e.Path, e.Name));
                        else AddErr(errs, Regs.SetStr(e.Hive, e.View, e.Path, e.Name, e.OffText));
                    }
                    else if (e.Off.HasValue) AddErr(errs, Regs.SetDword(e.Hive, e.View, e.Path, e.Name, e.Off.Value));
                    else AddErr(errs, Regs.DelVal(e.Hive, e.View, e.Path, e.Name));
                }
                return Join(errs);
            };
            return op;
        }

        static OneOp RegStrMulti(string group, string title, string desc, bool admin, bool recommended, string tag,
            params RegEntry[] entries)
        {
            return RegMulti(group, title, desc, admin, recommended, tag, entries);
        }

        static bool Match(RegEntry e)
        {
            if (e.IsText)
            {
                string v = Regs.Str(e.Hive, e.View, e.Path, e.Name);
                return v != null && string.Equals(v, e.OnText, StringComparison.OrdinalIgnoreCase);
            }
            int? d = Regs.Dword(e.Hive, e.View, e.Path, e.Name);
            return d.HasValue && d.Value == e.On;
        }

        static OneOp Svc(string group, string title, string desc, string service, int disabledStart, int normalStart,
            bool admin, bool recommended, string tag)
        {
            string path = Regs.HK_SERVICES + "\\" + service;
            var op = new OneOp
            {
                Group = group, Title = title, Desc = desc, Tag = tag,
                NeedsAdmin = admin, Recommended = recommended,
            };
            op.Supported = delegate { return Regs.KeyExists(LM, V64, path); };
            op.Detect = delegate
            {
                int? v = Regs.Dword(LM, V64, path, "Start");
                if (!v.HasValue) return 3;
                return v.Value == disabledStart ? 1 : 0;
            };
            op.Apply = delegate { return Regs.SetDword(LM, V64, path, "Start", disabledStart); };
            op.Restore = delegate { return Regs.SetDword(LM, V64, path, "Start", normalStart); };
            return op;
        }

        static OneOp Cmd(string group, string title, string desc, string applyCmd, string restoreCmd,
            string detectCmd, string[] detectMatches, bool admin, bool recommended, string tag)
        {
            var op = new OneOp
            {
                Group = group, Title = title, Desc = desc, Tag = tag,
                NeedsAdmin = admin, Recommended = recommended,
            };
            op.Supported = delegate { return true; };
            op.Detect = delegate { return CmdContains(detectCmd, detectMatches) ? 1 : 0; };
            op.Apply = delegate { return RunCmd(applyCmd); };
            op.Restore = delegate { return RunCmd(restoreCmd); };
            return op;
        }

        // ══════════ powercfg ══════════
        static readonly Dictionary<string, string[]> PowerMap = new Dictionary<string, string[]>
        {
            { "PROCTHROTTLEMIN", new[] { "54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c" } },
            { "USB", new[] { "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226" } },
            { "PCIE", new[] { "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5" } },
        };

        static OneOp Power(string group, string title, string desc, string key, int onValue, int offValue, string tag)
        {
            var op = new OneOp
            {
                Group = group, Title = title, Desc = desc, Tag = tag, NeedsAdmin = true, Recommended = true,
            };
            op.Supported = delegate { return PowerMap.ContainsKey(key); };
            op.Detect = delegate { return PowerValueIs(key, onValue) ? 1 : 0; };
            op.Apply = delegate { return SetPower(key, onValue); };
            op.Restore = delegate { return SetPower(key, offValue); };
            return op;
        }

        static bool PowerValueIs(string key, int expected)
        {
            string[] map = PowerMap[key];
            string outp = Runner.Run("powercfg.exe", "/q SCHEME_CURRENT " + map[0] + " " + map[1], 8000);
            if (string.IsNullOrEmpty(outp)) return false;
            foreach (string raw in outp.Split('\n'))
            {
                string line = raw.Trim();
                bool ac = line.IndexOf("AC", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("交流", StringComparison.Ordinal) >= 0;
                if (!ac) continue;
                int idx = line.LastIndexOf("0x", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                string hex = line.Substring(idx + 2).Trim();
                int val;
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val)) return val == expected;
            }
            return false;
        }

        static string SetPower(string key, int value)
        {
            string[] map = PowerMap[key];
            string a1 = Runner.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT " + map[0] + " " + map[1] + " " + value, 8000);
            string a2 = Runner.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT " + map[0] + " " + map[1] + " " + value, 8000);
            Runner.Run("powercfg.exe", "/setactive SCHEME_CURRENT", 8000);
            return a1 != null ? a1 : a2;
        }

        // ══════════ 小工具 ══════════
        static string[] Interfaces()
        {
            string[] subs = Regs.SubKeys(LM, V64, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            var list = new List<string>();
            foreach (string s in subs)
            {
                if (Regs.Dword(LM, V64, TCPIF + s, "EnableDHCP").HasValue) list.Add(s);
            }
            return list.ToArray();
        }

        static bool CmdContains(string cmd, string[] matches)
        {
            string file, args;
            if (!SplitCmd(cmd, out file, out args)) return false;
            string outp = Runner.Run(file, args, 10000);
            if (string.IsNullOrEmpty(outp)) return false;
            foreach (string m in matches)
                if (outp.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string RunCmd(string cmd)
        {
            string file, args;
            if (!SplitCmd(cmd, out file, out args)) return "命令解析失败";
            return Runner.Run(file, args, 20000);
        }

        static bool SplitCmd(string cmd, out string file, out string args)
        {
            file = null; args = null;
            int i = cmd.IndexOf(' ');
            if (i <= 0) return false;
            file = cmd.Substring(0, i) + ".exe";
            args = cmd.Substring(i + 1);
            return true;
        }

        static void AddErr(List<string> errs, string err)
        {
            if (err != null && errs.Count < 3) errs.Add(err);
        }

        static string Join(List<string> errs)
        {
            return errs.Count == 0 ? null : string.Join("；", errs.ToArray());
        }
    }
}