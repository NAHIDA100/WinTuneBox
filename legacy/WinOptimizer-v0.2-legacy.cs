using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// ═══════════════════════════════════════════
//  Windows 优化工具箱  v0.2
//  通用优化：集成卸载器 / 内存清理 / 启动项管理
// ═══════════════════════════════════════════

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

// ── 调色板 ──────────────────────────────
static class Theme
{
    public static readonly Color BgDark      = ColorTranslator.FromHtml("#1a1a2e");
    public static readonly Color Sidebar      = ColorTranslator.FromHtml("#16213e");
    public static readonly Color SidebarHover = ColorTranslator.FromHtml("#1f2b4d");
    public static readonly Color SidebarActive= ColorTranslator.FromHtml("#0f3460");
    public static readonly Color Accent       = ColorTranslator.FromHtml("#00d4ff");
    public static readonly Color CardBg       = ColorTranslator.FromHtml("#222244");
    public static readonly Color CardBorder   = ColorTranslator.FromHtml("#2a2a4a");
    public static readonly Color TextPrimary  = ColorTranslator.FromHtml("#e0e0e0");
    public static readonly Color TextSecondary= ColorTranslator.FromHtml("#8888aa");
    public static readonly Color ToggleOn     = ColorTranslator.FromHtml("#00d4ff");
    public static readonly Color ToggleOff    = ColorTranslator.FromHtml("#444466");
    public static readonly Color HeaderBg     = ColorTranslator.FromHtml("#0d1117");
    public static readonly Color Danger       = ColorTranslator.FromHtml("#e74c3c");
    public static readonly Color Success      = ColorTranslator.FromHtml("#2ecc71");
    public static readonly Color Warning      = ColorTranslator.FromHtml("#f39c12");

    public static readonly Font FontTitle  = new Font("Segoe UI Semibold", 16f);
    public static readonly Font FontSub    = new Font("Segoe UI", 10f);
    public static readonly Font FontNav    = new Font("Segoe UI", 11f);
    public static readonly Font FontNavBold= new Font("Segoe UI Semibold", 11f);
    public static readonly Font FontCard   = new Font("Segoe UI Semibold", 12f);
    public static readonly Font FontSmall  = new Font("Segoe UI", 9f);
    public static readonly Font FontValue  = new Font("Consolas", 11f);
    public static readonly Font FontBtn    = new Font("Segoe UI", 10f);
}

// ═══════════════════════════════════════════
//  数据模型
// ═══════════════════════════════════════════

class InstalledProgram
{
    public string Name;
    public string Publisher;
    public string Version;
    public string InstallDate;
    public string SizeStr;
    public string UninstallString;
    public string InstallLocation;
    public string RegistryKey;
}

class StartupItem
{
    public string Name;
    public string Command;
    public string Location;
    public bool Enabled;
    public RegistryKey RootKey;
    public string SubKeyPath;
    public bool IsWow64;
}

// ═══════════════════════════════════════════
//  注册表辅助
// ═══════════════════════════════════════════

