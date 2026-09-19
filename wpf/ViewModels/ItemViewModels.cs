using System;
using System.Collections.Generic;
using System.Windows;

namespace WinTune.Wpf
{
    /// <summary>一键优化项：状态 0=待优化 1=已优化 2=部分 3=不支持</summary>
    public class TweakItemVM : ViewModelBase
    {
        int _status = 3;
        bool _checked;

        public TweakItemVM(OneOp op)
        {
            Op = op;
            _checked = op.Recommended;
            Rescan();
        }

        public OneOp Op { get; private set; }

        public string Key { get { return Op.Title; } }
        public string Group { get { return Op.Group; } }
        public string Title { get { return Op.Title; } }
        public string Desc { get { return Op.Desc; } }
        public string Tag { get { return Op.Tag; } }
        public bool NeedsAdmin { get { return Op.NeedsAdmin; } }

        /// <summary>非推荐项 = 谨慎项（默认不勾选）</summary>
        public bool Risky { get { return !Op.Recommended; } }

        public bool IsChecked
        {
            get { return _checked; }
            set { Set(ref _checked, value, "IsChecked"); }
        }

        public int Status
        {
            get { return _status; }
            private set
            {
                if (Set(ref _status, value, "Status")) RaiseAll("StatusText", "StatusKind", "StatusVisibility");
            }
        }

        public string StatusText
        {
            get
            {
                if (Status == 1) return "已优化";
                if (Status == 0) return "待优化";
                if (Status == 2) return "部分";
                return "不支持";
            }
        }

        public string StatusKind
        {
            get
            {
                if (Status == 1) return "ok";
                if (Status == 0) return "idle";
                if (Status == 2) return "warn";
                return "na";
            }
        }

        public Visibility AdminVisibility { get { return NeedsAdmin ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RiskVisibility { get { return Risky ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility TagVisibility { get { return string.IsNullOrEmpty(Tag) ? Visibility.Collapsed : Visibility.Visible; } }

        /// <summary>是否命中搜索关键字</summary>
        public bool Matches(string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            q = q.Trim();
            return Contains(Title, q) || Contains(Desc, q) || Contains(Group, q) || Contains(Tag, q);
        }

        static bool Contains(string src, string q)
        {
            return !string.IsNullOrEmpty(src) && src.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal void SetStatus(int st) { Status = st; }

        public void Rescan()
        {
            int st = 3;
            try { st = (Op.Supported != null && !Op.Supported()) ? 3 : (Op.Detect != null ? Op.Detect() : 3); }
            catch (Exception ex) { Logger.Log("Tweak", "检测失败 " + Title + ": " + ex.Message); st = 3; }
            Status = st;
        }

        public string Apply()
        {
            string e = Op.Apply != null ? Op.Apply() : null;
            Rescan();
            return e;
        }

        public string Restore()
        {
            string e = Op.Restore != null ? Op.Restore() : null;
            Rescan();
            return e;
        }
    }

    /// <summary>清理项（包裹 CleanService.CleanItem）</summary>
    public class CleanItemVM : ViewModelBase
    {
        string _size = "--";
        string _detail = "";
        bool _busy;

        public CleanItemVM(CleanItem item)
        {
            Item = item;
            IsChecked = item.Checked;
        }

        public CleanItem Item { get; private set; }

        public string Name { get { return Item.Name; } }
        public string Note { get { return Item.Note; } }
        public bool NeedsAdmin { get { return Item.Admin; } }
        public Visibility AdminVisibility { get { return Item.Admin ? Visibility.Visible : Visibility.Collapsed; } }

        /// <summary>清理项分组（列表按此分组展示）</summary>
        public string Group
        {
            get
            {
                string k = Item.Kind;
                if (k == "events") return "日志与转储";
                if (k == "recycle" || k == "windowsold") return "不可恢复项（谨慎）";
                if (k == "mru") return "使用痕迹";
                if (k == "shortcuts") return "无效快捷方式";
                if (k == "browser") return "浏览器与显卡缓存";
                if (Item.Admin) return "系统临时文件";
                return "用户临时文件与缓存";
            }
        }

        public int GroupOrder
        {
            get
            {
                string g = Group;
                if (g == "用户临时文件与缓存") return 0;
                if (g == "系统临时文件") return 1;
                if (g == "浏览器与显卡缓存") return 2;
                if (g == "使用痕迹") return 3;
                if (g == "日志与转储") return 4;
                if (g == "无效快捷方式") return 5;
                return 6;
            }
        }

        public bool IsChecked
        {
            get { return Item.Checked; }
            set { if (Item.Checked != value) { Item.Checked = value; Raise("IsChecked"); } }
        }

        public string SizeText
        {
            get { return _size; }
            set { Set(ref _size, value, "SizeText"); }
        }

        public string Detail
        {
            get { return _detail; }
            set { Set(ref _detail, value, "Detail"); }
        }

        public bool Busy
        {
            get { return _busy; }
            set { Set(ref _busy, value, "Busy"); }
        }

        public void RefreshFromItem()
        {
            CleanItem it = Item;
            if (!it.Scanned) { SizeText = "--"; Detail = ""; return; }
            SizeText = CleanService.FormatSize(it.Size);
            Detail = it.Files + " 个文件";
        }
    }

    /// <summary>启动项</summary>
    public class StartupItemVM : ViewModelBase
    {
        bool _enabled;

        public StartupItemVM(StartupEntry e)
        {
            Entry = e;
            _enabled = e.Enabled;
        }

        public StartupEntry Entry { get; private set; }
        public string Name { get { return Entry.Name; } }
        public string Command { get { return Entry.Command; } }
        public string Loc { get { return Entry.Loc; } }

        public string SourceText
        {
            get
            {
                if (Entry.Source == "reg") return "注册表";
                if (Entry.Source == "folder") return "启动文件夹";
                if (Entry.Source == "task") return "计划任务";
                return Entry.Source;
            }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set { Set(ref _enabled, value, "Enabled"); Raise("StateText"); }
        }

        public string StateText { get { return _enabled ? "已启用" : "已禁用"; } }
    }

    /// <summary>服务建议项</summary>
    public class ServiceItemVM : ViewModelBase
    {
        public ServiceItemVM(ServiceManager.SvcView v)
        {
            View = v;
        }

        public ServiceManager.SvcView View { get; private set; }
        public string Name { get { return View.Name; } }
        public string Display { get { return View.Display; } }
        public string Why { get { return View.Why; } }
        public string CurStart { get { return View.CurStart; } }
        public string RecStart { get { return View.RecStart; } }
    }

    /// <summary>已安装软件</summary>
    public class SoftwareItemVM
    {
        public SoftwareItemVM(SoftInfo s) { Info = s; }
        public SoftInfo Info { get; private set; }
        public string Name { get { return Info.Name; } }
        public string Version { get { return Info.Version; } }
        public string Publisher { get { return Info.Publisher; } }
        public string SizeText { get { return Info.SizeText; } }
        public string From { get { return Info.StoreApp ? "应用商店" : Info.From; } }
    }

    /// <summary>快照</summary>
    public class SnapshotItemVM
    {
        public SnapshotItemVM(SnapshotInfo s) { Info = s; }
        public SnapshotInfo Info { get; private set; }
        public string Display { get { return Info.Display; } }
        public string Stamp { get { return Info.Stamp; } }
        public string Detail { get { return Info.RegFiles + " 项注册表备份" + (Info.HasServices ? " · 含服务快照" : "") + (Info.HasRestorePoint ? " · 含系统还原点" : ""); } }
    }
}
