using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 工具：系统修复 / 还原点 / 电源 / 安全模式 / 常用工具 ═══
    public class PgTools : UPage
    {
        readonly Label _powerLbl = new Label();
        LogBox _log;
        Process _proc;
        bool _loaded;

        public PgTools()
        {
            Title("更多工具", "系统修复（SFC/DISM）、还原点、电源计划、安全模式与常用系统工具。输出日志实时显示，可随时“中止”。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            UpdatePower();
        }

        void Build()
        {
            // ── 系统修复 ──
            var c1 = Card();
            CardTitle(c1, "系统修复");
            var f1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            f1.Controls.Add(toolBtn("系统文件检查 (SFC /scannow)", delegate { RunRepair("sfc.exe", "/scannow", "SFC"); }, 0));
            f1.Controls.Add(toolBtn("DISM 修复系统映像", delegate { RunRepair("Dism.exe", "/Online /Cleanup-Image /RestoreHealth", "DISM"); }));
            f1.Controls.Add(toolBtn("DISM 清理组件残留", delegate { RunRepair("Dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", "DISM 组件清理"); }));
            f1.Controls.Add(toolBtn("磁盘检查(计划重启)", delegate { ScheduleDiskCheck(); }));
            f1.Controls.Add(toolBtn("中止当前任务", delegate { KillProc(); }, 2));
            c1.Controls.Add(f1);
            CardNote(c1, "SFC 用于修复被破坏的系统文件；DISM 用于修复系统映像（Win8+），两项都可能耗时数分钟甚至更长，请耐心等待日志结束。磁盘检查将在下次重启时执行。");

            // ── 还原点与恢复 ──
            var c2 = Card();
            CardTitle(c2, "系统保护与恢复");
            var f2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            f2.Controls.Add(toolBtn("创建系统还原点", delegate { DoRestorePoint(); }, 0));
            f2.Controls.Add(toolBtn("打开系统还原", delegate { SysTools.Open("restore"); }));
            f2.Controls.Add(toolBtn("系统保护设置", delegate { SysTools.Open("sysdm"); }));
            c2.Controls.Add(f2);
            CardNote(c2, "创建还原点前请先在“系统保护设置”中确认系统盘保护已开启（C 盘）。还原点在安装驱动/系统更新前创建最合适。");

            // ── 电源计划 ──
            var c3 = Card();
            CardTitle(c3, "电源计划");
            _powerLbl.Font = C.F(9.5f);
            _powerLbl.ForeColor = C.Accent;
            _powerLbl.AutoSize = true;
            _powerLbl.Dock = DockStyle.Top;
            _powerLbl.Padding = new Padding(0, 0, 0, C.S(4));
            c3.Controls.Add(_powerLbl);
            var f3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            f3.Controls.Add(toolBtn("节能", delegate { SwitchPower("节能"); }));
            f3.Controls.Add(toolBtn("平衡(推荐)", delegate { SwitchPower("平衡"); }, 0));
            f3.Controls.Add(toolBtn("高性能", delegate { SwitchPower("高性能"); }));
            f3.Controls.Add(toolBtn("卓越性能", delegate { SwitchPower("卓越性能"); }));
            c3.Controls.Add(f3);
            CardNote(c3, "笔记本用户：切到高性能会提升耗电。卓越性能为隐藏计划，仅 Win10/11 支持，会由系统自动创建。");

            // ── 安全模式 ──
            var c4 = Card();
            CardTitle(c4, "安全模式与重启");
            var f4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            f4.Controls.Add(toolBtn("设置：重启进入安全模式", delegate { SafeBoot(true); }, 2));
            f4.Controls.Add(toolBtn("清除安全模式标记", delegate { SafeBoot(false); }));
            f4.Controls.Add(toolBtn("立即重启", delegate { SysTools.RestartNow(); }, 2));
            c4.Controls.Add(f4);
            CardNote(c4, "“重启进入安全模式”会在 bcdedit 写入标记后重启（保留该标记时每次开机都进安全模式，恢复正常请点“清除安全模式标记”）。");

            // ── 性能小工具 ──
            var c5 = Card();
            CardTitle(c5, "性能与显示");
            var f5 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            f5.Controls.Add(toolBtn("整理内存(所有进程)", delegate { CleanMem(); }, 0));
            f5.Controls.Add(toolBtn("重启资源管理器", delegate { RestartExplorer(); }, 2));
            f5.Controls.Add(toolBtn("刷新桌面图标", delegate { SysTools.RefreshDesktop(); }));
            c5.Controls.Add(f5);

            var desk = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            bool[] vis;
            string[] names = SysTools.DesktopIcons(out vis);
            string[] clsid = {
                "{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "{645FF040-5081-101B-9F08-00AA002F954E}",
                "{208D2C60-3AEA-1069-A2D8-08002B30309D}",
                "{59031a47-3f72-44a7-89c5-5595fe6b30ee}",
            };
            for (int i = 0; i < names.Length; i++)
            {
                string cid = clsid[i];
                bool shown = vis[i];
                var cb = new CheckBox
                {
                    Text = "显示" + names[i],
                    Checked = shown,
                    AutoSize = true,
                    Font = C.F(9f),
                    Margin = new Padding(0, C.S(6), C.S(18), 0),
                };
                cb.CheckedChanged += delegate
                {
                    SysTools.SetDesktopIcon(cid, cb.Checked);
                    SysTools.RefreshDesktop();
                };
                desk.Controls.Add(cb);
            }
            c5.Controls.Add(desk);

            // ── 常用系统工具 ──
            var c6 = Card();
            CardTitle(c6, "常用系统工具");
            var f6 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            string[] tools = { "任务管理器", "设备管理器", "系统配置(msconfig)", "服务", "事件查看器",
                "性能监视器", "磁盘管理", "注册表编辑器", "系统信息", "磁盘清理(系统版)", "命令行", "控制面板卸载" };
            string[] ids = { "taskmgr", "devmgmt", "msconfig", "services", "eventvwr",
                "perfmon", "diskmgmt", "regedit", "msinfo", "cleanmgr", "cmd", "appwiz" };
            for (int i = 0; i < tools.Length; i++)
            {
                var b = C.Btn(tools[i], 1);
                b.AutoSize = false;
                b.Width = C.S(tools[i].Length > 10 ? 146 : 120);
                string id = ids[i];
                b.Click += delegate { SysTools.Open(id); };
                f6.Controls.Add(b);
            }
            c6.Controls.Add(f6);

            // ── 日志 ──
            var c7 = Card();
            CardTitle(c7, "修复与任务日志");
            var lg = new LogBox { Dock = DockStyle.Top, Height = C.S(170) };
            lg.BorderStyle = BorderStyle.FixedSingle;
            c7.Controls.Add(lg);
            _log = lg;
            EndSpace(20);
            FinishPage();
        }

        FlatBtn toolBtn(string text, Action act, int kind = 1)
        {
            var b = C.Btn(text, kind);
            b.AutoSize = false;
            b.Width = C.S(text.Length > 12 ? 210 : 150);
            b.Click += delegate { act(); };
            return b;
        }

        void UpdatePower()
        {
            string g, n;
            OS.ActivePowerScheme(out g, out n);
            _powerLbl.Text = "当前电源计划：" + (string.IsNullOrEmpty(n) ? "未知" : n) +
                (string.IsNullOrEmpty(g) ? "" : "   " + g);
        }

        // ── 修复类（后台，实时日志，可中止）──
        void RunRepair(string file, string args, string label)
        {
            try
            {
                // 已释放的 Process 上求值 HasExited 会抛“没有与此对象关联的进程”，这里统一容错
                if (_proc != null && !_proc.HasExited)
                {
                    MessageBox.Show(this, "已有任务在运行，请先等待或点“中止当前任务”。", "提示");
                    return;
                }
            }
            catch { /* 旧进程对象已失效，允许启动新任务 */ }

            if (!OS.IsElevated)
            {
                if (C.Ask(label + " 需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            var np = Runner.RunAsync(file, args, _log, label, delegate { _proc = null; });
            if (np != null) _proc = np;
        }

        void KillProc()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    _proc.Kill();
                    _log.Log("已发出中止请求，正在终止…", true);
                }
                else
                {
                    _log.Log("当前没有正在运行的任务。");
                }
            }
            catch (Exception ex)
            {
                _log.Log("中止失败（任务可能已结束）: " + ex.Message, true);
            }
        }

        void ScheduleDiskCheck()
        {
            if (C.Ask("将对系统盘执行磁盘检查（chkdsk /f），将在“下次重启时”进行。\n确定计划吗？", "磁盘检查", true) != DialogResult.Yes) return;
            if (!OS.IsElevated)
            {
                if (C.Ask("需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string err = SysTools.ScheduleChkdsk(OS.SystemDrive);
            if (err == null)
            {
                if (C.Ask("已计划。是否现在重启以开始检查？", "磁盘检查", true) == DialogResult.Yes)
                    SysTools.RestartNow();
            }
            else MessageBox.Show(this, "计划失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void DoRestorePoint()
        {
            if (!OS.IsElevated)
            {
                if (C.Ask("创建还原点需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            Busy(delegate
            {
                string err = SysTools.CreateRestorePoint("WinTuneBox 手动还原点");
                C.On(this, delegate
                {
                    if (err == null)
                        MessageBox.Show(this, "系统还原点已成功创建。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(this, err + "\n\n请在“系统保护设置”中为系统盘开启保护后重试。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            });
        }

        void SwitchPower(string name)
        {
            Busy(delegate
            {
                string err = SysTools.SwitchPowerScheme(name);
                C.On(this, delegate
                {
                    bool okAsFallback = err != null && err.StartsWith("OK:");
                    string msg = okAsFallback ? err.Substring(3) : err;
                    if (err == null || okAsFallback)
                        MessageBox.Show(this, (err == null ? "已切换电源计划：" + name : msg), "完成",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(this, err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdatePower();
                });
            });
        }

        void SafeBoot(bool on)
        {
            if (on && C.Ask("确定重启进入安全模式吗？\n（恢复方法：再次打开本工具点“清除安全模式标记”后重启，或运行 msconfig 取消勾选）", "安全模式", true) != DialogResult.Yes) return;
            if (!OS.IsElevated)
            {
                if (C.Ask("需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string err = SysTools.SetSafeBoot(on);
            if (err == null)
            {
                if (on && C.Ask("已设置，是否立即重启进入安全模式？", "安全模式", true) == DialogResult.Yes)
                    SysTools.RestartNow();
                if (!on) MessageBox.Show(this, "已清除安全模式标记，下次重启恢复正常模式。", "完成");
            }
            else MessageBox.Show(this, "设置失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void CleanMem()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string report;
                try { report = SysTools.OptimizeMemoryNow(); }
                catch (Exception ex) { report = "优化失败: " + ex.Message; }
                C.On(this, delegate
                {
                    MessageBox.Show(this, report + "\n\n（内存用量会随程序读写自然回升，属正常现象）", "内存优化完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            });
        }

        void RestartExplorer()
        {
            if (C.Ask("将关闭并重新启动资源管理器（桌面会闪一下，打开中的资源管理器窗口会关闭）。确定？", "重启资源管理器", true) != DialogResult.Yes) return;
            SysTools.RestartExplorer();
        }
    }
}
