using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AMWebShell;

/// <summary>登录等弹窗：同样无标题栏融入背景、无浏览器控件，与主窗口共享登录状态。</summary>
internal sealed class PopupForm : Form
{
    public readonly WebView2 View;
    private Color _bg;

    public PopupForm(Icon icon, Color bg)
    {
        _bg = bg;
        Text = "AM Web Shell";
        Icon = icon;
        BackColor = bg;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(680));
        View = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = bg };
        Controls.Add(View);
        HandleCreated += (_, _) => Theme.ApplyCaption(Handle, _bg, frameChanged: true);
        FormClosed += (_, _) => Theme.TrimMemory(trimWebViewProcesses: false);
    }

    public async Task<CoreWebView2> InitAsync(CoreWebView2Environment env)
    {
        await View.EnsureCoreWebView2Async(env);
        var core = View.CoreWebView2;
        Web.Configure(core);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Web.InjectedScript);
        core.WebMessageReceived += OnWebMessage;
        core.WindowCloseRequested += (_, _) => Close();
        core.DocumentTitleChanged += (_, _) => Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "AM Web Shell" : core.DocumentTitle;
        return core;
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
                    }
                    break;
                case "winctl":
                    switch (r.GetProperty("a").GetString())
                    {
                        case "drag": Theme.BeginNcDrag(Handle, Theme.HTCAPTION); break;
                        case "resize-t": if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOP); break;
                        case "resize-tl": if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOPLEFT); break;
                        case "resize-tr": if (WindowState == FormWindowState.Normal) Theme.BeginNcDrag(Handle, Theme.HTTOPRIGHT); break;
                        case "min": WindowState = FormWindowState.Minimized; break;
                        case "max": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                        case "close": Close(); break;
                    }
                    break;
            }
        }
        catch { }
    }

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
        base.WndProc(ref m);
    }
}
