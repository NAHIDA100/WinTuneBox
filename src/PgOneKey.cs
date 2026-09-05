using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 一键优化：勾选 → 执行 / 还原，全部操作真实可逆 ═══
    public class PgOneKey : UPage
    {
        ExGrid _grid;
        readonly List<OneOp> _rows = new List<OneOp>();
        readonly Label _hint = new Label();
        bool _loaded;

        public PgOneKey()
        {
            Title("一键优化",
                "勾选需要调整的项目后点击“执行优化”。默认勾选官方推荐项；所有操作前自动尝试创建系统还原点，且每项都可单独“还原默认”。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            RefreshStates(false);
        }

        void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            flow.Controls.Add(flowBtn("全部勾选推荐", delegate { CheckAll(true); }));
            flow.Controls.Add(flowBtn("全不选", delegate { CheckAll(false); }));
            flow.Controls.Add(flowBtn("重新检测", delegate { RefreshStates(true); }));
            flow.Controls.Add(flowBtn("✓ 执行优化", delegate { RunAll(false); }, 0));
            flow.Controls.Add(flowBtn("⟲ 还原默认", delegate { RunAll(true); }, 3));
            flow.Controls.Add(flowBtn("⟲ 还原全部(含未勾选)", delegate { RestoreEverything(); }, 2));
            top.Controls.Add(flow);

            _hint.Dock = DockStyle.Top;
            _hint.AutoSize = true;
            _hint.Font = C.F(9f);
            _hint.ForeColor = C.TextSub;
            _hint.Padding = new Padding(0, C.S(4), 0, C.S(8));
            _hint.Text = "说明：状态列的 待优化/已优化/部分 是实时检测结果；“执行优化”对已优化项目会自动跳过。\r\n非管理员运行时，涉及 HKLM/服务 的项目会失败并提示，请点击界面左下角权限条“提权”。";
            top.Controls.Add(_hint);
            Root.Controls.Add(top);

            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(420) };
            _grid.Columns.Add(_grid.Col("Chk", "勾选", 4));
            _grid.Columns.Add(_grid.Col("Group", "分类", 10));
            _grid.Columns.Add(_grid.Col("Title", "项目", 20));
            _grid.Columns.Add(_grid.Col("Desc", "说明", 52));
            _grid.Columns.Add(_grid.Col("State", "状态", 14));
            _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _grid.Columns[0].Width = C.S(52);
            _grid.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var cb = _grid.Rows[e.RowIndex].Cells[0] as DataGridViewCheckBoxCell;
                if (cb != null) cb.Value = !(cb.Value is bool && (bool)cb.Value);
                _grid.EndEdit();
            };
            Root.Controls.Add(_grid);
            EndSpace(10);
            FinishPage();
        }

        FlatBtn flowBtn(string text, Action act, int kind = 1)
        {
            var b = C.Btn(text, kind);
            b.AutoSize = false;
            b.Width = C.S(text.Length > 8 ? 132 : 96);
            b.Click += delegate { act(); };
            return b;
        }

        void CheckAll(bool check)
        {
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.Tag is OneOp) r.Cells[0].Value = check;
            }
        }

        void RefreshStates(bool busy)
        {
            _grid.Rows.Clear();
            _rows.Clear();
            OpData.Build();
            int hidden = 0;
            foreach (var op in OpData.All)
            {
                bool sup = op.Supported == null || op.Supported();
                if (!sup) { hidden++; continue; }
                _rows.Add(op);
                int st = State(op);
                var txt = new[] { "待优化", "已优化", "部分", "不支持" }[st];
                var color = new[] { C.Orange, C.Green, C.Orange, C.TextDim }[st];
                int i = _grid.Rows.Add(true, op.Group, op.Title, op.Desc, txt);
                _grid.Rows[i].Tag = op;
                _grid.Rows[i].Cells[4].Style.ForeColor = color;
                _grid.Rows[i].Cells[1].Style.ForeColor = C.TextSub;
                if (st == 2) _grid.Rows[i].Cells[4].ToolTipText = "部分生效，执行优化可补全";
                _grid.Rows[i].Cells[0].Value = op.Recommended;
            }
            _hint.Text = "提示：共显示 " + _rows.Count + " 项适用本系统" +
                (hidden > 0 ? "，" + hidden + " 项因版本不适用未列出" : "") +
                "。可优化项目会显示橙色“待优化”，已按推荐设置的显示绿色“已优化”。";
        }

        int State(OneOp op)
        {
            try { return op.Detect(); }
            catch { return 3; }
        }

        void RunAll(bool restore) { RunAllImpl(restore, false); }

        void RunAllImpl(bool restore, bool includeUnchecked)
        {
            if (!OS.IsElevated)
            {
                MessageBox.Show(this, "建议以管理员身份运行以获得完整功能。\n请点击界面左下角权限条“以管理员身份重启”。",
                    "权限提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            var targets = new List<OneOp>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (!(r.Tag is OneOp)) continue;
                bool chk = r.Cells[0].Value is bool && (bool)r.Cells[0].Value;
                if (restore)
                {
                    if (chk || includeUnchecked) targets.Add((OneOp)r.Tag);
                }
                else if (chk) targets.Add((OneOp)r.Tag);
            }
            if (targets.Count == 0) { MessageBox.Show(this, "没有选择任何项目。", "提示", MessageBoxButtons.OK); return; }

            if (!restore && C.Ask("执行前将尝试自动创建系统还原点（如系统保护未开启则跳过），继续吗？",
                    "执行优化", false) != DialogResult.Yes) return;

            Busy(delegate
            {
                if (!restore)
                {
                    string rp = SysTools.CreateRestorePoint("WinTuneBox 优化前备份");
                    if (rp == null) SetStatus("系统还原点已创建。");
                    else SetStatus("还原点跳过: " + rp);
                }
                int ok = 0, fail = 0, skip = 0;
                var fails = new List<string>();
                foreach (var op in targets)
                {
                    if (!restore && State(op) == 1) { skip++; continue; }
                    string err = null;
                    try { err = restore ? op.Restore() : op.Apply(); }
                    catch (Exception ex) { err = ex.Message; }
                    if (err == null) ok++;
                    else { fail++; fails.Add(op.Title + "：" + err); }
                }
                string msg = restore
                    ? string.Format("还原完成：成功 {0} 项，失败 {1} 项", ok, fail)
                    : string.Format("执行完成：成功 {0} 项，已是最优跳过 {1} 项，失败 {2} 项", ok, skip, fail);
                if (fails.Count > 0)
                    msg += "\n\n失败明细（多为缺少管理员权限）:\n" + string.Join("\n", fails.ToArray());
                C.On(this, delegate
                {
                    MessageBox.Show(this, msg, "完成", MessageBoxButtons.OK,
                        fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    RefreshStates(false);
                });
            });
        }

        void RestoreEverything()
        {
            if (C.Ask("将把本页所有项目还原为系统默认（含已优化项）。确定？", "还原全部", true) != DialogResult.Yes) return;
            RunAllImpl(true, true);
        }

        void SetStatus(string s)
        {
            C.On(this, delegate { _hint.Text = s + "（正在进行…）"; });
        }
    }
}
