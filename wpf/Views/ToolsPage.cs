using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace WinTune.Wpf.Views
{
    public partial class ToolsPage : UserControl
    {
        bool _built;
        public ToolsPage()
        {
            InitializeComponent();
            Loaded += delegate { if (!_built) { _built = true; Build(); } };
        }

        void Build()
        {
            Root.Children.Add(Ui.T("更多工具", 24, Ui.TextBrush, true));
            var tSub = Ui.T("系统工具快捷入口、电源计划、内存整理、系统维护操作", 13, Ui.SubBrush);
            tSub.Margin = new Thickness(1, 4, 0, 14);
            Root.Children.Add(tSub);

            // 系统工具
            StackPanel sb;
            var sys = Ui.Card("系统工具", "一键打开常用 Windows 管理工具", out sb);
            var grid = new UniformGrid { Columns = 4 };
            foreach (var it in new[] {
                new { N="系统配置", K="msconfig" }, new { N="任务管理器", K="taskmgr" }, new { N="设备管理器", K="devmgmt" }, new { N="服务", K="services" },
                new { N="事件查看器", K="eventvwr" }, new { N="资源监视器", K="perfmon" }, new { N="磁盘管理", K="diskmgmt" }, new { N="计算机管理", K="compmgmt" },
                new { N="注册表编辑器", K="regedit" }, new { N="命令提示符", K="cmd" }, new { N="系统信息", K="msinfo" }, new { N="磁盘清理", K="cleanmgr" },
                new { N="程序和功能", K="appwiz" }, new { N="系统还原", K="restore" }, new { N="性能选项", K="sysdm" }, new { N="网络连接", K="netcpl" },
                new { N="任务计划", K="taskschd" }, new { N="高级防火墙", K="wf" }, new { N="关于 Windows", K="winver" },
            })
            {
                var key = it.K; var name = it.N;
                var b = Ui.Btn(name, "BtnDefault", delegate { SafeOpen(key, name); });
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                b.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(b);
            }
            sb.Children.Add(grid);
            Root.Children.Add(sys);

            // 电源
            StackPanel pb;
            var power = Ui.Card("电源计划", "切换高性能可释放性能，节能可延长笔记本续航", out pb);
            var pwrap = new WrapPanel();
            foreach (var name in new[] { "平衡", "高性能", "卓越性能", "节能" })
            {
                var n = name;
                pwrap.Children.Add(Ui.Btn(n, "BtnDefault", delegate { SwitchPower(n); }));
            }
            pb.Children.Add(pwrap);
            string pg, pn; OS.ActivePowerScheme(out pg, out pn);
            var pcur = Ui.T("当前：" + (pn ?? "未知"), 12, Ui.SubBrush);
            pcur.Margin = new Thickness(2, 4, 0, 0);
            pb.Children.Add(pcur);
            Root.Children.Add(power);

            // 内存
            StackPanel mb;
            var mem = Ui.Card("内存整理", "清理各进程工作集，被占用内存会换出到页面文件，效果为瞬时", out mb);
            var memRow = new StackPanel { Orientation = Orientation.Horizontal };
            var memResult = Ui.T("", 12.5, Ui.SubBrush);
            memResult.VerticalAlignment = VerticalAlignment.Center;
            memResult.Margin = new Thickness(12, 0, 0, 8);
            memRow.Children.Add(Ui.Btn("立即整理内存", "BtnPrimary", delegate
            {
                memResult.Text = "整理中…";
                Ui.RunAsync(delegate
                {
                    string r;
                    try { r = SystemTools.OptimizeMemoryNow(); }
                    catch (Exception ex) { r = ex.Message; }
                    Dispatcher.Invoke(delegate { memResult.Text = r; Notify.Success("内存整理完成"); });
                });
            }));
            mb.Children.Add(memRow);
            mb.Children.Add(memResult);
            Root.Children.Add(mem);

            // 系统维护
            StackPanel cb;
            var maint = Ui.Card("系统维护", "还原点、磁盘检查、资源管理器、长路径支持", out cb);
            cb.Children.Add(Ui.Row("创建系统还原点", "在重大修改前建立可回退的还原点（需开启系统保护）",
                BtnInRow("创建", delegate
                {
                    if (!RequireAdmin()) return;
                    Notify.Info("正在创建还原点…");
                    Ui.RunAsync(delegate
                    {
                        string r = SystemTools.CreateRestorePoint("WinTuneBox 手动还原点 " + DateTime.Now.ToString("MM-dd HH:mm"));
                        Dispatcher.Invoke(delegate { if (r == null) Notify.Success("还原点创建成功"); else Notify.Warning(r); });
                    });
                }), true));

            string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
            cb.Children.Add(Ui.Row("计划开机磁盘检查", "下次重启时对系统盘执行 chkdsk /f 修复文件系统错误",
                BtnInRow("计划检查 " + sysDrive, delegate
                {
                    if (!RequireAdmin()) return;
                    Ui.RunAsync(delegate
                    {
                        string r = SystemTools.ScheduleChkdsk(sysDrive);
                        Dispatcher.Invoke(delegate { Notify.Info(r == null ? "已计划，重启后执行检查" : ("结果: " + r)); });
                    });
                }), true));

            cb.Children.Add(Ui.Row("重启资源管理器", "任务栏/桌面异常，或切换经典右键菜单后生效",
                BtnInRow("重启 explorer", delegate
                {
                    Ui.RunAsync(delegate { SystemTools.RestartExplorer(); });
                }), false));

            // 长路径
            int? lp = Regs.Dword(RegistryHive.LocalMachine, Regs.V64(),
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled");
            var lpSwitch = Ui.Switch(lp == 1, delegate(bool on)
            {
                if (!OS.IsWin10Plus) { Notify.Warning("仅 Windows 10/11 支持"); return; }
                if (!RequireAdmin()) return;
                string r = SystemTools.EnableLongPaths(on);
                if (r != null) Notify.Warning("设置失败: " + r); else Notify.Success(on ? "已启用长路径支持" : "已关闭长路径支持");
            });
            cb.Children.Add(Ui.Row("启用 Win32 长路径(>260)", "解除传统 260 字符路径长度限制（Win10 1607+）", lpSwitch, true));
            Root.Children.Add(maint);

            // 安全模式
            StackPanel fb;
            var safe = Ui.Card("安全模式", "修改启动配置，重启后进入/退出安全模式", out fb);
            var fwrap = new StackPanel { Orientation = Orientation.Horizontal };
            fwrap.Children.Add(Ui.Btn("重启进入安全模式", "BtnDefault", delegate
            {
                if (!RequireAdmin()) return;
                if (MessageBox.Show("将设置安全模式启动并在 5 秒后重启，是否继续？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                string r = SystemTools.SetSafeBoot(true);
                if (r != null) { Notify.Warning("设置失败: " + r); return; }
                SystemTools.RestartNow();
            }));
            fwrap.Children.Add(Ui.Btn("取消安全模式启动", "BtnDefault", delegate
            {
                if (!RequireAdmin()) return;
                string r = SystemTools.SetSafeBoot(false);
                Notify.Info(r == null ? "已取消安全模式启动，正常重启即可" : r);
            }));
            fb.Children.Add(fwrap);
            Root.Children.Add(safe);

            // 桌面图标
            StackPanel db;
            var desk = Ui.Card("桌面系统图标", "控制此电脑、回收站、网络等图标显示", out db);
            bool[] vis;
            string[] names = SystemTools.DesktopIcons(out vis);
            string[] clsids = SystemTools.DesktopClsids();
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var sw = Ui.Switch(vis[i], delegate(bool on)
                {
                    string r = SystemTools.SetDesktopIcon(clsids[idx], on);
                    if (r != null) Notify.Warning("设置失败: " + r);
                    else { SystemTools.RefreshDesktop(); }
                });
                db.Children.Add(Ui.Row("显示 " + names[i], "", sw));
            }
            Root.Children.Add(desk);
        }

        Button BtnInRow(string text, Action a)
        {
            var b = Ui.Btn(text, "BtnDefault", a);
            b.Margin = new Thickness(0, 0, 0, 0);
            b.Padding = new Thickness(14, 6, 14, 6);
            return b;
        }

        bool RequireAdmin()
        {
            if (OS.IsElevated) return true;
            Notify.Warning("该操作需要管理员权限，请先在主界面左下角点击提权");
            return false;
        }

        void SwitchPower(string name)
        {
            if (!RequireAdmin()) return;
            Notify.Info("正在切换到 " + name + " …");
            Ui.RunAsync(delegate
            {
                string r;
                try { r = SystemTools.SwitchPowerScheme(name); }
                catch (Exception ex) { r = ex.Message; }
                Dispatcher.Invoke(delegate
                {
                    if (r == null) Notify.Success("已切换到 " + name);
                    else if (r.StartsWith("OK:")) Notify.Success(r.Substring(3));
                    else Notify.Warning(r);
                });
            });
        }

        void SafeOpen(string key, string name)
        {
            try { SystemTools.Open(key); }
            catch (Exception ex) { Notify.Warning("无法打开 " + name + ": " + ex.Message); }
        }
    }
}
