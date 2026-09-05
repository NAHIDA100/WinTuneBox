using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 服务优化：建议列表 + 手动管理，更改可撤销 ═══
    public class PgServices : UPage
    {
        ExGrid _grid;
        List<SvcRow> _all = new List<SvcRow>();
        List<SvcRow> _shown = new List<SvcRow>();
        readonly Label _sum = new Label();
        readonly TextBox _filter = new TextBox();
        readonly ComboBox _view = new ComboBox();
        bool _loaded, _loading;

        public PgServices()
        {
            Title("服务优化",
                "按“建议”调整非必要系统服务（隐私/游戏/Xbox/家庭组等）。所有改动记录原值，“撤销本工具全部修改”可一键还原；修改重启后完全生效。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            if (_all.Count == 0) Reload();
        }

        void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

            _view.Items.AddRange(new object[] { "全部服务", "仅建议项", "仅运行中" });
            _view.SelectedIndex = 0;
            _view.DropDownStyle = ComboBoxStyle.DropDownList;
            _view.Width = C.S(110); _view.Height = C.S(28); _view.Font = C.F(9f);
            _view.SelectedIndexChanged += delegate { Fill(); };
            flow.Controls.Add(_view);

            _filter.Width = C.S(200); _filter.Height = C.S(28); _filter.Font = C.F(9f);
            _filter.TextChanged += delegate { Fill(); };
            flow.Controls.Add(_filter);

            flow.Controls.Add(flowBtn("按建议调整选中行", delegate { ApplySuggestion(); }));
            flow.Controls.Add(flowBtn("启动服务", delegate { StartStopSel(true); }));
            flow.Controls.Add(flowBtn("停止服务", delegate { StartStopSel(false); }));
            flow.Controls.Add(flowBtn("设为 自动", delegate { SetTypeSel(2); }));
            flow.Controls.Add(flowBtn("设为 手动", delegate { SetTypeSel(3); }));
            flow.Controls.Add(flowBtn("设为 禁用", delegate { SetTypeSel(4); }, 2));
            flow.Controls.Add(flowBtn("撤销本工具全部修改", delegate { UndoAll(); }, 2));
            flow.Controls.Add(flowBtn("刷新", delegate { Reload(); }));
            top.Controls.Add(flow);

            _sum.Dock = DockStyle.Top; _sum.AutoSize = true; _sum.Font = C.F(9f); _sum.ForeColor = C.TextSub;
            _sum.Padding = new Padding(0, C.S(4), 0, C.S(8));
            top.Controls.Add(_sum);
            Root.Controls.Add(top);

            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(430) };
            _grid.Columns.Add(_grid.Col("Name", "服务名", 15));
            _grid.Columns.Add(_grid.Col("Display", "显示名", 22));
            _grid.Columns.Add(_grid.Col("State", "运行", 6));
            _grid.Columns.Add(_grid.Col("Type", "启动类型", 8));
            _grid.Columns.Add(_grid.Col("Sugg", "建议", 8));
            _grid.Columns.Add(_grid.Col("Why", "说明", 28));
            _grid.Columns.Add(_grid.Col("Desc", "映像路径", 13));
            Root.Controls.Add(_grid);
            EndSpace(8);
        }

        FlatBtn flowBtn(string text, Action act, int kind = 1)
        {
            var b = C.Btn(text, kind);
            b.AutoSize = false; b.Width = C.S(148);
            b.Click += delegate { act(); };
            return b;
        }

        void Reload()
        {
            if (_loading) return;
            _loading = true;
            Busy(delegate
            {
                try
                {
                    var rows = ServicesMgr.ReadAll(false);
                    ServicesMgr.FillRunning(rows);
                    _all = rows;
                    C.On(this, delegate { Fill(); });
                }
                finally { _loading = false; }
            });
        }

        void Fill()
        {
            _grid.Rows.Clear();
            string q = _filter.Text.Trim().ToLower();
            string mode = _view.SelectedIndex == 1 ? "sug" : (_view.SelectedIndex == 2 ? "run" : "all");
            int sugCount = 0, sugNeed = 0;
            _shown.Clear();
            foreach (var r in _all)
            {
                bool hasSug = r.Sugg != null;
                if (mode == "sug" && !hasSug) continue;
                if (mode == "run" && !r.Running) continue;
                if (q.Length > 0 && r.Name.ToLower().IndexOf(q) < 0 && r.Display.ToLower().IndexOf(q) < 0) continue;
                if (hasSug) sugCount++;
                if (hasSug && r.Start != r.Sugg.Target) sugNeed++;
                _shown.Add(r);
            }
            _shown.Sort(delegate(SvcRow a, SvcRow b)
            {
                int x = (a.Sugg == null ? 1 : 0) - (b.Sugg == null ? 1 : 0);
                return x != 0 ? x : string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var r in _shown)
            {
                bool hasSug = r.Sugg != null;
                string sugText = hasSug ? (ServicesMgr.TypeName(r.Sugg.Target) + " · " + r.Sugg.Tag) : "";
                int i = _grid.Rows.Add(r.Name, r.Display, r.Running ? "●运行" : "○停止",
                    ServicesMgr.TypeName(r.Start), sugText, hasSug ? r.Sugg.Title + "：" + r.Sugg.Desc : "", r.Desc);
                _grid.Rows[i].Tag = r;
                var row = _grid.Rows[i];
                row.Cells[2].Style.ForeColor = r.Running ? C.Green : C.TextDim;
                if (hasSug)
                {
                    if (r.Start != r.Sugg.Target)
                    {
                        row.Cells[4].Style.ForeColor = C.Orange;
                        row.Cells[4].Style.Font = C.F(9f, true);
                    }
                    else row.Cells[4].Style.ForeColor = C.Green;
                }
                row.Cells[6].ToolTipText = r.Desc;
            }
            bool hasUndo = ServicesMgr.HasUndoRecord;
            _sum.ForeColor = C.TextSub;
            _sum.Text = string.Format("共 {0} 项 · 建议项 {1} 项，其中 {2} 项未按建议" + (hasUndo ? "（存在本工具改动记录，可点右上撤销）" : ""),
                _grid.RowCount, sugCount, sugNeed);
        }

        List<SvcRow> Selected()
        {
            var L = new List<SvcRow>();
            foreach (DataGridViewRow r in _grid.SelectedRows)
                if (r.Tag is SvcRow) L.Add((SvcRow)r.Tag);
            return L;
        }

        void ApplySuggestion()
        {
            var sel = Selected();
            if (sel.Count == 0) { MessageBox.Show(this, "请先选中要调整的服务行。", "提示"); return; }
            DoBatch(sel, "建议", delegate(SvcRow r)
            {
                if (r.Start == r.Sugg.Target) return null;
                return ServicesMgr.SetStartType(r.Name, r.Sugg.Target, true);
            });
        }

        void SetTypeSel(int type)
        {
            var sel = Selected();
            if (sel.Count == 0) { MessageBox.Show(this, "请先选中要调整的服务行。", "提示"); return; }
            string tn = ServicesMgr.TypeName(type);
            if (C.Ask(string.Format("将选中 {0} 个服务的启动类型设为“{1}”？\n注意：禁用后系统重启时不再启动该服务。", sel.Count, tn), "确认", type == 4) != DialogResult.Yes) return;
            DoBatch(sel, tn, delegate(SvcRow r) { return ServicesMgr.SetStartType(r.Name, type, true); });
        }

        void DoBatch(List<SvcRow> rows, string opName, Func<SvcRow, string> fn)
        {
            if (!OS.IsElevated)
            {
                if (C.Ask("修改服务需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            Busy(delegate
            {
                int ok = 0, fail = 0;
                string errs = "";
                foreach (var r in rows)
                {
                    string err;
                    try { err = fn(r); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err == null) ok++;
                    else { fail++; if (errs.Length < 500) errs += "\n" + r.Name + ": " + err; }
                }
                string msg = string.Format("按“{0}”调整完成：成功 {1}，失败 {2}。", opName, ok, fail);
                if (fail > 0) msg += "\n失败原因多为权限不足或被系统保护。" + errs;
                C.On(this, delegate
                {
                    MessageBox.Show(this, msg, "完成", MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    Reload();
                });
            });
        }

        void StartStopSel(bool start)
        {
            var sel = Selected();
            if (sel.Count == 0) { MessageBox.Show(this, "请先选中要调整的服务行。", "提示"); return; }
            if (!OS.IsElevated)
            {
                if (C.Ask("启停服务需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            Busy(delegate
            {
                int ok = 0, fail = 0; string errs = "";
                foreach (var r in sel)
                {
                    string err = ServicesMgr.StartStop(r.Name, start);
                    if (err == null) ok++;
                    else { fail++; if (errs.Length < 500) errs += "\n" + r.Name + ": " + err; }
                }
                C.On(this, delegate
                {
                    MessageBox.Show(this, string.Format("启动/停止完成：成功 {0}，失败 {1}。{2}", ok, fail, errs),
                        "完成", MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    Reload();
                });
            });
        }

        void UndoAll()
        {
            if (!ServicesMgr.HasUndoRecord) { MessageBox.Show(this, "当前没有由本工具产生的服务改动记录。", "提示"); return; }
            if (C.Ask("将把本工具改过的所有服务还原为改动前的启动类型。\n（已运行的服务不会被自动停止）", "确认撤销", true) != DialogResult.Yes) return;
            Busy(delegate
            {
                string msg = ServicesMgr.UndoAll();
                C.On(this, delegate
                {
                    MessageBox.Show(this, msg, "完成");
                    Reload();
                });
            });
        }
    }
}
