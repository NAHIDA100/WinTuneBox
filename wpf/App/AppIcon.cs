using System;
using System.Drawing;
using System.Windows;

namespace WinTune.Wpf
{
    /// <summary>应用图标（多尺寸 ico，按需取用托盘/标题栏尺寸）</summary>
    public static class AppIcon
    {
        static Icon _base;

        static Icon Base
        {
            get
            {
                if (_base != null) return _base;
                try
                {
                    var res = Application.GetResourceStream(new Uri("assets/app.ico", UriKind.Relative));
                    if (res != null) _base = new Icon(res.Stream);
                }
                catch (Exception ex) { Logger.Log("Icon", "加载图标失败: " + ex.Message); }
                return _base;
            }
        }

        /// <summary>取指定尺寸图标（失败返回 null，调用方需容错）</summary>
        public static Icon Get(int size)
        {
            Icon b = Base;
            if (b == null) return null;
            try { return new Icon(b, new System.Drawing.Size(size, size)); }
            catch { return b; }
        }
    }
}