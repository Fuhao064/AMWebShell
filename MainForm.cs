using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AMWebShell;

public sealed class MainForm : Form
{
    private const string AppTitle = "AM Web Shell";
    private readonly WebView2 _web;
    private readonly string _dataDir;
    private readonly Settings _settings;
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _miNowPlaying, _miToggle, _miShow, _miCloseToTray;
    private readonly EventWaitHandle _activateEvent, _exitEvent;
    private readonly System.Windows.Forms.Timer _memTimer;
    private CoreWebView2Environment? _env;
    private Color _bg = Color.FromArgb(31, 31, 31);
    private bool _exiting;
    private string _npTitle = "", _npArtist = "";
    private bool _playing;
    private long _lastTrimTick;
    internal PopupForm? LastPopup;

    public MainForm()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMWebShell");
        Directory.CreateDirectory(_dataDir);
        _settings = new Settings(_dataDir);

        using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("AMWebShell.app.ico")!)
            _icon = new Icon(s);

        Text = AppTitle;
        Icon = _icon;
        BackColor = _bg;
        MinimumSize = new Size(360, 480);
        StartPosition = FormStartPosition.Manual;
        RestoreWindowBounds();

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = _bg };
        Controls.Add(_web);

        // —— 托盘 ——
        _miNowPlaying = new ToolStripMenuItem("未在播放") { Enabled = false };
        _miToggle = new ToolStripMenuItem("播放", null, (_, _) => Control("toggle"));
        var miPrev = new ToolStripMenuItem("上一首", null, (_, _) => Control("prev"));
        var miNext = new ToolStripMenuItem("下一首", null, (_, _) => Control("next"));
        _miShow = new ToolStripMenuItem("显示窗口", null, (_, _) => ToggleWindow());
        _miCloseToTray = new ToolStripMenuItem("关闭窗口时保留在后台", null, (_, _) =>
        {
            _settings.CloseToTray = !_settings.CloseToTray;
            _miCloseToTray!.Checked = _settings.CloseToTray;
            _settings.Save();
        }) { Checked = _settings.CloseToTray };
        var miExit = new ToolStripMenuItem($"退出 {AppTitle}", null, (_, _) => ExitApp());
        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miNowPlaying, new ToolStripSeparator(), _miToggle, miPrev, miNext, new ToolStripSeparator(),
            _miShow, _miCloseToTray, new ToolStripSeparator(), miExit,
        });
        _menu.Opening += (_, _) => _miShow.Text = Visible && WindowState != FormWindowState.Minimized ? "隐藏窗口" : "显示窗口";
        ApplyMenuTheme();

        _tray = new NotifyIcon { Icon = _icon, Text = AppTitle, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleWindow(); };
        _tray.BalloonTipClicked += (_, _) => ShowFromTray();

        // 二次启动时唤起已有窗口
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActivateEventName);
        new Thread(() =>
        {
            while (_activateEvent.WaitOne())
            {
                if (_exiting) return;
                try { BeginInvoke(ShowFromTray); } catch { return; }
            }
        }) { IsBackground = true }.Start();

        // 外部 --replace 升级退出信号
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        new Thread(() =>
        {
            if (_exitEvent.WaitOne() && !_exiting)
            {
                try { BeginInvoke(ExitApp); } catch { }
            }
        }) { IsBackground = true }.Start();

        // 周期性轻量回收冷内存页
        _memTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _memTimer.Tick += (_, _) => Theme.TrimMemory(!Visible || WindowState == FormWindowState.Minimized);
        _memTimer.Start();

        Load += async (_, _) => await InitAsync();
        Resize += (_, _) => { SyncMaxButtonState(); UpdateMemoryTarget(); };
        VisibleChanged += (_, _) => UpdateMemoryTarget();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyCaption(Handle, _bg, frameChanged: true);
    }

    private async Task InitAsync()
    {
        try
        {
            _env = await Web.GetEnvironmentAsync(_dataDir);
            await _web.EnsureCoreWebView2Async(_env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法初始化 WebView2 运行时：\n" + ex.Message +
                "\n\n请安装 Microsoft Edge WebView2 Runtime。", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitApp();
            return;
        }

        var core = _web.CoreWebView2;
        Web.Configure(core);
        if (_settings.Zoom is > 0.4 and < 2.1 && Math.Abs(_settings.Zoom - 1) > 0.01) _web.ZoomFactor = _settings.Zoom;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessage;
        core.NavigationCompleted += async (_, _) =>
        {
            SyncMaxButtonState();
            await Task.Delay(3500);
            Theme.TrimMemory(trimWebViewProcesses: false);
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Web.InjectedScript);

        TestHarness.Attach(this, core);
        UpdateMemoryTarget();
        core.Navigate(Web.StartUrl);
    }

    // 登录等弹窗在应用内的无标题栏浏览窗口中打开（保留 window.opener，登录完成后自动关闭）
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Web.IsAllowed(e.Uri)) { e.Handled = true; Web.OpenExternal(e.Uri); return; }
        using var deferral = e.GetDeferral();
        var popup = new PopupForm(_icon, _bg);
        var f = e.WindowFeatures;
        if (f.HasSize && f.Width > 200 && f.Height > 200)
            popup.ClientSize = new Size(LogicalToDeviceUnits((int)f.Width), LogicalToDeviceUnits((int)f.Height));
        popup.Show(this);
        e.NewWindow = await popup.InitAsync(_env!);
        e.Handled = true;
        LastPopup = popup;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var r = doc.RootElement;
            switch (r.GetProperty("t").GetString())
            {
                case "bg":
                    if (Theme.ParseCss(r.GetProperty("v").GetString()) is { } c && c != _bg)
                    {
                        _bg = c;
                        BackColor = c;
                        Theme.ApplyCaption(Handle, c);
                        ApplyMenuTheme();
                    }
                    break;
                case "np":
                    _npTitle = r.GetProperty("title").GetString() ?? "";
                    _npArtist = r.GetProperty("artist").GetString() ?? "";
                    _playing = r.GetProperty("playing").GetBoolean();
                    UpdateNowPlaying();
                    break;
                case "winctl":
                    HandleWindowControl(r.GetProperty("a").GetString());
                    break;
                case "idle-trim":
                    long now = Environment.TickCount64;
                    if (now - _lastTrimTick > 15_000)
                    {
                        _lastTrimTick = now;
                        Theme.TrimMemory(trimWebViewProcesses: false);
                    }
                    break;
            }
        }
        catch { }
    }

    private void HandleWindowControl(string? action)
    {
        switch (action)
        {
            case "drag":
                Theme.BeginNcDrag(Handle, Theme.HTCAPTION);
                break;
            case "resize-t":
                if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOP);
                break;
            case "resize-tl":
                if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOPLEFT);
                break;
            case "resize-tr":
                if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOPRIGHT);
                break;
            case "min":
                WindowState = FormWindowState.Minimized;
                break;
            case "max":
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                SyncMaxButtonState();
                break;
            case "close":
                Close();
                break;
        }
    }

    private void SyncMaxButtonState()
    {
        var core = _web.CoreWebView2;
        if (core == null) return;
        bool isMax = WindowState == FormWindowState.Maximized;
        try { _ = core.ExecuteScriptAsync($"window.__amlSetMax&&window.__amlSetMax({(isMax ? "true" : "false")})"); } catch { }
    }

    private void UpdateNowPlaying()
    {
        var song = string.IsNullOrEmpty(_npTitle) ? "" :
            string.IsNullOrEmpty(_npArtist) ? _npTitle : $"{_npTitle} - {_npArtist}";
        Text = song.Length == 0 ? AppTitle : $"{song}  ·  {AppTitle}";
        _miNowPlaying.Text = song.Length == 0 ? "未在播放" : (_playing ? "♪ " : "❚❚ ") + Truncate(song, 48);
        _miToggle.Text = _playing ? "暂停" : "播放";
        var tip = song.Length == 0 ? AppTitle : $"{(_playing ? "正在播放" : "已暂停")}：{song}";
        _tray.Text = Truncate(tip, 120);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    internal async void Control(string action)
    {
        var core = _web.CoreWebView2;
        if (core == null) return;
        try { await core.ExecuteScriptAsync(Web.ControlScript(action)); } catch { }
    }

    private void ApplyMenuTheme() => _menu.Renderer = new Theme.MenuRenderer(Theme.IsDark(_bg));

    // —— 窗口 / 托盘行为 ——
    private void ToggleWindow()
    {
        if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus) HideToTray();
        else ShowFromTray();
    }

    internal void ShowFromTray()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = _settings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void HideToTray()
    {
        SaveWindowBounds();
        Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            if (!_settings.CloseHintShown)
            {
                _settings.CloseHintShown = true;
                _settings.Save();
                _tray.ShowBalloonTip(3000, $"{AppTitle} 仍在后台运行",
                    "音乐会继续播放。可在托盘图标右键菜单中退出，或取消“关闭窗口时保留在后台”。", ToolTipIcon.None);
            }
            return;
        }
        _exiting = true;
        SaveWindowBounds();
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    internal void ExitApp()
    {
        _exiting = true;
        _activateEvent.Set();
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _memTimer.Dispose(); _tray.Dispose(); _menu.Dispose(); _icon.Dispose(); }
        base.Dispose(disposing);
    }

    // 后退/前进：Alt+←/→；刷新：Ctrl+R；回到首页：Alt+Home
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var core = _web.CoreWebView2;
        if (core != null)
        {
            switch (keyData)
            {
                case Keys.Alt | Keys.Left: if (core.CanGoBack) core.GoBack(); return true;
                case Keys.Alt | Keys.Right: if (core.CanGoForward) core.GoForward(); return true;
                case Keys.Control | Keys.R: core.Reload(); return true;
                case Keys.Alt | Keys.Home: core.Navigate(Web.StartUrl); return true;
                // 页面缩放（小窗口时可缩小内容），Ctrl+0 恢复
                case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Add: SetZoom(_web.ZoomFactor + 0.1); return true;
                case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: SetZoom(_web.ZoomFactor - 0.1); return true;
                case Keys.Control | Keys.D0: case Keys.Control | Keys.NumPad0: SetZoom(1.0); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 移除原生顶部标题栏（融入背景）+ 鼠标侧键后退/前进
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Theme.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            int savedLeft = Marshal.ReadInt32(m.LParam, 0);
            int savedTop = Marshal.ReadInt32(m.LParam, 4);
            base.WndProc(ref m);
            Theme.StripTopTitleBar(Handle, m.LParam, savedLeft, savedTop);
            return;
        }

        const int WM_APPCOMMAND = 0x0319;
        if (m.Msg == WM_APPCOMMAND && _web.CoreWebView2 is { } core)
        {
            int cmd = (int)(((long)m.LParam >> 16) & 0x0FFF);
            if (cmd == 1 && core.CanGoBack) { core.GoBack(); m.Result = 1; return; }
            if (cmd == 2 && core.CanGoForward) { core.GoForward(); m.Result = 1; return; }
        }
        base.WndProc(ref m);
    }

    private void SetZoom(double z)
    {
        z = Math.Clamp(Math.Round(z, 1), 0.5, 2.0);
        _web.ZoomFactor = z;
        _settings.Zoom = z;
        _settings.Save();
    }

    private void UpdateMemoryTarget()
    {
        bool lowPower = !Visible || WindowState == FormWindowState.Minimized;
        try
        {
            using var self = Process.GetCurrentProcess();
            self.PriorityClass = lowPower ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.AboveNormal;
        }
        catch { }

        var core = _web.CoreWebView2;
        if (core == null) return;
        // 最小化或隐藏到托盘时让 Chromium 与宿主主动回收内存（不会中断播放）
        core.MemoryUsageTargetLevel = lowPower
            ? CoreWebView2MemoryUsageTargetLevel.Low
            : CoreWebView2MemoryUsageTargetLevel.Normal;

        if (lowPower)
        {
            try { _ = core.ExecuteScriptAsync("typeof gc==='function'&&gc()"); } catch { }
            Theme.TrimMemory(trimWebViewProcesses: true);
        }
    }

    private void RestoreWindowBounds()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(1200, area.Width), Math.Min(800, area.Height));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        if (_settings.Bounds is { } r && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) Bounds = r;
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        if (WindowState == FormWindowState.Minimized) { _settings.Save(); return; }
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Save();
    }

    internal WebView2 View => _web;
}