static class RegistryHelper
{
    // ── 读取已安装程序 ──
    public static List<InstalledProgram> GetInstalledPrograms()
    {
        var list = new List<InstalledProgram>();
        string[] roots = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var root in roots)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(root))
                {
                    if (key == null) continue;
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var sub = key.OpenSubKey(subName))
                            {
                                var prog = ReadProgram(sub, subName);
                                if (prog != null) list.Add(prog);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // HKCU
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (key != null)
                {
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var sub = key.OpenSubKey(subName))
                            {
                                var prog = ReadProgram(sub, subName);
                                if (prog != null) list.Add(prog);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    static InstalledProgram ReadProgram(RegistryKey sub, string keyName)
    {
        string name = sub.GetValue("DisplayName") as string;
        if (string.IsNullOrEmpty(name)) return null;
        string uninstall = sub.GetValue("UninstallString") as string;
        if (string.IsNullOrEmpty(uninstall)) return null;

        return new InstalledProgram
        {
            Name           = name,
            Publisher      = sub.GetValue("Publisher") as string ?? "",
            Version        = sub.GetValue("DisplayVersion") as string ?? "",
            InstallDate    = sub.GetValue("InstallDate") as string ?? "",
            SizeStr        = FormatSize(sub.GetValue("EstimatedSize")),
            UninstallString= uninstall,
            InstallLocation= sub.GetValue("InstallLocation") as string ?? "",
            RegistryKey    = keyName,
        };
    }

    static string FormatSize(object val)
    {
        if (val == null) return "";
        long kb;
        try { kb = Convert.ToInt64(val); } catch { return ""; }
        if (kb < 1024) return kb + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("F1") + " MB";
        return (mb / 1024.0).ToString("F2") + " GB";
    }

    // ── 读取启动项 ──
    public static List<StartupItem> GetStartupItems()
    {
        var list = new List<StartupItem>();
        AddStartupFromKey(list, Registry.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        AddStartupFromKey(list, Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        try
        {
            AddStartupFromKey(list, Registry.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true);
        }
        catch { }
        return list;
    }

    static void AddStartupFromKey(List<StartupItem> list,
        RegistryKey root, string path, bool isWow64)
    {
        try
        {
            using (var key = root.OpenSubKey(path))
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    string cmd = key.GetValue(name) as string;
                    if (string.IsNullOrEmpty(cmd)) continue;
                    // 检查是否被禁用（移到 RunOnce 或标记）
                    list.Add(new StartupItem
                    {
                        Name      = name,
                        Command   = cmd,
                        Location  = root == Registry.CurrentUser ? "HKCU" : "HKLM",
                        Enabled   = true,
                        RootKey   = root,
                        SubKeyPath= path,
                        IsWow64   = isWow64,
                    });
                }
            }
        }
        catch { }

        // 检查 Run\AutorunsDisabled (Sysinternals 风格)
        try
        {
            string disabledPath = path + @"\AutorunsDisabled";
            using (var key = root.OpenSubKey(disabledPath))
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    string cmd = key.GetValue(name) as string;
                    if (string.IsNullOrEmpty(cmd)) continue;
                    list.Add(new StartupItem
                    {
                        Name      = name,
                        Command   = cmd,
                        Location  = root == Registry.CurrentUser ? "HKCU" : "HKLM",
                        Enabled   = false,
                        RootKey   = root,
                        SubKeyPath= disabledPath,
                        IsWow64   = isWow64,
                    });
                }
            }
        }
        catch { }
    }

    // ── 切换启动项状态 ──
    public static bool ToggleStartup(StartupItem item, bool enable)
    {
        try
        {
            string activePath = item.SubKeyPath;
            // 计算对应的禁用路径
            string disabledPath;
            if (activePath.EndsWith(@"\AutorunsDisabled"))
                disabledPath = activePath.Substring(0,
                    activePath.Length - @"\AutorunsDisabled".Length);
            else
                disabledPath = activePath + @"\AutorunsDisabled";

            string srcPath  = enable ? disabledPath : activePath;
            string dstPath  = enable ? activePath : disabledPath;

            using (var src = item.RootKey.OpenSubKey(srcPath, true))
            {
                if (src == null) return false;
                object val = src.GetValue(item.Name);
                if (val == null) return false;

                // 创建目标 key 并写入
                using (var dst = item.RootKey.CreateSubKey(dstPath))
                {
                    dst.SetValue(item.Name, val);
                }
                // 从源删除
                src.DeleteValue(item.Name, false);
            }
            item.Enabled = enable;
            return true;
        }
        catch { return false; }
    }
}

// ═══════════════════════════════════════════
//  内存清理
// ═══════════════════════════════════════════

static class MemoryCleaner
{
    [DllImport("psapi.dll")]
    static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    public static long GetUsedMemoryBytes()
    {
        return GC.GetTotalMemory(false);
    }

    /// <summary>
    /// 清理当前进程工作集，返回释放的字节数
    /// </summary>
    public static long CleanCurrentProcess()
    {
        long before = Environment.WorkingSet;
        try
        {
            Process proc = Process.GetCurrentProcess();
            EmptyWorkingSet(proc.Handle);
            SetProcessWorkingSetSize(proc.Handle, -1, -1);
        }
        catch { }
        long after = Environment.WorkingSet;
        return Math.Max(0, before - after);
    }
}

// ═══════════════════════════════════════════
//  深色主题 DataGridView
// ═══════════════════════════════════════════

class DarkDataGridView : DataGridView
{
    public DarkDataGridView()
    {
        BackgroundColor        = Theme.BgDark;
        BorderStyle            = BorderStyle.None;
        CellBorderStyle        = DataGridViewCellBorderStyle.SingleHorizontal;
        GridColor              = Theme.CardBorder;
        RowHeadersVisible      = false;
        AllowUserToAddRows     = false;
        AllowUserToDeleteRows  = false;
        AllowUserToResizeRows  = false;
        ReadOnly               = true;
        SelectionMode          = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect            = false;
        AutoSizeColumnsMode    = DataGridViewAutoSizeColumnsMode.Fill;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;

        ColumnHeadersDefaultCellStyle.BackColor       = Theme.CardBg;
        ColumnHeadersDefaultCellStyle.ForeColor       = Theme.Accent;
        ColumnHeadersDefaultCellStyle.Font            = Theme.FontNavBold;
        ColumnHeadersDefaultCellStyle.Alignment       = DataGridViewContentAlignment.MiddleLeft;
        ColumnHeadersDefaultCellStyle.Padding         = new Padding(8, 0, 0, 0);
        ColumnHeadersHeight = 36;

        DefaultCellStyle.BackColor       = Theme.BgDark;
        DefaultCellStyle.ForeColor       = Theme.TextPrimary;
        DefaultCellStyle.Font            = Theme.FontSub;
        DefaultCellStyle.SelectionBackColor = Theme.SidebarActive;
        DefaultCellStyle.SelectionForeColor = Theme.Accent;
        DefaultCellStyle.Padding         = new Padding(8, 4, 4, 4);

        AlternatingRowsDefaultCellStyle.BackColor = Theme.Sidebar;

        AutoGenerateColumns = false;
    }
}

