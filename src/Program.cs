using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinTune
{
    static class Program
    {
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

            bool mutexOk;
            using (var m = new Mutex(true, @"Local\WinTuneBox_SingleInstance", out mutexOk))
            {
                if (!mutexOk)
                {
                    MessageBox.Show("Windows 优化工具箱已在运行。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { OS.CollectInfo(); } catch { }
                try { OS.RefreshMem(); } catch { }
                Application.Run(new FrmMain());
                GC.KeepAlive(m);
            }
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
                line("显卡: " + OS.GpuName);
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

        /// <summary>离屏渲染 8 个页面到 png（开发用：检查布局）</summary>
        public static void UiDump()
        {
            string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest");
            try { if (!System.IO.Directory.Exists(outDir)) System.IO.Directory.CreateDirectory(outDir); } catch { }
            var pages = new UPage[] {
                new PgOverview(), new PgOneKey(), new PgClean(), new PgStartup(),
                new PgServices(), new PgNet(), new PgSoftware(), new PgTools(),
            };
            for (int i = 0; i < pages.Length; i++)
            {
                try
                {
                    using (var f = new Form())
                    {
                        f.Size = new System.Drawing.Size(C.S(1180), C.S(760));
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
                        pages[i].Root.AutoScrollPosition = System.Drawing.Point.Empty;
                        Application.DoEvents();
                        using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                        {
                            pages[i].DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                            string file = System.IO.Path.Combine(outDir, string.Format("ui{0}.png", i));
                            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        f.Close();
                    }
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "err" + i + ".txt"), ex.ToString()); } catch { }
                }
            }
        }
    }
}
