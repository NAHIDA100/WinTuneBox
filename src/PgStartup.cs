using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 启动项管理 ═══
    public class PgStartup : UPage
    {
        ExGrid _grid;
        List<StartupEntry> _all = new List<StartupEntry>();
        readonly Label _sum = new Label();
        readonly TextBox _filter = new TextBox();
        bool _loaded;

        public PgStartup()
        {
            Title("启动项管理",
                "列出系统自启动项目（注册表 + 启动文件夹，含 32/64 位视图）。双击行可快速启用/禁用。Win8+ 与任务管理器“启动”页共用同一套禁用标记。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            LoadItems();
        }

        void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

            var bOn = C.Btn("启用", 3); bOn.Click += delegate { Toggle(true); };
            var bOff = C.Btn("禁用", 2); bOff.Click += delegate { Toggle(false); };
            var bDel = C.Btn("删除(带备份)", 2); bDel.Click += delegate { DeleteSel(); };
            var bRef = C.Btn("刷新", 1); bRef.Click += delegate { LoadItems(); };
            var bLoc = C.Btn("打开所在位置", 1); bLoc.Click += delegate { OpenLocation(); };
            var bAdd = C.Btn("+ 添加启动项", 0); bAdd.Click += delegate { AddDialog(); };
            var bF1 = C.Btn("打开用户启动文件夹", 1); bF1.Click += delegate { StartupMgr.OpenFolder(false); };
            var bF2 = C.Btn("打开公共启动文件夹", 1); bF2.Click += delegate { StartupMgr.OpenFolder(true); };
            flow.Controls.Add(bAdd); flow.Controls.Add(bOn); flow.Controls.Add(bOff); flow.Controls.Add(bDel);
            flow.Controls.Add(bRef); flow.Controls.Add(bLoc); flow.Controls.Add(bF1); flow.Controls.Add(bF2);

            _filter.Font = C.F(9f);
            _filter.Width = C.S(230);
            _filter.Height = C.S(28);
            _filter.Margin = new Padding(C.S(14), 0, 0, 0);
            _filter.TextChanged += delegate { Fill(); };
            flow.Controls.Add(_filter);
            top.Controls.Add(flow);

            _sum.Dock = DockStyle.Top; _sum.AutoSize = true; _sum.Font = C.F(9f); _sum.ForeColor = C.TextSub;
            _sum.Padding = new Padding(0, C.S(4), 0, C.S(8));
            top.Controls.Add(_sum);
            Root.Controls.Add(top);

            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(430) };
            _grid.Columns.Add(_grid.Col("Name", "名称", 20));
            _grid.Columns.Add(_grid.Col("Cmd", "命令 / 目标", 56));
            _grid.Columns.Add(_grid.Col("Loc", "位置", 16));
            _grid.Columns.Add(_grid.Col("State", "状态", 8));
            _grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) ToggleSingle(RowEntry(e.RowIndex), !RowEntry(e.RowIndex).Enabled);
            };
            Root.Controls.Add(_grid);
            EndSpace(8);
        }

        void LoadItems()
        {
            Busy(delegate
            {
                var items = StartupMgr.GetAll();
                _all = items;
                Fill();
            });
        }

        void Fill()
        {
            _grid.Rows.Clear();
            string q = _filter.Text.Trim().ToLower();
            int disabled = 0;
            foreach (var it in _all)
            {
                if (q.Length > 0 && it.Name.ToLower().IndexOf(q) < 0 &&
                    it.Command.ToLower().IndexOf(q) < 0 && it.Loc.ToLower().IndexOf(q) < 0) continue;
                string st = it.Enabled ? "已启用" : (it.SysDisabled ? "已禁用(系统)" : "已禁用(本工具)");
                int i = _grid.Rows.Add(it.Name, it.Command, it.Loc, st);
                _grid.Rows[i].Tag = it;
                if (!it.Enabled)
                {
                    _grid.Rows[i].DefaultCellStyle.ForeColor = C.TextDim;
                    disabled++;
                }
            }
            _sum.ForeColor = C.TextSub;
            _sum.Text = string.Format("共 {0} 项（禁用 {1} 项）。输入关键字可过滤；支持“名称/命令/位置”检索。", _all.Count, disabled);
        }

        StartupEntry RowEntry(int idx)
        {
            return idx >= 0 && idx < _grid.Rows.Count ? _grid.Rows[idx].Tag as StartupEntry : null;
        }

        StartupEntry Cur()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            return RowEntry(_grid.SelectedRows[0].Index);
        }

        void Toggle(bool enable)
        {
            var it = Cur();
            if (it == null) { MessageBox.Show(this, "请先选中一个启动项。", "提示"); return; }
            ToggleSingle(it, enable);
        }

        void ToggleSingle(StartupEntry it, bool enable)
        {
            string msg = StartupMgr.Enable(it, enable, it.FullPath);
            if (msg != null)
                MessageBox.Show(this, "操作失败：" + msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadItems();
        }

        void DeleteSel()
        {
            var it = Cur();
            if (it == null) { MessageBox.Show(this, "请先选中一个启动项。", "提示"); return; }
            string confirm = string.Format("删除启动项“{0}”？\n\n{1}\n\n删除前会导出 .reg 备份或复制到备份目录，可随时从备份恢复。", it.Name, it.Command);
            if (C.Ask(confirm, "确认删除", true) != DialogResult.Yes) return;
            string msg = StartupMgr.Delete(it);
            if (msg == null) MessageBox.Show(this, "已删除。", "完成");
            else if (msg.StartsWith("已删除")) MessageBox.Show(this, msg, "完成");
            else MessageBox.Show(this, "删除失败：" + msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadItems();
        }

        void OpenLocation()
        {
            var it = Cur();
            if (it == null) { MessageBox.Show(this, "请先选中一个启动项。", "提示"); return; }
            try
            {
                string[] p = it.FullPath.Split('|');
                if (p[0] == "R")
                {
                    // 复制完整路径
                    string path = (p[1] == "lm" ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + p[3];
                    Clipboard.SetText(path);
                    MessageBox.Show(this, "注册表位置已复制到剪贴板：\n" + path, "注册表启动项", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p[1] + "\"");
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "提示"); }
        }

        void AddDialog()
        {
            var ofd = new OpenFileDialog
            {
                Title = "选择要随系统启动的程序",
                Filter = "程序 (*.exe)|*.exe|命令脚本 (*.bat;*.cmd)|*.bat;*.cmd|所有文件 (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            var form = new Form
            {
                Text = "添加启动项",
                Size = new Size(C.S(420), C.S(300)),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = C.PageBg,
                AutoScaleMode = AutoScaleMode.None,
                MaximizeBox = false,
                MinimizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
            };
            var pan = new Panel { Dock = DockStyle.Fill, Padding = new Padding(C.S(18)) };
            var lbl = new Label
            {
                Text = "名称（显示在启动项列表中）：",
                Font = C.F(9f), ForeColor = C.TextSub, AutoSize = true, Location = new Point(0, C.S(6)),
            };
            var name = new TextBox
            {
                Font = C.F(9.5f),
                Width = C.S(330),
                Text = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName),
                Location = new Point(0, C.S(30)),
            };
            var cbUser = new RadioButton { Text = "仅当前用户启动（推荐）", Checked = true, AutoSize = true, Location = new Point(0, C.S(66)) };
            var cbSys = new RadioButton { Text = "所有用户启动（需管理员）", AutoSize = true, Location = new Point(0, C.S(96)) };
            var note = new Label
            {
                Text = "提示：Win8+ 禁用后可在任务管理器“启动”页管理。",
                Font = C.F(8.5f), ForeColor = C.TextDim, AutoSize = true, Location = new Point(0, C.S(130)),
            };
            var ok = C.Btn("添加", 0); ok.Width = C.S(100); ok.Location = new Point(0, C.S(168));
            var cancel = C.Btn("取消", 1); cancel.Width = C.S(90); cancel.Location = new Point(C.S(110), C.S(168));
            ok.Click += delegate
            {
                string nm = name.Text.Trim();
                if (nm.Length == 0) { MessageBox.Show(form, "请输入名称。", "提示"); return; }
                string err = StartupMgr.Add(nm, ofd.FileName, cbSys.Checked, false);
                if (err != null) MessageBox.Show(form, "添加失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else { form.DialogResult = DialogResult.OK; form.Close(); }
            };
            cancel.Click += delegate { form.DialogResult = DialogResult.Cancel; form.Close(); };
            pan.Controls.Add(lbl); pan.Controls.Add(name); pan.Controls.Add(cbUser); pan.Controls.Add(cbSys);
            pan.Controls.Add(note); pan.Controls.Add(ok); pan.Controls.Add(cancel);
            form.Controls.Add(pan);
            if (form.ShowDialog(this) == DialogResult.OK) LoadItems();
        }
    }
}