// ═══════════════════════════════════════════
//  主窗体
// ═══════════════════════════════════════════
class MainForm : Form
{
    // ── 导航项 ──
    readonly NavItem[] navItems = new NavItem[]
    {
        new NavItem("通用优化",  "⚙"),
        new NavItem("Win11 优化","◈"),
        new NavItem("Win10 优化","▣"),
        new NavItem("Win7 优化", "▦"),
    };

    int selectedIndex = 0;
    Rectangle sidebarRect;
    Rectangle contentRect;
    Rectangle headerRect;

    // ── 内容页 ──
    Panel[] pages;

    // ── 卸载器 ──
    DarkDataGridView dgvPrograms;
    List<InstalledProgram> allPrograms = new List<InstalledProgram>();
    TextBox txtSearch;
    Label lblUninstallStatus;

    // ── 启动项 ──
    DarkDataGridView dgvStartup;

    // ── 内存清理 ──
    Label lblMemoryUsage;

    // ── 侧边栏按钮区域 ──
    Rectangle memBtnRect;

    public MainForm()
    {
        Text = "Windows 优化工具箱";
        Size = new Size(1020, 680);
        MinimumSize = new Size(860, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgDark;
        DoubleBuffered = true;
        Font = Theme.FontSub;
        AutoScaleMode = AutoScaleMode.Dpi;

        // 创建内容页
        pages = new Panel[]
        {
            CreateGeneralPage(),
            CreatePage("Win11 专属优化", new string[] {
                "恢复经典右键菜单", "禁用小组件 (Widgets)", "禁用聊天图标",
                "关闭任务栏合并", "禁用 Snap 布局建议", "移除开始菜单推荐项",
                "禁用 Copilot 按钮", "恢复经典文件资源管理器",
                "关闭聚焦锁屏广告", "禁用 Edge 默认浏览器提示",
            }),
            CreatePage("Win10 专属优化", new string[] {
                "禁用 Windows Ink 工作区", "关闭时间线 (Timeline)",
                "禁用活动历史记录", "关闭锁屏提示", "移除 OneDrive 集成",
                "禁用 Game Bar / DVR", "关闭自动更新", "禁用传递优化 (P2P更新)",
            }),
            CreatePage("Win7 专属优化", new string[] {
                "禁用 Aero Snap", "关闭系统还原", "禁用 Windows Search 索引",
                "关闭自动碎片整理", "禁用操作中心提示", "关闭 IE 增强安全配置",
                "禁用远程差分压缩", "关闭家庭组",
            }),
        };

        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = (i == 0);
            Controls.Add(pages[i]);
        }

        Resize     += (s, e) => LayoutRects();
        Load       += (s, e) => { LayoutRects(); LoadPrograms(); LoadStartupItems(); };
        Paint      += OnPaintMain;
        MouseClick += OnMouseClick;
        MouseMove  += (s, e) => { if (sidebarRect.Width > 0) Invalidate(sidebarRect); };

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    void LayoutRects()
    {
        int headerH = 60;
        int sidebarW = 200;
        headerRect   = new Rectangle(0, 0, ClientSize.Width, headerH);
        sidebarRect  = new Rectangle(0, headerH, sidebarW, ClientSize.Height - headerH);
        contentRect  = new Rectangle(sidebarW, headerH,
                                     ClientSize.Width - sidebarW,
                                     ClientSize.Height - headerH);

        foreach (var p in pages)
            p.Bounds = new Rectangle(contentRect.X + 12, contentRect.Y + 12,
                                     contentRect.Width - 24, contentRect.Height - 24);

        // 侧边栏底部清理按钮区域
        int btnH = 44;
        memBtnRect = new Rectangle(10, sidebarRect.Bottom - 90, sidebarW - 20, btnH);

        Invalidate();
    }

    // ═══════════════════════════════════════
    //  绘制（侧边栏 / 标题栏）
    // ═══════════════════════════════════════
    void OnPaintMain(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ── 标题栏 ──
        using (var brush = new SolidBrush(Theme.HeaderBg))
            g.FillRectangle(brush, headerRect);

        string title = "Windows 优化工具箱";
        var titleSize = g.MeasureString(title, Theme.FontTitle);
        using (var brush = new SolidBrush(Theme.TextPrimary))
            g.DrawString(title, Theme.FontTitle, brush,
                         20, (headerRect.Height - titleSize.Height) / 2);

        string ver = "v0.2";
        using (var brush = new SolidBrush(Theme.TextSecondary))
        {
            var verSize = g.MeasureString(ver, Theme.FontSmall);
            g.DrawString(ver, Theme.FontSmall, brush,
                         20 + titleSize.Width + 10,
                         (headerRect.Height - verSize.Height) / 2 + 4);
        }

        string sysInfo = Environment.OSVersion.VersionString + "  |  " +
                         Environment.MachineName;
        using (var brush = new SolidBrush(Theme.TextSecondary))
        {
            var sysSize = g.MeasureString(sysInfo, Theme.FontSmall);
            g.DrawString(sysInfo, Theme.FontSmall, brush,
                         headerRect.Width - sysSize.Width - 20,
                         (headerRect.Height - sysSize.Height) / 2);
        }

        using (var pen = new Pen(Theme.Accent, 1))
            g.DrawLine(pen, 0, headerRect.Bottom, ClientSize.Width, headerRect.Bottom);

        // ── 侧边栏 ──
        using (var brush = new SolidBrush(Theme.Sidebar))
            g.FillRectangle(brush, sidebarRect);

        using (var pen = new Pen(Theme.CardBorder, 1))
            g.DrawLine(pen, sidebarRect.Right - 1, sidebarRect.Top,
                            sidebarRect.Right - 1, sidebarRect.Bottom);

        // 导航项
        int itemH = 52;
        int itemY = sidebarRect.Top + 12;
        for (int i = 0; i < navItems.Length; i++)
        {
            var itemRect = new Rectangle(sidebarRect.X, itemY + i * itemH,
                                         sidebarRect.Width, itemH);
            bool hover = itemRect.Contains(PointToClient(Cursor.Position)) && i != selectedIndex;
            bool active = i == selectedIndex;

            if (active)
            {
                using (var brush = new SolidBrush(Theme.SidebarActive))
                    g.FillRectangle(brush, itemRect);
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillRectangle(brush, itemRect.X, itemRect.Y, 3, itemRect.Height);
            }
            else if (hover)
            {
                using (var brush = new SolidBrush(Theme.SidebarHover))
                    g.FillRectangle(brush, itemRect);
            }

            var iconFont = new Font("Segoe UI Symbol", 16f);
            var iconSize = g.MeasureString(navItems[i].Icon, iconFont);
            using (var brush = new SolidBrush(active ? Theme.Accent : Theme.TextSecondary))
                g.DrawString(navItems[i].Icon, iconFont, brush,
                             itemRect.X + 18,
                             itemRect.Y + (itemRect.Height - iconSize.Height) / 2);

            var navFont = active ? Theme.FontNavBold : Theme.FontNav;
            using (var brush = new SolidBrush(active ? Theme.Accent : Theme.TextPrimary))
                g.DrawString(navItems[i].Label, navFont, brush,
                             itemRect.X + 50,
                             itemRect.Y + (itemRect.Height - g.MeasureString(navItems[i].Label, navFont).Height) / 2);
        }

        // ── 一键清理内存按钮 ──
        bool memHover = memBtnRect.Contains(PointToClient(Cursor.Position));
        using (var btnPath = RoundedRect(memBtnRect, 8))
        {
            using (var brush = new SolidBrush(memHover ? Theme.Accent : Color.FromArgb(40, Theme.Accent)))
                g.FillPath(brush, btnPath);
            using (var pen = new Pen(Theme.Accent, 1.5f))
                g.DrawPath(pen, btnPath);
        }

        string memBtnText = "🧹 一键清理内存";
        using (var brush = new SolidBrush(Theme.Accent))
        {
            var btnSize = g.MeasureString(memBtnText, Theme.FontBtn);
            g.DrawString(memBtnText, Theme.FontBtn, brush,
                         memBtnRect.X + (memBtnRect.Width - btnSize.Width) / 2,
                         memBtnRect.Y + (memBtnRect.Height - btnSize.Height) / 2);
        }

        // ── 底部版权 ──
        string copyright = "仅供个人使用";
        using (var brush = new SolidBrush(Theme.TextSecondary))
        {
            var cSize = g.MeasureString(copyright, Theme.FontSmall);
            g.DrawString(copyright, Theme.FontSmall, brush,
                         sidebarRect.X + (sidebarRect.Width - cSize.Width) / 2,
                         sidebarRect.Bottom - 26);
        }
    }

    // ── 鼠标点击 ──
    void OnMouseClick(object sender, MouseEventArgs e)
    {
        // 检查侧边栏导航
        int itemH = 52;
        int itemY = sidebarRect.Top + 12;
        for (int i = 0; i < navItems.Length; i++)
        {
            var itemRect = new Rectangle(sidebarRect.X, itemY + i * itemH,
                                         sidebarRect.Width, itemH);
            if (itemRect.Contains(e.Location))
            {
                SelectPage(i);
                return;
            }
        }

        // 检查清理内存按钮
        if (memBtnRect.Contains(e.Location))
        {
            DoMemoryClean();
        }
    }

    void SelectPage(int index)
    {
        if (index == selectedIndex) return;
        pages[selectedIndex].Visible = false;
        selectedIndex = index;
        pages[selectedIndex].Visible = true;
        Invalidate();
    }

    // ═══════════════════════════════════════
    //  内存清理功能
    // ═══════════════════════════════════════

    void DoMemoryClean()
    {
        long freed = MemoryCleaner.CleanCurrentProcess();
        UpdateMemoryLabel();
        string msg = freed > 0
            ? string.Format("已释放 {0:F1} MB 内存", freed / (1024.0 * 1024.0))
            : "内存已处于最优状态";
        lblUninstallStatus.Text = "🧹 " + msg;
        Invalidate(memBtnRect);
    }

    void UpdateMemoryLabel()
    {
        if (lblMemoryUsage == null) return;
        var proc = Process.GetCurrentProcess();
        double mb = proc.WorkingSet64 / (1024.0 * 1024.0);
        lblMemoryUsage.Text = string.Format("本程序内存: {0:F1} MB", mb);
    }

    // ═══════════════════════════════════════
    //  通用优化页（三合一）
    // ═══════════════════════════════════════

    Panel CreateGeneralPage()
    {
        var panel = new Panel
        {
            AutoScroll = false,
            BackColor = Color.Transparent,
        };

        // ── 子功能切换按钮行（自绘） ──
        string[] tabs = { "集成卸载器", "内存清理", "启动项管理" };
        int tabSel = 0;
        Panel[] tabPanels = new Panel[3];

        tabPanels[0] = CreateUninstallerPanel();
        tabPanels[1] = CreateMemoryPanel();
        tabPanels[2] = CreateStartupPanel();

        // 状态标签（底部）
        lblUninstallStatus = new Label
        {
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontSmall,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Dock = DockStyle.Bottom,
        };
        panel.Controls.Add(lblUninstallStatus);

        // 子面板全部放入（用显式定位，Resize 时更新位置）
        foreach (var tp in tabPanels) { tp.Visible = false; panel.Controls.Add(tp); }
        tabPanels[0].Visible = true;

        // 标签栏
        var tabRow = new Panel { Height = 44, BackColor = Color.Transparent };
        panel.Controls.Add(tabRow);

        // 布局：tabRow 在顶部，子面板填充剩余空间
        panel.Resize += (s, e) =>
        {
            int w = panel.ClientSize.Width;
            int h = panel.ClientSize.Height;
            tabRow.Bounds = new Rectangle(0, 0, w, 44);
            foreach (var tp in tabPanels)
                tp.Bounds = new Rectangle(0, 44, w, h - 44 - 24);
            lblUninstallStatus.Bounds = new Rectangle(0, h - 24, w, 24);
        };
        // 首次布局
        panel.Layout += (s, e) =>
        {
            int w = panel.ClientSize.Width;
            int h = panel.ClientSize.Height;
            tabRow.Bounds = new Rectangle(0, 0, w, 44);
            foreach (var tp in tabPanels)
                tp.Bounds = new Rectangle(0, 44, w, h - 44 - 24);
            lblUninstallStatus.Bounds = new Rectangle(0, h - 24, w, 24);
        };

        // 绘制标签
        var tabBtnRects = new Rectangle[tabs.Length];
        tabRow.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = 4;
            for (int i = 0; i < tabs.Length; i++)
            {
                var sz = g.MeasureString(tabs[i], Theme.FontNavBold);
                int bw = (int)sz.Width + 28;
                tabBtnRects[i] = new Rectangle(x, 6, bw, 32);
                bool hover = tabBtnRects[i].Contains(tabRow.PointToClient(Cursor.Position));
                bool active = i == tabSel;

                using (var path = RoundedRect(tabBtnRects[i], 6))
                using (var brush = new SolidBrush(active ? Theme.Accent :
                    hover ? Theme.SidebarHover : Theme.CardBg))
                    g.FillPath(brush, path);

                using (var brush = new SolidBrush(active ? Color.White : Theme.TextSecondary))
                    g.DrawString(tabs[i], active ? Theme.FontNavBold : Theme.FontNav, brush,
                                 x + 14, 12);
                x += bw + 6;
            }
        };
        tabRow.MouseClick += (s, e) =>
        {
            for (int i = 0; i < tabBtnRects.Length; i++)
            {
                if (tabBtnRects[i].Contains(e.Location))
                {
                    tabPanels[tabSel].Visible = false;
                    tabSel = i;
                    tabPanels[tabSel].Visible = true;
                    tabRow.Invalidate();
                    if (i == 2) LoadStartupItems();
                    break;
                }
            }
        };
        tabRow.MouseMove += (s, e) => tabRow.Invalidate();

        return panel;
    }

