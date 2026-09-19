using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace WinTune.Wpf
{
    public class SvcRow
    {
        public string Name { get; set; }
        public string Display { get; set; }
        public string Desc { get; set; }
        public int Start { get; set; }  // 2=自动 3=手动 4=禁用
        public string TypeName { get; set; }
        public bool Running { get; set; }
        public SvcSug Sugg { get; set; }
    }

    public class SvcSug
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public int Target { get; set; }
        public string Tag { get; set; }
        public bool Only7 { get; set; }
    }

    /// <summary>服务优化：注册表启动类型 + 运行状态，改动记录原值，可整体撤销</summary>
    public static class ServiceManager
    {
        public static List<SvcSug> Suggestions()
        {
            var L = new List<SvcSug>();
            L.Add(new SvcSug { Name = "DiagTrack", Target = 4, Tag = "隐私", Title = "连接的用户体验与遥测", Desc = "微软遥测/诊断数据上传服务（Win10/11）" });
            L.Add(new SvcSug { Name = "dmwappushservice", Target = 4, Tag = "隐私", Title = "设备管理 WAP 推送", Desc = "推送遥测相关（Win10/11）" });
            L.Add(new SvcSug { Name = "RetailDemo", Target = 4, Tag = "隐私", Title = "零售演示", Desc = "商店演示模式服务" });
            L.Add(new SvcSug { Name = "RemoteRegistry", Target = 4, Tag = "安全", Title = "远程注册表", Desc = "允许远程修改注册表，一般无需开启" });
            L.Add(new SvcSug { Name = "WMPNetworkSvc", Target = 4, Tag = "共享", Title = "Windows Media Player 网络共享", Desc = "向其他设备共享媒体库" });
            L.Add(new SvcSug { Name = "Fax", Target = 4, Tag = "办公", Title = "传真", Desc = "传真机功能，几乎用不到" });
            L.Add(new SvcSug { Name = "HomeGroupProvider", Target = 4, Tag = "共享", Title = "家庭组提供程序", Desc = "仅 Win7/Win8 家庭组", Only7 = true });
            L.Add(new SvcSug { Name = "HomeGroupListener", Target = 4, Tag = "共享", Title = "家庭组侦听器", Desc = "仅 Win7/Win8 家庭组", Only7 = true });
            L.Add(new SvcSug { Name = "Computer Browser", Target = 4, Tag = "网络", Title = "计算机浏览器", Desc = "仅 Win7，维护网上邻居列表", Only7 = true });
            L.Add(new SvcSug { Name = "XblAuthManager", Target = 3, Tag = "游戏", Title = "Xbox Live 认证管理器", Desc = "Xbox 应用/商店登录，游戏可正常玩" });
            L.Add(new SvcSug { Name = "XblGameSave", Target = 3, Tag = "游戏", Title = "Xbox Live 游戏存档", Desc = "Xbox 云存档" });
            L.Add(new SvcSug { Name = "XboxNetApiSvc", Target = 3, Tag = "游戏", Title = "Xbox Live 网络服务", Desc = "Xbox 联网功能" });
            L.Add(new SvcSug { Name = "MapsBroker", Target = 3, Tag = "应用", Title = "下载的映射管理器", Desc = "Windows 地图离线更新（Win10+）" });
            L.Add(new SvcSug { Name = "PcaSvc", Target = 3, Tag = "性能", Title = "程序兼容性助手", Desc = "监控兼容性问题，后台开销小" });
            L.Add(new SvcSug { Name = "SysMain", Target = 3, Tag = "性能", Title = "SysMain(超级预取)", Desc = "SSD 上收益很小，机械硬盘建议保留自动" });
            L.Add(new SvcSug { Name = "WSearch", Target = 3, Tag = "性能", Title = "Windows 搜索", Desc = "文件/开始菜单索引搜索；不常用搜索可设手动" });
            L.Add(new SvcSug { Name = "Spooler", Target = 3, Tag = "打印", Title = "打印后台处理程序", Desc = "未连接/不使用打印机时可设手动" });
            L.Add(new SvcSug { Name = "iphlpsvc", Target = 3, Tag = "网络", Title = "IP Helper", Desc = "IPv6 隧道/Teredo 等，绝大多数网络不需要" });
            return L;
        }

        public static List<SvcRow> ReadAll(bool includeDrivers)
        {
            var list = new List<SvcRow>();
            string[] names = Regs.SubKeys(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES);
            var sug = Suggestions();
            var sugByName = new Dictionary<string, SvcSug>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sug) if (!sugByName.ContainsKey(s.Name)) sugByName[s.Name] = s;

            foreach (string n in names)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(Regs.HK_SERVICES + "\\" + n))
                    {
                        if (k == null) continue;
                        object typeObj = k.GetValue("Type");
                        object startObj = k.GetValue("Start");
                        int type = typeObj == null ? 0 : Convert.ToInt32(typeObj);
                        int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                        if (!includeDrivers && type != 0 && type < 0x10) continue;
                        string disp = k.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(disp)) continue;
                        if (start == 0 || start == 1) continue;
                        string image = k.GetValue("ImagePath") as string;
                        list.Add(new SvcRow
                        {
                            Name = n,
                            Display = disp,
                            Desc = (image ?? "").Length > 120 ? image.Substring(0, 120) : (image ?? ""),
                            Start = start,
                            TypeName = TypeName(start),
                            Sugg = sugByName.ContainsKey(n) ? sugByName[n] : null,
                        });
                    }
                }
                catch { }
            }
            return list;
        }

        public static string TypeName(int start)
        {
            switch (start)
            {
                case 2: return "自动";
                case 3: return "手动";
                case 4: return "禁用";
                default: return "未知(" + start + ")";
            }
        }

        public static void FillRunning(List<SvcRow> rows)
        {
            try
            {
                var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (ServiceController sc in ServiceController.GetServices())
                {
                    try { map[sc.ServiceName] = sc.Status == ServiceControllerStatus.Running; }
                    catch { }
                    sc.Dispose();
                }
                foreach (var r in rows) { bool v; r.Running = map.TryGetValue(r.Name, out v) && v; }
            }
            catch { }
        }

        static string UndoFile { get { return Path.Combine(AppPaths.BackupDir, "service-undo.txt"); } }

        public static string SetStartType(string name, int target, bool record)
        {
            if (!OS.IsElevated) return "需要管理员权限";
            try
            {
                string path = Regs.HK_SERVICES + "\\" + name;
                int old = -1;
                if (record)
                {
                    var o = Regs.Dword(RegistryHive.LocalMachine, RegistryView.Default, path, "Start");
                    if (o != null) old = o.Value;
                }
                string err = Regs.SetDword(RegistryHive.LocalMachine, RegistryView.Default, path, "Start", target);
                if (err != null) return err;
                if (record && old >= 0 && old != target) RecordUndo(name, old);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static void RecordUndo(string name, int oldStart)
        {
            try
            {
                string file = UndoFile;
                var lines = new List<string>();
                if (File.Exists(file)) lines.AddRange(File.ReadAllLines(file));
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string[] p = lines[i].Split('=');
                    if (p.Length == 2 && p[0] == name) { lines[i] = name + "=" + oldStart; found = true; break; }
                }
                if (!found) lines.Add(name + "=" + oldStart);
                File.WriteAllLines(file, lines);
            }
            catch { }
        }

        public static string UndoAll()
        {
            string file = UndoFile;
            if (!File.Exists(file)) return "没有需要撤销的服务更改记录";
            if (!OS.IsElevated) return "需要管理员权限";
            int ok = 0, fail = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] p = line.Split('=');
                if (p.Length != 2) continue;
                int val;
                if (!int.TryParse(p[1], out val)) continue;
                string err = SetStartType(p[0], val, false);
                if (err == null) ok++; else fail++;
            }
            try { File.Delete(file); } catch { }
            return string.Format("已还原 {0} 项，失败 {1} 项", ok, fail);
        }

        public static bool HasUndoRecord { get { return File.Exists(UndoFile); } }

        public static int UndoCount()
        {
            try { return File.Exists(UndoFile) ? File.ReadAllLines(UndoFile).Length : 0; }
            catch { return 0; }
        }

        /// <summary>UI 用：命中的优化建议（服务存在且当前启动类型不同于建议值）</summary>
        public class SvcView
        {
            public string Name { get; set; }
            public string Display { get; set; }
            public string Why { get; set; }
            public string CurStart { get; set; }
            public string RecStart { get; set; }
            public int Target { get; set; }
        }

        public static List<SvcView> Evaluate()
        {
            var res = new List<SvcView>();
            foreach (var s in Suggestions())
            {
                if (s.Only7 && !OS.IsWin7) continue;
                int? cur = Regs.Dword(RegistryHive.LocalMachine, RegistryView.Default, Regs.HK_SERVICES + "\\" + s.Name, "Start");
                if (!cur.HasValue) continue;
                if (cur.Value < 2 || cur.Value > 4) continue;
                if (cur.Value == s.Target) continue;
                res.Add(new SvcView
                {
                    Name = s.Name,
                    Display = s.Title,
                    Why = s.Desc,
                    CurStart = TypeName(cur.Value),
                    RecStart = TypeName(s.Target),
                    Target = s.Target,
                });
            }
            return res;
        }

        public static string StartStop(string name, bool start)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (start)
                    {
                        if (sc.Status == ServiceControllerStatus.Running) return null;
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                    else
                    {
                        if (sc.Status != ServiceControllerStatus.Running) return null;
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("108") >= 0 || ex.Message.IndexOf("不能启动") >= 0 ||
                    ex.Message.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("已禁用", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "服务已禁用，请先设为自动/手动";
                return ex.Message;
            }
        }
    }
}
