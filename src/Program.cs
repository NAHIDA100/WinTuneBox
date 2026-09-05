using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinTune
{
    static class Program
    {
        public const string MutexName = @"Local\WinTuneBox_SingleInstance";
        static Mutex _instanceMutex;
        public static bool StartMinimized;   // --minimized：静默启动到托盘（开机自启用）

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 自检模式：--selftest（写 selftest.txt 后退出，供开发者验证）
            if (args != null && args.Length > 0 && args[0].IndexOf("selftest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelfTest.Run();
                return;
            }
            // UI 离屏截图：--uitest（把 8 个页面渲染为 png 供开发者检查布局）
            if (args != null && args.Length > 0 && args[0].IndexOf("uitest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelfTest.UiDump();
                return;
            }
            // 静默启动到托盘（开机自启场景）
            if (args != null)
                foreach (string a in args)
                    if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
                        StartMinimized = true;

            // 免责声明：绿色版首次启动弹窗；安装版由安装器写入已同意标记
            if (!CheckDisclaimer(StartMinimized))
                return;   // 拒绝同意或静默启动且未同意 → 不启动

            // 内存优化实测：--memprobe（跑一次并写 selftest-mem.txt，验证清理量）
            if (args != null && args.Length > 0 && args[0].IndexOf("memprobe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelfTest.RunMemProbe();
                return;
            }

            bool mutexOk;
            _instanceMutex = new Mutex(true, MutexName, out mutexOk);
            if (!mutexOk)
            {
                MessageBox.Show("Windows 优化工具箱已在运行。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { OS.CollectInfo(); } catch { }
            try { OS.RefreshMem(); } catch { }
            Application.Run(new FrmMain(StartMinimized));
            ReleaseInstance();
        }

        const string DisclaimKey = @"Software\WinTuneBox";
        const string DisclaimVal = "DisclaimerShown";

        static bool DisclaimerAccepted()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DisclaimKey))
                    return k != null && k.GetValue(DisclaimVal) != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// 免责声明检查。allowDialog=false（静默启动）时若尚未同意则直接退出，不打扰开机；
        /// 开发/自动化可用环境变量 WTB_SKIP_DISCLAIMER=1 跳过。
        /// </summary>
        static bool CheckDisclaimer(bool allowDialog)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("WTB_SKIP_DISCLAIMER") == "1") return true;
                if (DisclaimerAccepted()) return true;
                if (!allowDialog) return false;
                string text =
                    "本软件为免费开源的系统优化工具，仅供个人学习与使用。\n\n" +
                    "使用前请确认：系统优化、清理、服务与注册表调整等操作可能影响系统稳定性。\n" +
                    "因误操作、使用方法不当、系统环境差异或其他任何因素造成的系统损坏、设备损坏或数据丢失，" +
                    "均与本软件作者无关，作者不承担任何责任。\n\n" +
                    "本软件已提供操作前备份与撤销机制，请理解每项功能后再执行；重要数据请自行备份。\n\n" +
                    "是否同意以上免责声明并继续使用？";
                var r = MessageBox.Show(text, "免责声明",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return false;
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(DisclaimKey))
                        if (k != null) k.SetValue(DisclaimVal, 1);
                }
                catch { }
                return true;
            }
            catch { return true; }
        }

        /// <summary>释放单实例互斥锁（提权重启前必须先释放，否则新实例会提示“已在运行”）</summary>
        public static void ReleaseInstance()
        {
            var m = _instanceMutex;
            _instanceMutex = null;
            if (m == null) return;
            try { m.ReleaseMutex(); } catch { }
            try { m.Dispose(); } catch { }
        }

        /// <summary>重新获取互斥锁（提权被取消时恢复）；false=期间已有别的实例启动</summary>
        public static bool ReacquireInstance()
        {
            if (_instanceMutex != null) return true;
            bool ok;
            try { _instanceMutex = new Mutex(true, MutexName, out ok); }
            catch { return false; }
            if (ok) return true;
            try { _instanceMutex.Dispose(); } catch { }
            _instanceMutex = null;
            return false;
        }
    }

    /// <summary>命令行自检（无需 GUI）：验证关键读取逻辑在当前系统是否正常</summary>
    public static class SelfTest
    {
        public static void Run()
        {
            string outFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt");
            try { RunInner(outFile); }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(outFile, "致命异常: " + ex, Encoding.UTF8); } catch { }
            }
        }

        static void RunInner(string outFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("════ WinTuneBox 自检报告 ════ " + DateTime.Now);
            Action<string> line = delegate(string s) { sb.AppendLine(s); };
            try
            {
                OS.CollectInfo();
                OS.RefreshMem();
                line("操作系统: " + OS.WinVerFull);
                line("Build=" + OS.Build + "  Win7=" + OS.IsWin7 + "  Win8+=" + OS.IsWin8Plus +
                    "  Win10+=" + OS.IsWin10Plus + "  Win11=" + OS.IsWin11 + "  64位OS=" + OS.IsX64);
                line("提权=" + OS.IsElevated);
                line("CPU: " + OS.CpuName + " / " + OS.CpuCores);
                line("内存: " + C.Bytes((long)OS.TotalRam) + " 空闲 " + C.Bytes((long)OS.FreeRam));
                ulong f, t;
                if (OS.DiskInfo(OS.SystemDrive, out f, out t))
                    line("系统盘: 总 " + C.Bytes((long)t) + " 可用 " + C.Bytes((long)f));
                line("显卡(首选): " + OS.GpuName);
                if (!string.IsNullOrEmpty(OS.GpuAll) && OS.GpuAll != OS.GpuName)
                    line("显卡(全部): " + OS.GpuAll);
                line("开机时间: " + OS.BootTime);

                line("── 一键优化项检测 ──");
                OpData.Build();
                foreach (var op in OpData.All)
                {
                    if (op.Supported == null || !op.Supported()) { line("  [跳过-系统不支持] " + op.Title); continue; }
                    try { line("  [" + new[] { "待优化", "已优化", "部分", "不支持" }[op.Detect()] + "] " + op.Title); }
                    catch (Exception ex) { line("  [异常] " + op.Title + " " + ex.Message); }
                }

                line("── 垃圾清理目标 ──");
                foreach (var it in Cleaner.Defaults())
                {
                    Cleaner.Scan(it, null);
                    line(string.Format("  {0,-20} {1,10}  {2,6} 文件", it.Name,
                        it.Scanned ? C.Bytes(it.Size) : "-", it.Files >= 0 ? it.Files.ToString() : "-"));
                }

                line("── 启动项 ──");
                int en = 0, dis = 0;
                foreach (var it in StartupMgr.GetAll())
                {
                    if (it.Enabled) en++; else dis++;
                    if (en + dis <= 12) line(string.Format("  [{0}] {1} <- {2} ({3})", it.Enabled ? "启" : "禁", it.Name, it.Command, it.Loc));
                }
                line("  (启用 " + en + " / 禁用 " + dis + ")");

                line("── 服务建议 ──");
                var rows = ServicesMgr.ReadAll(false);
                ServicesMgr.FillRunning(rows);
                var sug = ServicesMgr.Suggestions();
                int hit = 0;
                foreach (var s in sug)
                {
                    var r = rows.Find(x => x.Name == s.Name);
                    if (r == null) continue;
                    hit++;
                    line(string.Format("  {0,-22} 当前={1} 建议={2} {3}", r.Name, r.TypeName,
                        ServicesMgr.TypeName(s.Target), r.Start == s.Target ? "✔" : "→需调整"));
                }
                line("  服务建议命中 " + hit + "/" + sug.Count + "（其余本机不存在，自动跳过）");

                line("── 网络 ──");
                foreach (var a in NetMgr.Adapters())
                    line(string.Format("  {0,-18} {1,-9} IP={2,-16} DNS={3}", a.Name, a.Status, a.Ip, a.Dns));
                line("  hosts=" + NetMgr.HostsFile + (System.IO.File.Exists(NetMgr.HostsFile) ? " 可读" : " 缺失"));

                line("── 软件 ──");
                var soft = SoftMgr.ReadAll();
                line("  已装软件 " + soft.Count + " 个；样例: " +
                    (soft.Count > 0 ? soft[0].Name : "-"));

                line("── 界面字体/DPI ──");
                line("  UI字体 " + C.F(9f).Name + "  DPI缩放 " + C.S(100));
            }
            catch (Exception ex)
            {
                line("!! 自检异常: " + ex);
            }
            line("════ 自检结束 ════");
            try { System.IO.File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8); } catch { }
            // 也尝试写入控制台
            try { Console.WriteLine(sb.ToString()); } catch { }
        }

        /// <summary>内存优化实测：跑一次优化并输出报告文件</summary>
        public static void RunMemProbe()
        {
            string outFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-mem.txt");
            try
            {
                long before = SysTools.AvailableMB();
                string report = SysTools.OptimizeMemoryNow();
                long after = SysTools.AvailableMB();
                string txt = string.Format("优化前可用: {0} MB\r\n{1}\r\n优化后可用: {2} MB",
                    before, report, after);
                System.IO.File.WriteAllText(outFile, txt, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(outFile, "异常: " + ex, Encoding.UTF8); } catch { }
            }
        }

        /// <summary>离屏渲染 8 个页面到 png（开发用：检查布局）</summary>
        public static void UiDump()
        {
            string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest");
            try { if (!System.IO.Directory.Exists(outDir)) System.IO.Directory.CreateDirectory(outDir); } catch { }
            var dump = new StringBuilder();
            dump.AppendLine("════ UI 布局数值诊断 ════ " + DateTime.Now);
            // 每个场景用全新的页面实例（避免窗体关闭后对象被释放）
            RunDump(CreatePages(), dump, outDir, 1180, 760, "NORMAL-1180");
            // 真实可达的最小窗口（FrmMain MinimumSize=1000 逻辑宽）压扁→还原
            RunDump(CreatePages(), dump, outDir, 1180, 760, "SQUASH-RECOVER-1180", true, 1000, 650);
            RunDump(CreatePages(), dump, outDir, 640, 520, "NARROW-640");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "layout.txt"), dump.ToString(), Encoding.UTF8); } catch { }
        }

        static UPage[] CreatePages()
        {
            return new UPage[] {
                new PgOverview(), new PgOneKey(), new PgClean(), new PgStartup(),
                new PgServices(), new PgNet(), new PgSoftware(), new PgTools(),
            };
        }

        static void RunDump(UPage[] pages, StringBuilder dump, string outDir, int w, int h, string tag, bool squash = false, int sqW = 200, int sqH = 200)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                try
                {
                    using (var f = new Form())
                    {
                        f.Size = new System.Drawing.Size(C.S(w), C.S(h));
                        f.FormBorderStyle = FormBorderStyle.None;
                        f.ShowInTaskbar = false;
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new System.Drawing.Point(-10000, -10000); // 屏外
                        pages[i].Dock = System.Windows.Forms.DockStyle.Fill;
                        f.Controls.Add(pages[i]);
                        f.Show();
                        Application.DoEvents();
                        pages[i].OnShown();
                        Application.DoEvents();
                        if (squash)
                        {
                            f.Size = new System.Drawing.Size(C.S(sqW), C.S(sqH));
                            Application.DoEvents();
                            f.Size = new System.Drawing.Size(C.S(w), C.S(h));
                            Application.DoEvents();
                            pages[i].Root.PerformLayout();
                            Application.DoEvents();
                        }
                        pages[i].Root.AutoScrollPosition = System.Drawing.Point.Empty;
                        Application.DoEvents();
                        // 数值快照：Root 直接子控件的实际几何
                        dump.AppendLine("── " + tag + " page#" + i + " (" + pages[i].GetType().Name + ")  Root=" +
                            pages[i].Root.Width + "x" + pages[i].Root.Height);
                        foreach (System.Windows.Forms.Control c in pages[i].Root.Controls)
                        {
                            string extra = "";
                            var rc = c as RCard;
                            if (rc != null)
                            {
                                int visibleTop = 0;
                                foreach (System.Windows.Forms.Control ch in rc.Controls)
                                {
                                    if (ch.Visible && ch.Dock != System.Windows.Forms.DockStyle.Bottom)
                                        visibleTop = System.Math.Max(visibleTop, ch.Bottom);
                                }
                                extra = " cardTopY=" + c.Top + " contentBottom=" + visibleTop;
                                if (tag == "NORMAL-1180" && i == 0 && rc.Height > 200)
                                {
                                    foreach (System.Windows.Forms.Control ch in rc.Controls)
                                    {
                                        string txt = ch.Text ?? "";
                                        if (txt.Length > 26) txt = txt.Substring(0, 26);
                                        dump.AppendLine("      ⚙ " + ch.GetType().Name + " H=" + ch.Height + " Vis=" + ch.Visible + " 「" + txt + "」");
                                    }
                                }
                                if (tag.StartsWith("SQUASH") && rc.Height > 700)
                                {
                                    // 异常大卡片：打印子控件明细
                                    foreach (System.Windows.Forms.Control ch in rc.Controls)
                                    {
                                        string pref = "";
                                        if (ch.AutoSize)
                                        {
                                            try
                                            {
                                                pref = " pref=" + ch.GetPreferredSize(new System.Drawing.Size(rc.ClientSize.Width - 48, 0)).Height;
                                            }
                                            catch { }
                                        }
                                        dump.AppendLine(string.Format("    ├ {0} H={1} Dock={2} Auto={3} Y={4}{5}",
                                            ch.GetType().Name, ch.Height, ch.Dock, ch.AutoSize, ch.Top, pref));
                                    }
                                }
                            }
                            dump.AppendLine(string.Format("  [{0}] {1}  Bounds={2},{3} {4}x{5}  Dock={6}{7}",
                                pages[i].Root.Controls.GetChildIndex(c), c.GetType().Name,
                                c.Left, c.Top, c.Width, c.Height, c.Dock, extra));
                        }
                        using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                        {
                            pages[i].DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                            string file = System.IO.Path.Combine(outDir, string.Format("ui{0}-{1}.png", i, tag.ToLower()));
                            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        f.Close();
                    }
                }
                catch (Exception ex)
                {
                    dump.AppendLine("!! " + tag + " page#" + i + " 异常: " + ex);
                }
            }
        }
    }
}