    // ── 卸载器面板 ──
    Panel CreateUninstallerPanel()
    {
        var pnl = new Panel { BackColor = Color.Transparent };

        // 工具栏
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.CardBg };

        txtSearch = new TextBox
        {
            Location = new Point(12, 10),
            Size = new Size(300, 28),
            BackColor = Theme.BgDark,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontSub,
        };
        txtSearch.TextChanged += (s, e) => FilterPrograms();
        txtSearch.GotFocus += (s, e) => { if (txtSearch.Text == "搜索程序...") { txtSearch.Text = ""; txtSearch.ForeColor = Theme.TextPrimary; } };
        txtSearch.LostFocus += (s, e) => { if (string.IsNullOrEmpty(txtSearch.Text)) { txtSearch.Text = "搜索程序..."; txtSearch.ForeColor = Theme.TextSecondary; } };
        txtSearch.Text = "搜索程序...";
        txtSearch.ForeColor = Theme.TextSecondary;
        toolbar.Controls.Add(txtSearch);

        // 按钮
        int bx = 328;
        toolbar.Controls.Add(MakeButton("卸载选中", bx, Theme.Danger, (s, e) => UninstallSelected()));
        bx += 86;
        toolbar.Controls.Add(MakeButton("刷新列表", bx, Theme.Accent, (s, e) => LoadPrograms()));
        bx += 86;
        toolbar.Controls.Add(MakeButton("安装位置", bx, Theme.TextSecondary, (s, e) => OpenInstallLocation()));

