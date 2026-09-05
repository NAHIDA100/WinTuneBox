using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTune
{
    // ═══ 网络优化：适配器/DNS/hosts/重置 ═══
    public class PgNet : UPage
    {
        ExGrid _grid;
        readonly ComboBox _dnsCb = new ComboBox();
        readonly TextBox _dns1 = new TextBox(), _dns2 = new TextBox();
        readonly TextBox _hostsBox = new TextBox();
        readonly Label _hostsState = new Label();
        bool _loaded, _hostsDirty;

        static readonly string[] DnsPresets = {
            "阿里 DNS     223.5.5.5 / 223.6.6.6",
            "腾讯 DNSPod  119.29.29.29 / 119.28.28.28",
            "114 DNS      114.114.114.114 / 114.114.115.115",
            "百度 DNS     180.76.76.76",
            "谷歌 DNS     8.8.8.8 / 8.8.4.4",
            "Cloudflare   1.1.1.1 / 1.0.0.1",
        };

        public PgNet()
        {
            Title("网络优化", "查看网络连接、切换公共 DNS、刷新缓存、重置 Winsock/TCP-IP，以及编辑 hosts 文件（修改 hosts 与 DNS 需管理员权限）。");
        }

        public override void OnShown()
        {
            base.OnShown();
            if (!_loaded) { Build(); _loaded = true; }
            RefreshAdapters();
            if (!_hostsDirty) _hostsBox.Text = NetMgr.ReadHosts();
        }

        void Build()
        {
            // ── 适配器 + DNS ──
            var c1 = Card();
            CardTitle(c1, "网络接口与 DNS 设置");
            _grid = new ExGrid { Dock = DockStyle.Top, Height = C.S(190) };
            _grid.Columns.Add(_grid.Col("Name", "连接名称", 18));
            _grid.Columns.Add(_grid.Col("Ip", "IPv4 地址", 14));
            _grid.Columns.Add(_grid.Col("Dns", "当前 DNS", 24));
            _grid.Columns.Add(_grid.Col("Dhcp", "DHCP", 8));
            _grid.Columns.Add(_grid.Col("St", "状态", 8));
            c1.Controls.Add(_grid);

            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _dnsCb.Items.AddRange(DnsPresets);
            _dnsCb.SelectedIndex = 0;
            _dnsCb.DropDownStyle = ComboBoxStyle.DropDownList;
            _dnsCb.Width = C.S(250); _dnsCb.Height = C.S(28); _dnsCb.Font = C.F(9f);
            _dnsCb.SelectedIndexChanged += delegate { FillDnsBoxes(); };
            flow.Controls.Add(C.Lbl("公共 DNS：", 9f, C.TextSub));
            flow.Controls.Add(_dnsCb);

            _dns1.Width = C.S(130); _dns1.Height = C.S(26); _dns1.Font = C.F(9f);
            _dns2.Width = C.S(130); _dns2.Height = C.S(26); _dns2.Font = C.F(9f);
            var sep = new Label { Text = "首/备：", Font = C.F(9f), ForeColor = C.TextSub, AutoSize = true, Margin = new Padding(C.S(10), 0, 0, 0) };
            flow.Controls.Add(sep); flow.Controls.Add(_dns1); flow.Controls.Add(_dns2);
            flow.Controls.Add(navBtn("应用到选中接口", delegate { ApplyDns(); }, 0));
            flow.Controls.Add(navBtn("还原为自动获取", delegate { RevertDns(); }, 1));
            flow.Controls.Add(navBtn("刷新缓存", delegate { FlushDns(); }, 1));
            flow.Controls.Add(navBtn("刷新列表", delegate { RefreshAdapters(); }, 1));
            flow.Controls.Add(navBtn("Winsock 重置…", delegate { NetshReset("winsock reset"); }, 2));
            flow.Controls.Add(navBtn("TCP/IP 重置…", delegate { NetshReset("int ip reset"); }, 2));
            c1.Controls.Add(flow);
            CardNote(c1, "说明：选择一个“已连接”的接口后点击“应用”。自定义时直接修改右侧首/备 DNS 框。修改后需重新连接网络或重启生效。");

            // ── hosts ──
            var c2 = Card();
            var cardTitle = CardTitle(c2, "hosts 文件编辑器");
            _hostsState.AutoSize = true; _hostsState.Font = C.F(9f); _hostsState.ForeColor = C.Green;
            _hostsState.Dock = DockStyle.Top;      // Top 停靠参与卡片高度计算（Bottom 会导致挤压）
            c2.Controls.Add(_hostsState);
            _hostsState.Text = NetMgr.HasBlock() ? "当前已包含遥测拦截段（可点下方按钮移除）" : "";

            _hostsBox.Font = C.Mono(9f);
            _hostsBox.Multiline = true;
            _hostsBox.ScrollBars = ScrollBars.Both;
            _hostsBox.WordWrap = false;
            _hostsBox.AcceptsReturn = true;
            _hostsBox.Dock = DockStyle.Top;
            _hostsBox.Height = C.S(200);
            _hostsBox.BackColor = Color.White;
            _hostsBox.BorderStyle = BorderStyle.FixedSingle;
            _hostsBox.TextChanged += delegate { _hostsDirty = true; };
            c2.Controls.Add(_hostsBox);

            var hf = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            hf.Controls.Add(navBtn("保存 hosts（先备份）", delegate { SaveHosts(); }, 0));
            hf.Controls.Add(navBtn("重新载入", delegate { _hostsBox.Text = NetMgr.ReadHosts(); _hostsDirty = false; }, 1));
            hf.Controls.Add(navBtn("还原为默认", delegate { ResetHosts(); }, 2));
            var toggle = navBtn(NetMgr.HasBlock() ? "移除遥测拦截段" : "添加遥测拦截段", delegate { ToggleBlock(); }, 1);
            hf.Controls.Add(toggle);
            hf.Controls.Add(navBtn("用记事本打开", delegate { try { System.Diagnostics.Process.Start("notepad.exe", NetMgr.HostsFile); } catch { } }, 1));
            c2.Controls.Add(hf);
            CardNote(c2, "“添加遥测拦截段”会写入微软遥测域名重定向（含明确注释标记，随时可移除）。文件修改立即生效并自动刷新 DNS 缓存。");
            EndSpace(20);

            FillDnsBoxes();
            FinishPage();
        }

        FlatBtn navBtn(string text, Action act, int kind = 1)
        {
            var b = C.Btn(text, kind);
            b.AutoSize = false; b.Width = C.S(text.Length > 8 ? 150 : 110);
            b.Margin = new Padding(0, 0, C.S(6), 0);
            b.Click += delegate { act(); };
            return b;
        }

        void FillDnsBoxes()
        {
            string s = (string)_dnsCb.SelectedItem;
            if (s == null) return;
            int slash = s.LastIndexOf('/');
            if (slash < 0) return;
            string first = s.Substring(slash + 1).Trim();
            string[] parts = first.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1) _dns1.Text = parts[0];
            if (parts.Length >= 2) _dns2.Text = parts[1]; else _dns2.Text = "";
        }

        AdapterInfo CurAdapter()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            return _grid.SelectedRows[0].Tag as AdapterInfo;
        }

        void RefreshAdapters()
        {
            Busy(delegate
            {
                var list = NetMgr.Adapters();
                C.On(this, delegate
                {
                    _grid.Rows.Clear();
                    foreach (var a in list)
                    {
                        int i = _grid.Rows.Add(a.Name, a.Ip ?? "—", a.Dns ?? "—",
                            a.Dhcp ? "DHCP" : "静态", a.Status);
                        _grid.Rows[i].Tag = a;
                        bool up = a.Status == "已连接";
                        if (!up) _grid.Rows[i].DefaultCellStyle.ForeColor = C.TextDim;
                        if (up && _grid.SelectedRows.Count == 0) _grid.Rows[i].Selected = true;
                    }
                });
            });
        }

        void ApplyDns()
        {
            var a = CurAdapter();
            if (a == null) { MessageBox.Show(this, "请先在列表中选中一个网络接口。", "提示"); return; }
            string s1 = _dns1.Text.Trim(), s2 = _dns2.Text.Trim();
            string servers = s1;
            if (s2.Length > 0 && s2 != s1) servers += "," + s2;
            if (servers.Length == 0) { MessageBox.Show(this, "请输入 DNS 地址。", "提示"); return; }
            if (!OS.IsElevated)
            {
                if (C.Ask("修改 DNS 需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string err = NetMgr.SetDns(a.Id, servers);
            if (err != null) { MessageBox.Show(this, "设置失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            NetMgr.FlushDns();
            MessageBox.Show(this, "已应用 DNS：" + servers.Replace(",", " / ") + "\n\n若未立即生效，请禁用再启用该网络连接或重启电脑。", "完成");
            RefreshAdapters();
        }

        void RevertDns()
        {
            var a = CurAdapter();
            if (a == null) { MessageBox.Show(this, "请先选中一个网络接口。", "提示"); return; }
            if (!OS.IsElevated)
            {
                if (C.Ask("修改 DNS 需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string err = NetMgr.SetDns(a.Id, null);
            NetMgr.FlushDns();
            MessageBox.Show(this, err == null ? "已还原为自动获取 DNS（DHCP）。" : "还原失败：" + err, "完成");
            RefreshAdapters();
        }

        void FlushDns()
        {
            string err = NetMgr.FlushDns();
            MessageBox.Show(this, err == null ? "DNS 缓存已刷新。" : err, "完成");
        }

        void NetshReset(string args)
        {
            if (C.Ask(string.Format("确定执行 “netsh {0}” 吗？\n该操作会重置 Winsock/TCP-IP 协议栈（修复网络问题），之后必须重启电脑。", args),
                    "确认重置", true) != DialogResult.Yes) return;
            if (!OS.IsElevated)
            {
                if (C.Ask("该操作需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string outp = Runner.Run("netsh.exe", args, 60000);
            string msg = (outp == null ? "执行完成。请重启电脑后测试网络。" : "返回结果：\n" + outp + "\n\n请重启电脑后测试网络。");
            MessageBox.Show(this, msg, "完成");
        }

        void SaveHosts()
        {
            if (!OS.IsElevated)
            {
                if (C.Ask("写入 hosts 需要管理员权限。是否以管理员身份重启本工具？", "权限提示") != DialogResult.Yes) return;
                FrmMain.ElevateSelf();
                return;
            }
            string err = NetMgr.SaveHosts(_hostsBox.Text);
            if (err == null)
            {
                _hostsDirty = false;
                _hostsState.Text = "已保存（原文件已备份到备份目录）。";
                MessageBox.Show(this, "hosts 已保存，DNS 缓存已刷新。", "完成");
            }
            else MessageBox.Show(this, "保存失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void ResetHosts()
        {
            if (C.Ask("将 hosts 还原为系统默认内容（只保留 localhost）？\n当前文件会先备份到备份目录。", "确认", true) != DialogResult.Yes) return;
            _hostsBox.Text = NetMgr.DefaultHosts();
            if (OS.IsElevated) SaveHosts();
            else
            {
                _hostsState.Text = "请先提权后再点“保存 hosts”。";
                MessageBox.Show(this, "当前未提权，请点击界面左下角权限条“以管理员身份重启”后再保存。", "提示");
            }
        }

        void ToggleBlock()
        {
            bool had = NetMgr.HasBlock();
            string[] r = NetMgr.ToggleBlock(!had);
            _hostsBox.Text = r[0];
            _hostsState.Text = r[1] == "1" ? "已添加拦截段，记得点“保存 hosts”生效。" : "已移除拦截段，记得点“保存 hosts”生效。";
            if (OS.IsElevated)
            {
                string err = NetMgr.SaveHosts(_hostsBox.Text);
                if (err == null) _hostsState.Text = r[1] == "1" ? "遥测拦截段已启用并保存。" : "遥测拦截段已移除并保存。";
                else _hostsState.Text = "保存失败：" + err;
            }
        }
    }
}
