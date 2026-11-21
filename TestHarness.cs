using Microsoft.Web.WebView2.Core;

namespace AMWebShell;

/// <summary>
/// 自动化自测（仅当设置环境变量 AML_TEST=目录 时启用，正常使用不会触发）。
/// 目录下 steps.txt 每行一条指令：wait ms | size w h | js 代码 | jsa 异步表达式 | shot 名称 | pshot 名称 | ctl 动作 | hide | show | close | quit
/// </summary>
internal static class TestHarness
{
    public static void Attach(MainForm form, CoreWebView2 core)
    {
        var dir = Environment.GetEnvironmentVariable("AML_TEST");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "steps.txt"))) return;
        var log = Path.Combine(dir, "log.txt");
        File.WriteAllText(log, "");
        bool started = false;
        core.NavigationCompleted += async (_, e) =>
        {
            File.AppendAllText(log, $"[nav] ok={e.IsSuccess} status={e.WebErrorStatus} url={core.Source}\n");
            if (started || !e.IsSuccess) return;
            started = true;
            foreach (var raw in File.ReadAllLines(Path.Combine(dir, "steps.txt")))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var sp = line.IndexOf(' ');
                var cmd = sp < 0 ? line : line[..sp];
                var arg = sp < 0 ? "" : line[(sp + 1)..];
                string result = "";
                try
                {
                    switch (cmd)
                    {
                        case "wait": await Task.Delay(int.Parse(arg)); break;
                        case "size":
                            var p = arg.Split(' ').Select(int.Parse).ToArray();
                            form.WindowState = FormWindowState.Normal; form.Size = new Size(p[0], p[1]);
                            await Task.Delay(1500);
                            result = $"client={form.ClientSize}";
                            break;
                        case "js": result = await core.ExecuteScriptAsync(arg); break;
                        case "jsa":
                            await core.ExecuteScriptAsync($"window.__t=undefined;Promise.resolve(({arg})).then(r=>window.__t=JSON.stringify(r),e=>window.__t='ERR '+e)");
                            for (int i = 0; i < 40; i++)
                            {
                                await Task.Delay(250);
                                result = await core.ExecuteScriptAsync("window.__t");
                                if (result != "null") break;
                            }
                            break;
                        case "shot":
                            using (var fs = File.Create(Path.Combine(dir, arg + ".png")))
                                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                            break;
                        case "pshot":
                            if (form.LastPopup is { IsDisposed: false } pop && pop.View.CoreWebView2 is { } pc)
                            {
                                using var fs2 = File.Create(Path.Combine(dir, arg + ".png"));
                                await pc.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs2);
                                result = $"popup url={pc.Source} size={pop.ClientSize}";
                            }
                            else result = "no popup";
                            break;
                        case "ctl": form.Control(arg); break;
                        case "hide": form.Close(); break;
                        case "show": form.ShowFromTray(); break;
                        case "title": result = form.Text; break;
                        case "quit": File.AppendAllText(log, "[quit]\n"); form.ExitApp(); return;
                    }
                }
                catch (Exception ex) { result = "EXC " + ex.Message; }
                File.AppendAllText(log, $"> {Truncate(line, 120)}\n{result}\n");
            }
        };
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
