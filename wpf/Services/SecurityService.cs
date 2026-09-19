using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    /// <summary>安全体检项（只读诊断，不做任何修改）</summary>
    public class SecurityItem
    {
        public string Title { get; set; }
        public string State { get; set; }  // 良好 / 注意 / 风险 / 未知
        public string Detail { get; set; }
        public string Kind { get; set; }  // ok / warn / err / na
        int _weight = 1;
        public int Weight { get { return _weight; } set { _weight = value; } }
    }

    /// <summary>
    /// 安全体检：Defender / 防火墙 / 受控文件夹 / 系统还原 / UAC / 安全启动 / 遥测 / SMBv1。
    /// 全部为只读检测，仅给出建议文本，绝不自动修改这些安全机制。
    /// </summary>
    public static class SecurityService
    {
        public static List<SecurityItem> Check()
        {
            var list = new List<SecurityItem>();
            list.Add(Defender());
            list.Add(Firewall());
            list.Add(ControlledFolder());
            list.Add(RestorePoint());
            list.Add(Uac());
            list.Add(SecureBoot());
            list.Add(Telemetry());
            list.Add(SmbV1());
            return list;
        }

        /// <summary>健康分（0-100）+ 建议文本</summary>
        public static int HealthScore(out string advice, out int issues)
        {
            List<SecurityItem> items = Check();
            int total = 0, good = 0;
            issues = 0;
            foreach (SecurityItem it in items)
            {
                if (it.Kind == "na") continue;
                total += it.Weight;
                if (it.Kind == "ok") good += it.Weight;
                else if (it.Kind == "err" || it.Kind == "warn") issues++;
            }
            advice = items.Count > 0 ? BuildAdvice(items) : "";
            if (total == 0) return 0;
            return (int)Math.Round(good * 100.0 / total);
        }

        static string BuildAdvice(List<SecurityItem> items)
        {
            foreach (SecurityItem it in items)
                if (it.Kind == "err") return it.Title + "：" + it.Detail;
            foreach (SecurityItem it in items)
                if (it.Kind == "warn") return it.Title + "：" + it.Detail;
            return "当前未发现明显的安全配置问题，保持定期更新与还原点即可。";
        }

        // ── 各项检测 ──
        static SecurityItem Defender()
        {
            var it = new SecurityItem { Title = "Defender 实时保护", Weight = 2 };
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object rt = mo["RealTimeProtectionEnabled"];
                        object age = mo["AntivirusSignatureAge"];
                        bool on = rt != null && Convert.ToBoolean(rt);
                        int days = age != null ? Convert.ToInt32(age) : -1;
                        it.Kind = on ? "ok" : "err";
                        it.State = on ? "良好" : "风险";
                        it.Detail = on
                            ? ("实时保护已开启" + (days >= 0 ? "，病毒库 " + days + " 天前更新" : ""))
                            : "实时保护被关闭，建议在 Windows 安全中心重新开启";
                        return it;
                    }
                }
            }
            catch { }

            // 回退：注册表判断（第三方杀软接管时该键可能不存在）
            int? dis = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring");
            if (dis == null)
            {
                it.Kind = "na"; it.State = "未知";
                it.Detail = "未检测到 Defender 状态（可能由第三方安全软件接管）";
            }
            else
            {
                bool on = dis.Value == 0;
                it.Kind = on ? "ok" : "err";
                it.State = on ? "良好" : "风险";
                it.Detail = on ? "实时保护已开启" : "实时保护已关闭";
            }
            return it;
        }

        static SecurityItem Firewall()
        {
            var it = new SecurityItem { Title = "防火墙", Weight = 2 };
            const string baseKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            int on = 0, total = 0;
            string[] profiles = { "DomainProfile", "StandardProfile", "PublicProfile" };
            foreach (string p in profiles)
            {
                int? v = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(), baseKey + "\\" + p, "EnableFirewall");
                if (!v.HasValue) continue;
                total++;
                if (v.Value == 1) on++;
            }
            if (total == 0)
            {
                it.Kind = "na"; it.State = "未知"; it.Detail = "无法读取防火墙配置";
            }
            else if (on == total)
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "全部 " + total + " 个网络配置文件的防火墙均已开启";
            }
            else
            {
                it.Kind = "warn"; it.State = "注意";
                it.Detail = "有 " + (total - on) + " 个网络配置文件的防火墙未开启";
            }
            return it;
        }

        static SecurityItem ControlledFolder()
        {
            var it = new SecurityItem { Title = "勒索防护(受控文件夹)", Weight = 1 };
            int? v = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access",
                "EnableControlledFolderAccess");
            if (v == null)
            {
                it.Kind = "na"; it.State = "未知"; it.Detail = "该项由第三方安全软件或系统版本决定";
            }
            else if (v.Value == 1)
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "受控文件夹访问已开启，可拦截勒索软件加密文件";
            }
            else
            {
                it.Kind = "warn"; it.State = "注意"; it.Detail = "受控文件夹访问未开启，可在 Windows 安全中心开启";
            }
            return it;
        }

        static SecurityItem RestorePoint()
        {
            var it = new SecurityItem { Title = "系统还原", Weight = 1 };
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, CreationTime FROM SystemRestore"))
                {
                    int count = 0;
                    DateTime latest = DateTime.MinValue;
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        count++;
                        try
                        {
                            DateTime t = ManagementDateTimeConverter.ToDateTime(Convert.ToString(mo["CreationTime"]));
                            if (t > latest) latest = t;
                        }
                        catch { }
                    }
                    if (count == 0)
                    {
                        it.Kind = "warn"; it.State = "注意";
                        it.Detail = "没有可用还原点，建议在重大改动前创建一个";
                    }
                    else
                    {
                        it.Kind = "ok"; it.State = "良好";
                        it.Detail = count + " 个还原点，最近 " + latest.ToString("yyyy-MM-dd HH:mm");
                    }
                    return it;
                }
            }
            catch
            {
                it.Kind = "na"; it.State = "未知";
                it.Detail = "系统还原可能已关闭或不可用（需管理员读取）";
                return it;
            }
        }

        static SecurityItem Uac()
        {
            var it = new SecurityItem { Title = "UAC 用户账户控制", Weight = 2 };
            int? lua = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
            int? behavior = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin");
            if (lua == null)
            {
                it.Kind = "na"; it.State = "未知"; it.Detail = "无法读取 UAC 配置";
            }
            else if (lua.Value == 0)
            {
                it.Kind = "err"; it.State = "风险";
                it.Detail = "UAC 已被完全关闭，任何程序都可静默提权，强烈建议重新开启";
            }
            else if (behavior.HasValue && behavior.Value == 0)
            {
                it.Kind = "warn"; it.State = "注意";
                it.Detail = "UAC 已开启但提权不弹窗（免打扰模式），安全性下降";
            }
            else
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "UAC 已开启并会在提权时提示";
            }
            return it;
        }

        static SecurityItem SecureBoot()
        {
            var it = new SecurityItem { Title = "安全启动(Secure Boot)", Weight = 1 };
            int? v = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            if (v == null)
            {
                it.Kind = "na"; it.State = "未知"; it.Detail = "传统 BIOS 启动或系统未提供该状态";
            }
            else if (v.Value == 1)
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "已启用，可防止篡改的引导程序加载";
            }
            else
            {
                it.Kind = "warn"; it.State = "注意"; it.Detail = "未启用，可在 UEFI 固件设置中开启";
            }
            return it;
        }

        static SecurityItem Telemetry()
        {
            var it = new SecurityItem { Title = "诊断数据级别", Weight = 1 };
            int? v = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
            if (v == null)
            {
                it.Kind = "warn"; it.State = "注意";
                it.Detail = "未限制诊断数据（默认可发送完整诊断信息），可用“一键优化”收紧";
            }
            else if (v.Value == 0)
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "诊断数据已限制为最低（安全级）";
            }
            else
            {
                it.Kind = "warn"; it.State = "注意"; it.Detail = "诊断数据级别为 " + v.Value + "，可进一步收紧";
            }
            return it;
        }

        static SecurityItem SmbV1()
        {
            var it = new SecurityItem { Title = "SMBv1 协议", Weight = 1 };
            int? server = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1");
            int? client = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "SMB1");
            bool disabled = server.HasValue && server.Value == 0 && client.HasValue && client.Value == 0;
            if (disabled)
            {
                it.Kind = "ok"; it.State = "良好"; it.Detail = "SMBv1 已关闭";
            }
            else
            {
                it.Kind = "warn"; it.State = "注意";
                it.Detail = "SMBv1 未显式关闭（存在历史高危漏洞），可用“隐私与安全”分组一键加固";
            }
            return it;
        }
    }
}