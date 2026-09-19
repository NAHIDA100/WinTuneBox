using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace WinTune.Wpf
{
    /// <summary>轻量 MVVM 基类：属性变更通知 + 批量刷新</summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        protected void RaiseAll(params string[] names)
        {
            foreach (string n in names) Raise(n);
        }

        protected bool Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    /// <summary>导航项（数据驱动侧边栏）</summary>
    public class NavItem : ViewModelBase
    {
        bool _selected;
        bool _compact;

        public NavItem(string key, string title, string glyph, string group)
        {
            Key = key; Title = title; Glyph = glyph; Group = group;
        }

        public string Key { get; private set; }
        public string Title { get; private set; }
        public string Glyph { get; private set; }
        public string Group { get; private set; }

        public bool IsSelected
        {
            get { return _selected; }
            set { Set(ref _selected, value, "IsSelected"); }
        }

        /// <summary>窄窗口收起文字，仅显示图标</summary>
        public bool IsCompact
        {
            get { return _compact; }
            set { if (Set(ref _compact, value, "IsCompact")) Raise("TextVisibility"); }
        }

        public System.Windows.Visibility TextVisibility
        {
            get { return _compact ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; }
        }
    }

    /// <summary>导航分组（优化 / 工具 / 系统 / 设置）</summary>
    public class NavGroup : ViewModelBase
    {
        bool _compact;

        public NavGroup(string title, List<NavItem> items)
        {
            Title = title;
            Items = items;
        }

        public string Title { get; private set; }
        public List<NavItem> Items { get; private set; }

        public bool IsCompact
        {
            get { return _compact; }
            set { if (Set(ref _compact, value, "IsCompact")) Raise("HeaderVisibility"); }
        }

        public System.Windows.Visibility HeaderVisibility
        {
            get { return _compact ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; }
        }
    }
}