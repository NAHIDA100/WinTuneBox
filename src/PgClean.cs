using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 垃圾清理：勾选 → 扫描 → 清理，附无效快捷方式清理 ═══
    public class PgClean : UPage
    {
        ExGrid _grid;
        List<CleanItem> _items = new List<CleanItem>();
        readonly Label _sum = new Label();
        LogBox _log;
        bool _loaded;
        volatile bool _cancel;
        bool _scanning;

        public PgClean()
        {
            Title("垃圾清理",
                "勾选项目 → “扫描大小” → 查看结果后“清理选中项”。清理前会在备份目录自动留档可撤销的注册表项；系统正在使用的文件会安全跳过。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
        }

        void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var bScan = C.Btn("扫描所选", 0); bScan.Click += delegate { Scan(); };
            var bClean = C.Btn("清理选中项", 2); bClean.Click += delegate { CleanSel(); };
            var bChk = C.Btn("全选", 1); bChk.Click += delegate { CheckAll(true); };
            var bUn = C.Btn("全不选", 1); bUn.Click += delegate { CheckAll(false); };
            var bLog = C.Btn("清空日志", 1); bLog.Click += delegate { _log.ClearAll(); };
            var bBad = C.Btn("扫描无效快捷方式…", 1); bBad.Click += delegate { ScanBadShortcuts(); };
            flow.Controls.Add(bScan); flow.Controls.Add(bClean);
            flow.Controls.Add(bChk); flow.Controls.Add(bUn);
            flow.Controls.Add(bBad); flow.Controls.Add(bLog);
            top.Controls.Add(flow);

            _sum.Dock = DockStyle.Top;
            _sum.AutoSize = true;
            _sum.Font = C.F(9f);
            _sum.ForeColor = C.TextSub;
            _sum.Padding = new Padding(0, C.S(4), 0, C.S(8));
            top.Controls.Add(_sum);
            Root.Controls.Add(top);

            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(420) };
            _grid.Columns.Add(_grid.Col("Chk", "清", 4));
            _grid.Columns.Add(_grid.Col("Name", "项目", 16));
            _grid.Columns.Add(_grid.Col("Size", "大小", 10));
            _grid.Columns.Add(_grid.Col("Files", "文件数", 9));
            _grid.Columns.Add(_grid.Col("Note", "说明", 61));
            _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _grid.Columns[0].Width = C.S(44);
            _grid.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var cb = _grid.Rows[e.RowIndex].Cells[0] as DataGridViewCheckBoxCell;
                if (cb != null) cb.Value = !(cb.Value is bool && (bool)cb.Value);
                _grid.EndEdit();
            };
            Root.Controls.Add(_grid);

            _log = new LogBox { Dock = DockStyle.Top, Height = C.S(150) };
            _log.BorderStyle = BorderStyle.FixedSingle;
            Root.Controls.Add(_log);
            EndSpace(10);

            FillList();
        }

        void FillList()
        {
            _grid.Rows.Clear();
            _items = Cleaner.Defaults();
            foreach (var it in _items)
            {
                int i = _grid.Rows.Add(it.Checked, it.Name, it.Scanned ? C.Bytes(it.Size) : "—",
                    it.Files >= 0 ? it.Files.ToString() : "—", it.Note + (it.Admin ? "（需管理员）" : ""));
                _grid.Rows[i].Tag = it;
                if (it.Admin) _grid.Rows[i].Cells[4].Style.ForeColor = C.Orange;
            }
            _sum.Text = "提示：浏览器缓存建议先关闭浏览器；“回收站”为彻底清空，默认不勾选。";
        }

        void CheckAll(bool v)
        {
            foreach (DataGridViewRow r in _grid.Rows) r.Cells[0].Value = v;
        }

        List<CleanItem> Selected()
        {
            var L = new List<CleanItem>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (!(r.Tag is CleanItem)) continue;
                bool chk = r.Cells[0].Value is bool && (bool)r.Cells[0].Value;
                if (chk) L.Add((CleanItem)r.Tag);
            }
            return L;
        }

        void Scan()
        {
            var sel = Selected();
            if (sel.Count == 0) { MessageBox.Show(this, "请先勾选要扫描的项目。", "提示"); return; }
            _scanning = true;
            _cancel = false;
            Busy(delegate
            {
                var cts = new CancellationTokenSource();
                foreach (var it in sel)
                {
                    if (_cancel) break;
                    int idx = RowOf(it);
                    SetCell(idx, 1, "扫描中…", C.Orange);
                    try { Cleaner.Scan(it, cts); }
                    catch { }
                    C.On(this, delegate
                    {
                        if (idx >= 0 && idx < _grid.Rows.Count)
                        {
                            _grid.Rows[idx].Cells[1].Value = it.Name;
                            _grid.Rows[idx].Cells[2].Value = it.Scanned ? C.Bytes(it.Size) : "—";
                            _grid.Rows[idx].Cells[3].Value = it.Files >= 0 ? it.Files.ToString() : "—";
                        }
                    });
                }
                long total = 0; int fc = 0;
                foreach (var it in sel) { total += it.Size; if (it.Files >= 0) fc += it.Files; }
                long t = total;
                C.On(this, delegate
                {
                    _sum.ForeColor = C.Accent;
                    _sum.Text = "扫描完成：共 " + C.Bytes(t) + "，" + fc + " 个文件。清理前可再核对勾选。";
                    _scanning = false;
                });
            });
        }

        int RowOf(CleanItem it)
        {
            for (int i = 0; i < _grid.Rows.Count; i++)
                if (_grid.Rows[i].Tag == it) return i;
            return -1;
        }

        void SetCell(int idx, int col, string txt, Color c)
        {
            C.On(this, delegate
            {
                if (idx >= 0 && idx < _grid.Rows.Count)
                {
                    _grid.Rows[idx].Cells[col].Value = txt;
                    _grid.Rows[idx].Cells[col].Style.ForeColor = c;
                }
            });
        }

        void CleanSel()
        {
            var sel = Selected();
            if (sel.Count == 0) { MessageBox.Show(this, "请先勾选要清理的项目。", "提示"); return; }
            if (_scanning) { MessageBox.Show(this, "扫描中，请稍候…"); return; }
            if (C.Ask("确定清理勾选的 " + sel.Count + " 项吗？\n“回收站”为彻底清空不可恢复。", "确认清理", true) != DialogResult.Yes) return;
            if (!OS.IsElevated && NeedsAdmin(sel))
            {
                if (C.Ask("其中部分项目需要管理员权限，当前未提权可能失败。\n是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            Busy(delegate
            {
                var cts = new CancellationTokenSource();
                int ok = 0, fail = 0;
                foreach (var it in sel)
                {
                    if (_cancel) break;
                    int idx = RowOf(it);
                    SetCell(idx, 1, "清理中…", C.Red);
                    try { Cleaner.Clean(it, _log, cts); ok++; }
                    catch (Exception ex) { fail++; _log.Log(it.Name + " 失败: " + ex.Message, true); }
                    C.On(this, delegate
                    {
                        if (idx >= 0 && idx < _grid.Rows.Count)
                        {
                            _grid.Rows[idx].Cells[1].Value = it.Name;
                            _grid.Rows[idx].Cells[2].Value = "—";
                            _grid.Rows[idx].Cells[3].Value = "—";
                        }
                    });
                    it.Scanned = false;
                }
                string msg = string.Format("清理完成：成功 {0} 项，失败 {1} 项（详情见下方日志）。", ok, fail);
                C.On(this, delegate
                {
                    _sum.ForeColor = fail == 0 ? C.Green : C.Red;
                    _sum.Text = msg;
                    if (fail == 0) MessageBox.Show(this, "清理完成，详见日志。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            });
        }

        static bool NeedsAdmin(List<CleanItem> sel)
        {
            foreach (var it in sel) if (it.Admin) return true;
            return false;
        }

        // ── 无效快捷方式 ──
        void ScanBadShortcuts()
        {
            Busy(delegate
            {
                int total;
                var bad = Cleaner.FindInvalidShortcuts(out total);
                if (bad.Count == 0)
                {
                    C.On(this, delegate { MessageBox.Show(this, "在开始菜单与桌面共扫描 " + total + " 个快捷方式，未发现失效项。", "扫描结果", MessageBoxButtons.OK); });
                    return;
                }
                C.On(this, delegate
                {
                    var dlg = new FrmBadList(bad);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        int ok = 0;
                        foreach (string f in dlg.Selected)
                        {
                            try { File.Delete(f); ok++; }
                            catch (Exception ex) { _log.Log("删除失败: " + Path.GetFileName(f) + " " + ex.Message, true); }
                        }
                        _log.Log("已删除失效快捷方式 " + ok + " 个");
                        _sum.Text = "已删除失效快捷方式 " + ok + " 个";
                    }
                });
            });
        }
    }

    /// <summary>失效快捷方式列表（勾选删除）</summary>
    class FrmBadList : Form
    {
        public List<string> Selected = new List<string>();
        readonly ExGrid _g = new ExGrid { Dock = DockStyle.Fill };
        public FrmBadList(List<string> bad)
        {
            Text = "失效快捷方式（共 " + bad.Count + " 个）";
            Size = new Size(C.S(760), C.S(460));
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C.PageBg;
            AutoScaleMode = AutoScaleMode.None;
            _g.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "删", Name = "Chk", Width = C.S(48), FillWeight = 4, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            _g.Columns.Add(_g.Col("Path", "失效快捷方式完整路径", 96));
            var btnDel = C.Btn("删除勾选项", 2); btnDel.Height = C.S(32); btnDel.Width = C.S(130);
            var btnCancel = C.Btn("取消", 1); btnCancel.Height = C.S(32); btnCancel.Width = C.S(90);
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = C.S(50), FlowDirection = FlowDirection.RightToLeft };
            row.Controls.Add(btnDel); row.Controls.Add(btnCancel);
            foreach (string p in bad)
            {
                int i = _g.Rows.Add(false, p);
                _g.Rows[i].Cells[1].ToolTipText = p;
            }
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            btnDel.Click += delegate
            {
                Selected.Clear();
                foreach (DataGridViewRow r in _g.Rows)
                {
                    if (r.Cells[0].Value is bool && (bool)r.Cells[0].Value)
                        Selected.Add(r.Cells[1].Value as string);
                }
                if (Selected.Count == 0) { MessageBox.Show(this, "请先勾选要删除的快捷方式。", "提示"); return; }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_g);
            Controls.Add(row);
        }
    }
}
