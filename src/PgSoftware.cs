using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 软件管理：列出/卸载（32/64 位全视图）═══
    public class PgSoftware : UPage
    {
        ExGrid _grid;
        List<SoftInfo> _all = new List<SoftInfo>();
        readonly Label _sum = new Label();
        readonly TextBox _search = new TextBox();
        bool _loaded;

        public PgSoftware()
        {
            Title("软件管理",
                "列出系统已安装软件（含 32/64 位与当前用户）。卸载会调用软件自带卸载程序；带 的条目为 Windows 系统补丁已自动隐藏。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            LoadList();
        }

        void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

            var bUn = C.Btn("卸载所选软件", 2); bUn.Click += delegate { UninstallSel(); };
            var bLoc = C.Btn("打开安装目录", 1); bLoc.Click += delegate { OpenLoc(); };
            var bRef = C.Btn("刷新列表", 1); bRef.Click += delegate { LoadList(); };
            var bCpl = C.Btn("系统卸载面板", 1); bCpl.Click += delegate { SysTools.Open("appwiz"); };
            var bExp = C.Btn("导出列表 TXT", 1); bExp.Click += delegate { ExportList(); };
            flow.Controls.Add(bUn); flow.Controls.Add(bLoc); flow.Controls.Add(bRef); flow.Controls.Add(bCpl); flow.Controls.Add(bExp);

            _search.Width = C.S(240); _search.Height = C.S(28); _search.Font = C.F(9f);
            _search.TextChanged += delegate { Fill(); };
            flow.Controls.Add(_search);
            top.Controls.Add(flow);

            _sum.Dock = DockStyle.Top; _sum.AutoSize = true; _sum.Font = C.F(9f); _sum.ForeColor = C.TextSub;
            _sum.Padding = new Padding(0, C.S(4), 0, C.S(8));
            top.Controls.Add(_sum);
            Root.Controls.Add(top);

            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(430) };
            _grid.Columns.Add(_grid.Col("Name", "软件名称", 30));
            _grid.Columns.Add(_grid.Col("Ver", "版本", 12));
            _grid.Columns.Add(_grid.Col("Pub", "发布者", 16));
            _grid.Columns.Add(_grid.Col("Size", "大小", 8));
            _grid.Columns.Add(_grid.Col("Date", "安装日期", 8));
            _grid.Columns.Add(_grid.Col("From", "来源", 8));
            Root.Controls.Add(_grid);
            EndSpace(8);
            FinishPage();
        }

        void LoadList()
        {
            Busy(delegate
            {
                var list = SoftMgr.ReadAll();
                _all = list;
                C.On(this, Fill);
            });
        }

        void Fill()
        {
            _grid.Rows.Clear();
            string q = _search.Text.Trim().ToLower();
            int n = 0;
            foreach (var s in _all)
            {
                if (q.Length > 0 && s.Name.ToLower().IndexOf(q) < 0 && (s.Publisher ?? "").ToLower().IndexOf(q) < 0) continue;
                int i = _grid.Rows.Add(s.Name, s.Version, s.Publisher,
                    s.SizeMB > 0 ? s.SizeMB.ToString("F0") + " MB" : "", s.InstallDate, s.From);
                _grid.Rows[i].Tag = s;
                n++;
            }
            _sum.Text = string.Format("共 {0} 个（含系统补丁过滤）。选择后点“卸载所选软件”。", n);
        }

        SoftInfo Cur()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            return _grid.SelectedRows[0].Tag as SoftInfo;
        }

        void UninstallSel()
        {
            var s = Cur();
            if (s == null) { MessageBox.Show(this, "请先选中要卸载的软件。", "提示"); return; }
            string cmd = SoftMgr.BuildUninstallCmd(s);
            bool msiexec = SoftMgr.IsMsiexec(s.Uninstall ?? "") || SoftMgr.IsMsiexec(s.Quiet ?? "");
            var msg = new StringBuilder();
            msg.AppendLine("确定要卸载以下软件？");
            msg.AppendLine();
            msg.AppendLine("名称: " + s.Name);
            if (!string.IsNullOrEmpty(s.Publisher)) msg.AppendLine("发布者: " + s.Publisher);
            if (!string.IsNullOrEmpty(s.Version)) msg.AppendLine("版本: " + s.Version);
            if (s.SizeMB > 0) msg.AppendLine("大小: " + s.SizeMB.ToString("F0") + " MB");
            msg.AppendLine();
            msg.AppendLine("将调用其官方卸载程序。卸载完成后请手动刷新列表。");
            if (C.Ask(msg.ToString(), "确认卸载", true) != DialogResult.Yes) return;

            try
            {
                string exe, args;
                string c = cmd.Trim();
                if (c.StartsWith("\""))
                {
                    int end = c.IndexOf('"', 1);
                    exe = c.Substring(1, end - 1);
                    args = c.Substring(end + 1).Trim();
                }
                else
                {
                    int sp = c.IndexOf(' ');
                    if (sp > 0) { exe = c.Substring(0, sp); args = c.Substring(sp + 1); }
                    else { exe = c; args = ""; }
                }
                if (exe.Length == 0 || (!msiexec && !File.Exists(exe)))
                    throw new Exception("未找到卸载程序: " + exe + "\n可到系统卸载面板手动卸载。");
                if (msiexec) args = "/x " + (c.IndexOf('{') >= 0 ? c.Substring(c.IndexOf('{')) : "");
                var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true };
                Process.Start(psi);
                MessageBox.Show(this, "已启动卸载程序，请按提示完成操作。\n本工具会在后台监测，卸载完成自动刷新列表。", "卸载中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                WatchUninstall(s.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动卸载失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>后台轮询注册表，条目消失即视为卸载完成，自动刷新列表（最长监测约 90 秒）</summary>
        void WatchUninstall(string softName)
        {
            string target = softName ?? "";
            if (target.Length == 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool done = false;
                for (int i = 0; i < 36; i++)
                {
                    System.Threading.Thread.Sleep(2500);
                    if (IsDisposed) return;
                    try
                    {
                        bool still = SoftMgr.ReadAll().Exists(x =>
                            string.Equals(x.Name, target, StringComparison.OrdinalIgnoreCase));
                        if (!still) { done = true; break; }
                    }
                    catch { }
                }
                C.On(this, delegate
                {
                    if (IsDisposed) return;
                    LoadList();
                    if (done)
                        MessageBox.Show(this, "已检测到“" + target + "”卸载完成，列表已自动刷新。", "完成",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(this, "卸载可能尚未完成（或卸载器仍在运行）。列表已刷新，稍后可再点“刷新列表”。", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            });
        }

        void OpenLoc()
        {
            var s = Cur();
            if (s == null) { MessageBox.Show(this, "请先选中一个软件。", "提示"); return; }
            SoftMgr.OpenInstallFolder(s);
        }

        void ExportList()
        {
            var dlg = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "已装软件-" + DateTime.Now.ToString("yyyyMMdd") + ".txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var sb = new StringBuilder();
            sb.AppendLine("已安装软件列表（WinTuneBox）");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("══════════════════════════");
            foreach (var s in _all)
            {
                sb.AppendLine(string.Format("{0}  |  {1}  |  {2}  |  {3}  |  {4}",
                    s.Name, s.Version, s.Publisher, s.SizeMB > 0 ? s.SizeMB.ToString("F0") + " MB" : "", s.From));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(this, "已导出 " + _all.Count + " 条记录。", "完成");
        }
    }
}
