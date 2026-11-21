using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AMWebShell;

/// <summary>无标题栏沉浸式窗口（Windows 11 DWM）、多线程性能调优、内存工作集回收与深色菜单渲染。</summary>
internal static class Theme
{
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWCP_ROUND = 2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static bool IsDark(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128;

    /// <summary>让无标题栏窗口的边框、圆角、阴影与网页背景完全融为一体。</summary>
    public static void ApplyCaption(IntPtr hwnd, Color bg, bool frameChanged = false)
    {
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark(bg) ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        int caption = ToColorRef(bg);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref caption, 4);
        int text = ToColorRef(dark == 1 ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);

        var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        if (frameChanged)
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 在保留 Windows 11 原生缩放边框、圆角、阴影与 Aero Snap 的前提下，完全移除顶部标题栏。
    /// 调用前先保存原始 top/left，调用 base.WndProc 后恢复 top 到窗口顶部。
    /// </summary>
    public static void StripTopTitleBar(IntPtr hwnd, IntPtr lParam, int savedLeft, int savedTop)
    {
        int newLeft = Marshal.ReadInt32(lParam, 0);
        int border = Math.Max(0, newLeft - savedLeft);
        int top = IsZoomed(hwnd) ? savedTop + border : savedTop + 1;
        Marshal.WriteInt32(lParam, 4, top);
    }

    /// <summary>发起原生窗口拖动或顶部边缘缩放（支持 Windows 11 贴靠布局与多屏拖拽）。</summary>
    public static void BeginNcDrag(IntPtr hwnd, int hitTest = HTCAPTION)
    {
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
    }

    /// <summary>
    /// 配置多线程并发线程池，并清理会导致双显卡笔记本 PlayReady DRM 音频输出保护（IDXGIOutput）失败的 WebView2 强绑注册表项。
    /// </summary>
    public static void ConfigurePerformanceAndGpu()
    {
        try
        {
            int cores = Math.Max(4, Environment.ProcessorCount);
            ThreadPool.GetMinThreads(out int worker, out int io);
            ThreadPool.SetMinThreads(Math.Max(worker, cores * 2), Math.Max(io, cores));
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (name.Contains("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("AppleMusic.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try { key.DeleteValue(name, false); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>主动回收宿主（及后台最小化时的 WebView2）工作集中的冷内存页，降低物理内存占用且不影响前台播放。</summary>
    public static void TrimMemory(bool trimWebViewProcesses = false)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
                using var self = Process.GetCurrentProcess();
                EmptyWorkingSet(self.Handle);
            }
            catch { }

            if (!trimWebViewProcesses) return;
            try
            {
                foreach (var p in Process.GetProcessesByName("msedgewebview2"))
                {
                    using (p)
                    {
                        try { EmptyWorkingSet(p.Handle); } catch { }
                    }
                }
            }
            catch { }
        });
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>解析 CSS 的 rgb()/rgba() 颜色。</summary>
    public static Color? ParseCss(string? css)
    {
        if (string.IsNullOrEmpty(css)) return null;
        var s = css.IndexOf('('); var e = css.IndexOf(')');
        if (s < 0 || e < s) return null;
        var parts = css[(s + 1)..e].Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        try
        {
            if (parts.Length >= 4 && double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) < 0.5) return null;
            return Color.FromArgb((int)double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                (int)double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                (int)double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { return null; }
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer(bool dark) : base(new MenuColors(dark)) { RoundedEdges = false; _dark = dark; }
        private readonly bool _dark;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Color.Gray : _dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(25, 25, 25);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle; r.Inflate(-2, -2);
            using var pen = new Pen(Color.FromArgb(250, 45, 72), 2f);
            g.DrawLines(pen, new[] { new PointF(r.Left + r.Width * .15f, r.Top + r.Height * .5f),
                new PointF(r.Left + r.Width * .42f, r.Bottom - r.Height * .18f), new PointF(r.Right - r.Width * .1f, r.Top + r.Height * .2f) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _dark ? Color.Gainsboro : Color.DimGray;
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        private readonly Color _bg, _hl, _border, _sep;
        public MenuColors(bool dark)
        {
            _bg = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
            _hl = dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(228, 228, 228);
            _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210);
            _sep = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(220, 220, 220);
            UseSystemColors = false;
        }
        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _hl;
        public override Color MenuItemSelected => _hl;
        public override Color MenuItemSelectedGradientBegin => _hl;
        public override Color MenuItemSelectedGradientEnd => _hl;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
        public override Color SeparatorDark => _sep;
        public override Color SeparatorLight => _sep;
    }
}