        // 计数标签
        var lblCount = new Label
        {
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontSmall,
            AutoSize = true,
            Location = new Point(bx + 96, 18),
            Text = "",
        };
        toolbar.Controls.Add(lblCount);

        pnl.Controls.Add(toolbar);

        // DataGridView
        dgvPrograms = new DarkDataGridView { Dock = DockStyle.Fill };
        dgvPrograms.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "Name",      HeaderText = "程序名称",   FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "Publisher",  HeaderText = "发布者",     FillWeight = 25 },
            new DataGridViewTextBoxColumn { Name = "Version",   HeaderText = "版本",       FillWeight = 12 },
            new DataGridViewTextBoxColumn { Name = "Size",      HeaderText = "大小",       FillWeight = 12 },
            new DataGridViewTextBoxColumn { Name = "Date",      HeaderText = "安装日期",   FillWeight = 16 },
        });
        dgvPrograms.DataBindingComplete += (s, e) =>
        {
            lblCount.Text = string.Format("共 {0} 个程序", dgvPrograms.RowCount);
        };
        pnl.Controls.Add(dgvPrograms);

        return pnl;
    }

    Button MakeButton(string text, int x, Color color, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, 8),
            Size = new Size(78, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = color == Theme.TextSecondary ? Theme.BgDark : color,
            ForeColor = Color.White,
            Font = Theme.FontBtn,
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = color;
        btn.Click += onClick;
        return btn;
    }

    void LoadPrograms()
    {
        allPrograms = RegistryHelper.GetInstalledPrograms();
        FilterPrograms();
    }

    void FilterPrograms()
    {
        if (dgvPrograms == null || allPrograms == null) return;
        string q = (txtSearch.Text == "搜索程序..." ? "" : txtSearch.Text).ToLower();
        dgvPrograms.Rows.Clear();
        foreach (var p in allPrograms)
        {
            if (q.Length > 0 && !p.Name.ToLower().Contains(q) &&
                !p.Publisher.ToLower().Contains(q)) continue;
            dgvPrograms.Rows.Add(p.Name, p.Publisher, p.Version, p.SizeStr, p.InstallDate);
        }
        lblUninstallStatus.Text = string.Format("共 {0} 个程序", dgvPrograms.RowCount);
    }

    InstalledProgram GetSelectedProgram()
    {
        if (dgvPrograms.SelectedRows.Count == 0) return null;
        string name = dgvPrograms.SelectedRows[0].Cells["Name"].Value as string;
        return allPrograms.Find(p => p.Name == name);
    }

    void UninstallSelected()
    {
        var prog = GetSelectedProgram();
        if (prog == null)
        {
            lblUninstallStatus.Text = "请先选择一个程序";
            return;
        }

        var result = MessageBox.Show(
            string.Format("确定要卸载以下程序吗？\n\n{0}\n发布者: {1}", prog.Name, prog.Publisher),
            "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            // 提取可执行文件和参数
            string cmd = prog.UninstallString.Trim();
            string exe, args;
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                exe = cmd.Substring(1, end - 1);
                args = cmd.Substring(end + 1).Trim();
            }
            else
            {
                int sp = cmd.IndexOf(' ');
                if (sp > 0) { exe = cmd.Substring(0, sp); args = cmd.Substring(sp + 1); }
                else { exe = cmd; args = ""; }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
            });

            lblUninstallStatus.Text = "正在卸载: " + prog.Name + " ...";

            // 延迟刷新
            var timer = new Timer { Interval = 5000 };
            timer.Tick += (s2, e2) => { timer.Stop(); LoadPrograms(); };
            timer.Start();
        }
        catch (Exception ex)
        {
            lblUninstallStatus.Text = "卸载失败: " + ex.Message;
        }
    }

    void OpenInstallLocation()
    {
        var prog = GetSelectedProgram();
        if (prog == null) { lblUninstallStatus.Text = "请先选择一个程序"; return; }
        string path = prog.InstallLocation;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            lblUninstallStatus.Text = "安装目录不存在: " + path;
            return;
        }
        Process.Start("explorer.exe", path);
    }

    // ── 内存清理面板 ──
    Panel CreateMemoryPanel()
    {
        var pnl = new Panel { BackColor = Color.Transparent };

        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 200,
            BackColor = Theme.CardBg,
            Padding = new Padding(20),
        };

        card.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.Accent))
                g.DrawString("内存清理", Theme.FontCard, brush, 20, 12);
            using (var pen = new Pen(Theme.CardBorder, 1))
                g.DrawLine(pen, 20, 38, card.Width - 40, 38);
        };

        // 说明
        var lblDesc = new Label
        {
            Text = "清理当前进程的工作集内存（类似 PCL 启动器的内存优化功能）\n" +
                   "该操作会释放本程序占用的多余内存，不影响系统其他程序。\n" +
                   "建议在程序运行一段时间、内存占用较高时使用。",
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontSub,
            Location = new Point(20, 50),
            AutoSize = true,
            MaximumSize = new Size(600, 0),
        };
        card.Controls.Add(lblDesc);

        // 内存使用显示
        lblMemoryUsage = new Label
        {
            Text = "本程序内存: 计算中...",
            ForeColor = Theme.TextPrimary,
            Font = new Font("Consolas", 14f),
            Location = new Point(20, 120),
            AutoSize = true,
        };
        card.Controls.Add(lblMemoryUsage);

        // 刷新按钮
        var btnRefresh = new Button
        {
            Text = "刷新",
            Location = new Point(360, 116),
            Size = new Size(60, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBg,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontBtn,
            Cursor = Cursors.Hand,
        };
        btnRefresh.FlatAppearance.BorderColor = Theme.CardBorder;
        btnRefresh.Click += (s, e) => UpdateMemoryLabel();
        card.Controls.Add(btnRefresh);

        // 清理按钮
        var btnClean = new Button
        {
            Text = "🧹 立即清理内存",
            Location = new Point(20, 155),
            Size = new Size(180, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Font = Theme.FontBtn,
            Cursor = Cursors.Hand,
        };
        btnClean.FlatAppearance.BorderSize = 0;
        btnClean.Click += (s, e) => DoMemoryClean();
        card.Controls.Add(btnClean);

        card.Height = 200;
        pnl.Controls.Add(card);
        return pnl;
    }

    // ── 启动项管理面板 ──
    Panel CreateStartupPanel()
    {
        var pnl = new Panel { BackColor = Color.Transparent };

        // 工具栏
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.CardBg };
        toolbar.Controls.Add(MakeButton("刷新", 12, Theme.Accent, (s, e) => LoadStartupItems()));
        toolbar.Controls.Add(MakeButton("切换状态", 100, Theme.Warning, (s, e) => ToggleSelectedStartup()));
        toolbar.Controls.Add(MakeButton("删除", 188, Theme.Danger, (s, e) => DeleteSelectedStartup()));
        pnl.Controls.Add(toolbar);

        // 启动项列表
        dgvStartup = new DarkDataGridView { Dock = DockStyle.Fill };
        dgvStartup.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "Name",    HeaderText = "名称",   FillWeight = 25 },
            new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "命令",   FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "Location",HeaderText = "位置",   FillWeight = 12 },
            new DataGridViewTextBoxColumn { Name = "Status",  HeaderText = "状态",   FillWeight = 18 },
        });
        dgvStartup.Tag = new List<StartupItem>();
        pnl.Controls.Add(dgvStartup);

        return pnl;
    }

    void LoadStartupItems()
    {
        if (dgvStartup == null) return;
        var items = RegistryHelper.GetStartupItems();
        dgvStartup.Tag = items;
        dgvStartup.Rows.Clear();
        foreach (var item in items)
        {
            dgvStartup.Rows.Add(item.Name, item.Command, item.Location,
                         item.Enabled ? "✓ 已启用" : "✗ 已禁用");
            var row = dgvStartup.Rows[dgvStartup.Rows.Count - 1];
            row.DefaultCellStyle.ForeColor = item.Enabled ? Theme.Success : Theme.Danger;
        }
        if (lblUninstallStatus != null)
            lblUninstallStatus.Text = string.Format("启动项: {0} 个", items.Count);
    }

    void ToggleSelectedStartup()
    {
        if (dgvStartup == null || dgvStartup.SelectedRows.Count == 0) return;
        var items = dgvStartup.Tag as List<StartupItem>;
        if (items == null) return;

        int idx = dgvStartup.SelectedRows[0].Index;
        if (idx < 0 || idx >= items.Count) return;

        var item = items[idx];
        bool newState = !item.Enabled;
        if (RegistryHelper.ToggleStartup(item, newState))
        {
            LoadStartupItems();
            lblUninstallStatus.Text = string.Format("{0}: 已{1}", item.Name,
                newState ? "启用" : "禁用");
        }
        else
        {
            lblUninstallStatus.Text = "操作失败（可能需要管理员权限）";
        }
    }

    void DeleteSelectedStartup()
    {
        if (dgvStartup == null || dgvStartup.SelectedRows.Count == 0) return;
        var items = dgvStartup.Tag as List<StartupItem>;
        if (items == null) return;

        int idx = dgvStartup.SelectedRows[0].Index;
        if (idx < 0 || idx >= items.Count) return;

        var item = items[idx];
        var result = MessageBox.Show(
            string.Format("确定要删除启动项 \"{0}\" 吗？\n\n命令: {1}", item.Name, item.Command),
            "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            using (var key = item.RootKey.OpenSubKey(item.SubKeyPath, true))
            {
                if (key != null)
                {
                    key.DeleteValue(item.Name, false);
                    LoadStartupItems();
                    lblUninstallStatus.Text = "已删除: " + item.Name;
                }
            }
        }
        catch
        {
            lblUninstallStatus.Text = "删除失败（可能需要管理员权限）";
        }
    }

    // ═══════════════════════════════════════
    //  通用页面创建（Win11/Win10/Win7）
    // ═══════════════════════════════════════

    Panel CreatePage(string title, string[] items)
    {
        var panel = new Panel
        {
            AutoScroll = true,
            BackColor = Color.Transparent,
        };

        var card = CreateCard(title, 0);
        foreach (var item in items)
            AddToggleRow(card, item);
        panel.Controls.Add(card);

        return panel;
    }

    // ═══════════════════════════════════════
    //  UI 组件辅助
    // ═══════════════════════════════════════

    Panel CreateCard(string title, int topOffset)
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Theme.CardBg,
            Padding = new Padding(16, 12, 16, 12),
        };
        card.Margin = new Padding(0, topOffset > 0 ? 16 : 0, 0, 0);

        card.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.Accent))
                g.DrawString(title, Theme.FontCard, brush, 16, 10);
            using (var pen = new Pen(Theme.CardBorder, 1))
                g.DrawLine(pen, 16, 36, card.Width - 32, 36);
        };
        return card;
    }

    void AddToggleRow(Panel card, string text)
    {
        int rowH = 40;
        bool state = false;

        var row = new Panel
        {
            Dock = DockStyle.Top,
            Height = rowH,
            BackColor = Color.Transparent,
        };

        row.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.TextPrimary))
                g.DrawString(text, Theme.FontSub, brush, 16, 10);
            int tw = 42, th = 22;
            int tx = row.Width - tw - 20;
            int ty = (row.Height - th) / 2;
            var toggleRect = new Rectangle(tx, ty, tw, th);
            using (var path = RoundedRect(toggleRect, th / 2))
            using (var brush = new SolidBrush(state ? Theme.ToggleOn : Theme.ToggleOff))
                g.FillPath(brush, path);
            int dotR = 8;
            int dotX = state ? tx + tw - dotR - 4 : tx + 4;
            int dotY = ty + (th - dotR * 2) / 2;
            using (var brush = new SolidBrush(Color.White))
                g.FillEllipse(brush, dotX, dotY, dotR * 2, dotR * 2);
        };

        row.MouseClick += (s, e) =>
        {
            state = !state;
            row.Invalidate();
        };
        row.Cursor = Cursors.Hand;
        card.Controls.Add(row);
        card.Height += rowH;
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// ── 导航项数据 ──
class NavItem
{
    public string Label;
    public string Icon;
    public NavItem(string label, string icon) { Label = label; Icon = icon; }
}
